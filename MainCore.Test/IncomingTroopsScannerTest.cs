using System.Text.Json;

using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class IncomingTroopsScannerTest : IDisposable
    {
        private sealed class Clock : TimeProvider
        {
            public long Seconds { get; set; }
            public override long TimestampFrequency => 1;
            public override long GetTimestamp() => Seconds;
        }

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-incoming-scan-{Guid.NewGuid():N}");
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly Clock _clock = new();
        private readonly IPage _page = Substitute.For<IPage>();
        private readonly IJSHandle _tooltip = Substitute.For<IJSHandle>();
        private readonly ReportNotificationOutbox _outbox;
        private readonly IncomingTroopsScanner _scanner;
        private readonly TaskManager _taskManager;

        public IncomingTroopsScannerTest()
        {
            Directory.CreateDirectory(_directory);
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_directory, "account.db")};Pooling=False").Options;
            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();
            context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
            context.AccountsSetting.Add(new AccountSetting
                { AccountId = 1, Setting = AccountSettingEnums.EnableIncomingTroopsNotifications, Value = 1 });
            context.SaveChanges();
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(_options));
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(_page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            _page.Mouse.Returns(Substitute.For<IMouse>());
            _page.Locator(Arg.Any<string>(), Arg.Any<PageLocatorOptions>()).Returns(Substitute.For<ILocator>());
            _page.WaitForFunctionAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<PageWaitForFunctionOptions>())
                .Returns(_tooltip);
            _tooltip.JsonValueAsync<string>().Returns("Incoming attacks: 1");
            _outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var monitor = new IncomingTroopsMonitor(_outbox, Substitute.For<Serilog.ILogger>(), _clock);
            _taskManager = new TaskManager(new RxQueue(factory), factory, Substitute.For<Serilog.ILogger>(),
                new ReportMonitor(_outbox, Substitute.For<Serilog.ILogger>()), monitor);
            _scanner = new IncomingTroopsScanner(browser,
                monitor, new DataService { AccountData = "test" }, factory, _taskManager);
        }

        private void Snapshot(string json) =>
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns(JsonDocument.Parse(json).RootElement.Clone());

        [Fact]
        public async Task DisabledFeatureDoesNotReadDom()
        {
            using var context = new AppDbContext(_options);
            context.AccountsSetting.Single().Value = 0;
            context.SaveChanges();
            await _scanner.Scan(new(1), CancellationToken.None);
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task ThrottlesAttemptsAndPreservesStateAcrossInvalidSnapshot()
        {
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":true}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            var first = _outbox.GetNext(DateTime.UtcNow)!;
            _outbox.Complete(first.Id);
            _clock.Seconds = 59;
            await _scanner.Scan(new(1), CancellationToken.None);
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());

            _clock.Seconds = 60;
            Snapshot("null");
            await _scanner.Scan(new(1), CancellationToken.None);
            _clock.Seconds = 120;
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":true}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public async Task PlaywrightFailureDoesNotThrowOrBypassThrottle()
        {
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns<JsonElement>(_ => throw new PlaywrightException("Page changed"));
            await _scanner.Scan(new(1), CancellationToken.None);
            _clock.Seconds = 59;
            await _scanner.Scan(new(1), CancellationToken.None);
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task TooltipFailureForOneVillageDoesNotBlockOtherVillages()
        {
            Snapshot("""[{"Id":1,"Name":"v1","Coordinates":"","Attack":true},{"Id":2,"Name":"v2","Coordinates":"","Attack":true}]""");
            _page.WaitForFunctionAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<PageWaitForFunctionOptions>())
                .Returns(call => (int)call[1] == 1 ? throw new PlaywrightException("Tooltip missing") : _tooltip);
            await _scanner.Scan(new(1), CancellationToken.None);
            var message = _outbox.GetNext(DateTime.UtcNow)!;
            message.Text.ShouldContain("Village: v2");
            message.Text.ShouldContain("Incoming attacks: 0 -> 1");
        }

        [Fact]
        public async Task CountIncreaseNotifiesAndInvalidTooltipPreservesPreviousCounts()
        {
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":true}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            _outbox.Complete(_outbox.GetNext(DateTime.UtcNow)!.Id);
            _clock.Seconds = 60;
            _tooltip.JsonValueAsync<string>().Returns("Loading...");
            await _scanner.Scan(new(1), CancellationToken.None);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            _clock.Seconds = 120;
            _tooltip.JsonValueAsync<string>().Returns("Incoming attacks: 2\nIncoming raids: 1");
            await _scanner.Scan(new(1), CancellationToken.None);
            var message = _outbox.GetNext(DateTime.UtcNow)!;
            message.Text.ShouldContain("Incoming attacks: 1 -> 2");
            message.Text.ShouldContain("Incoming raids: 0 -> 1");
        }

        [Fact]
        public async Task CreatesSingleFallbackAndPostponesItOnlyWhenScanning()
        {
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":false}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            var fallback = _taskManager.GetTaskList(new(1)).OfType<MainCore.Tasks.CheckIncomingTroopsTask.Task>().Single();
            fallback.ExecuteAt.ShouldBeInRange(DateTime.Now.AddMinutes(9.9), DateTime.Now.AddMinutes(10.1));

            _clock.Seconds = 30;
            await _scanner.Scan(new(1), CancellationToken.None);
            _taskManager.GetTaskList(new(1)).Count.ShouldBe(1);
            fallback.ExecuteAt.ShouldBeInRange(DateTime.Now.AddSeconds(569), DateTime.Now.AddSeconds(571));

            _clock.Seconds = 60;
            await _scanner.Scan(new(1), CancellationToken.None);
            _taskManager.GetTaskList(new(1)).Count.ShouldBe(1);
            fallback.ExecuteAt.ShouldBeInRange(DateTime.Now.AddMinutes(9.9), DateTime.Now.AddMinutes(10.1));
        }

        [Fact]
        public async Task DisablingRemovesFallback()
        {
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":false}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            using var context = new AppDbContext(_options);
            context.AccountsSetting.Single().Value = 0;
            context.SaveChanges();
            await _scanner.Scan(new(1), CancellationToken.None);
            _taskManager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Fact]
        public async Task FallbackCanRepeatWithoutAnyOtherTasks()
        {
            Snapshot("""[{"Id":1,"Name":"v","Coordinates":"","Attack":false}]""");
            await _scanner.Scan(new(1), CancellationToken.None);
            var fallback = _taskManager.GetTaskList(new(1)).OfType<MainCore.Tasks.CheckIncomingTroopsTask.Task>().Single();
            var handler = new MainCore.Tasks.CheckIncomingTroopsTask.HandleBehavior();
            _clock.Seconds = 600;
            (await handler.HandleAsync(fallback, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            await _scanner.Scan(new(1), CancellationToken.None);
            await _page.Received(2).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _taskManager.GetTaskList(new(1)).Single().ShouldBeSameAs(fallback);
            fallback.ExecuteAt.ShouldBeInRange(DateTime.Now.AddMinutes(9.9), DateTime.Now.AddMinutes(10.1));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
