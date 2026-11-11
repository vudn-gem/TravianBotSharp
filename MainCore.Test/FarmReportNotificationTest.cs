using System.Text.Json;

using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Commands.Navigate;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class FarmReportNotificationTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-farm-reports-{Guid.NewGuid():N}");
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly AccountId _accountId = new(1);
        private readonly IPage _page = Substitute.For<IPage>();
        private readonly IChromeBrowser _browser = Substitute.For<IChromeBrowser>();
        private readonly IHandler<ToFarmListPageCommand.Command, Result> _navigate =
            Substitute.For<IHandler<ToFarmListPageCommand.Command, Result>>();
        private readonly ReportNotificationOutbox _outbox;
        private readonly StartFarmListTask.HandleBehavior _handler;

        public FarmReportNotificationTest()
        {
            Directory.CreateDirectory(_directory);
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_directory, "account.db")};Pooling=False")
                .Options;
            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();
            context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
            context.AccountsSetting.AddRange(
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.EnableReportNotifications, Value = 1 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.UseStartAllButton, Value = 1 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.FarmIntervalMin, Value = 120 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.FarmIntervalMax, Value = 180 });
            context.SaveChanges();

            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(_options));
            _browser.CurrentPage.Returns(_page);
            _browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            _page.Locator(Arg.Any<string>(), Arg.Any<PageLocatorOptions>()).Returns(Substitute.For<ILocator>());
            _browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(Result.Ok());
            _navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(Result.Ok());
            _outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var monitor = new ReportMonitor(_outbox, Substitute.For<Serilog.ILogger>());
            var scanner = new ReportBadgeScanner(_browser, monitor,
                new DataService { AccountId = _accountId, AccountData = "test" }, factory,
                new LostReportTargetScanner(_browser));
            _handler = new StartFarmListTask.HandleBehavior(new StartFarmListTask(factory, _browser, _navigate, scanner));
        }

        private void Badge(string json) =>
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns(JsonDocument.Parse(json).RootElement.Clone());

        private void Setting(AccountSettingEnums setting, int value)
        {
            using var context = new AppDbContext(_options);
            context.AccountsSetting.Single(x => x.AccountId == 1 && x.Setting == setting).Value = value;
            context.SaveChanges();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task OpensFarmPageThenScansBeforeSendingAndUsesFarmInterval(bool startAll)
        {
            Setting(AccountSettingEnums.UseStartAllButton, startAll ? 1 : 0);
            if (!startAll)
            {
                using var context = new AppDbContext(_options);
                context.FarmLists.Add(new Farm { Id = 10, AccountId = 1, Name = "active", IsActive = true });
                context.SaveChanges();
            }
            var clicked = false;
            var scanned = false;
            var navigations = 0;
            _navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    if (++navigations % 2 == 0) scanned.ShouldBeTrue();
                    return Result.Ok();
                });
            _browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(_ =>
            {
                scanned.ShouldBeTrue();
                (navigations % 2).ShouldBe(0);
                clicked = true;
                return Result.Ok();
            });
            var json = "{\"valid\":true,\"text\":null}";
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>()).Returns(_ =>
            {
                clicked.ShouldBeFalse();
                scanned = true;
                return JsonDocument.Parse(json).RootElement.Clone();
            });
            var task = new StartFarmListTask.Task(_accountId);
            var before = DateTime.Now;
            (await _handler.HandleAsync(task, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            task.ExecuteAt.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(120));
            task.ExecuteAt.ShouldBeLessThanOrEqualTo(DateTime.Now.AddSeconds(180));
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();

            clicked = false;
            scanned = false;
            json = "{\"valid\":true,\"text\":\"1\"}";
            (await _handler.HandleAsync(task, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("0 -> 1");
        }

        [Theory]
        [InlineData("navigation")]
        [InlineData("inactive")]
        public async Task FailedInitialNavigationOrInactiveListsDoNotScan(string failure)
        {
            Badge("{\"valid\":true,\"text\":\"1\"}");
            if (failure == "navigation")
                _navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Fail("Navigation failed"));
            if (failure == "inactive")
                Setting(AccountSettingEnums.UseStartAllButton, 0);

            (await _handler.HandleAsync(new(_accountId), CancellationToken.None)).IsFailed.ShouldBeTrue();
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task FarmClickFailureOccursAfterBadgeScan()
        {
            Badge("""{"valid":true,"text":"1"}""");
            _browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(Result.Fail("Click failed"));
            (await _handler.HandleAsync(new(_accountId), CancellationToken.None)).IsFailed.ShouldBeTrue();
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task DoesNotSendFarmWhenReestablishingPageAfterScanFails()
        {
            Badge("""{"valid":true,"text":"1"}""");
            _navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(Result.Ok(), Result.Fail("Farm page unavailable after reports"));
            (await _handler.HandleAsync(new(_accountId), CancellationToken.None)).IsFailed.ShouldBeTrue();
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            await _browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Fact]
        public async Task DisabledReportsDoNotScanOrChangeFarmSchedule()
        {
            Setting(AccountSettingEnums.EnableReportNotifications, 0);
            var task = new StartFarmListTask.Task(_accountId);
            (await _handler.HandleAsync(task, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            task.ExecuteAt.ShouldBeGreaterThan(DateTime.Now.AddSeconds(119));
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task AutomationOnlyRunsOnChangesAndIgnoresItsOwnBadgeDecrease()
        {
            Setting(AccountSettingEnums.EnableReportNotifications, 0);
            using (var context = new AppDbContext(_options))
            {
                context.AccountsSetting.Add(new AccountSetting
                {
                    AccountId = 1, Setting = AccountSettingEnums.EnableAutoDeactivateLostReportTargets, Value = 1
                });
                context.SaveChanges();
            }
            var locator = Substitute.For<ILocator>();
            locator.GetAttributeAsync("data-did", Arg.Any<LocatorGetAttributeOptions>()).Returns("1");
            _page.Locator(Arg.Any<string>(), Arg.Any<PageLocatorOptions>()).Returns(locator);
            _page.Url.Returns("https://example.com/farm");
            _browser.Navigate(Arg.Any<string>()).Returns(Result.Ok());
            var reportClicks = 0;
            var events = new List<string>();
            _navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    events.Add("farm-page");
                    return Result.Ok();
                });
            _browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(call =>
            {
                if (((string)call[1]).Contains("a.reports", StringComparison.Ordinal))
                {
                    events.Add("process-reports");
                    reportClicks++;
                    Badge("""{"valid":true,"text":"2"}""");
                    return Result.Fail("Report page unavailable");
                }
                events.Add("start-farm");
                return Result.Ok();
            });
            var task = new StartFarmListTask.Task(_accountId);
            Badge("""{"valid":true,"text":"3"}""");
            await _handler.HandleAsync(task, CancellationToken.None);
            reportClicks.ShouldBe(0);
            events.Clear();
            Badge("""{"valid":true,"text":"4"}""");
            await _handler.HandleAsync(task, CancellationToken.None);
            reportClicks.ShouldBe(1);
            events.ShouldBe(new[] { "farm-page", "process-reports", "farm-page", "start-farm" });
            await _handler.HandleAsync(task, CancellationToken.None);
            reportClicks.ShouldBe(1);
            Badge("""{"valid":true,"text":"1"}""");
            await _handler.HandleAsync(task, CancellationToken.None);
            reportClicks.ShouldBe(2);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public async Task FailedScanKeepsBaselineAndFarmingContinues()
        {
            Badge("{\"valid\":true,\"text\":null}");
            var task = new StartFarmListTask.Task(_accountId);
            await _handler.HandleAsync(task, CancellationToken.None);

            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns<JsonElement>(_ => throw new PlaywrightException("Page changed"));
            (await _handler.HandleAsync(task, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            task.ExecuteAt.ShouldBeGreaterThan(DateTime.Now.AddSeconds(119));

            Badge("{\"valid\":true,\"text\":\"1\"}");
            await _handler.HandleAsync(task, CancellationToken.None);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("0 -> 1");
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
