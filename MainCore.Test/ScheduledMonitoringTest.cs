using System.Text.Json;
using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Commands.Navigate;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.UI.Models.Input;
using MainCore.UI.Models.Validators;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class ScheduledMonitoringTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-monitor-{Guid.NewGuid():N}");
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly IPage _page = Substitute.For<IPage>();
        private readonly IChromeBrowser _browser = Substitute.For<IChromeBrowser>();
        private readonly IHandler<ToDorfCommand.Command, Result> _toDorf =
            Substitute.For<IHandler<ToDorfCommand.Command, Result>>();
        private readonly ReportNotificationOutbox _outbox;
        private readonly TaskManager _manager;
        private readonly ScheduledMonitoringTask.HandleBehavior _handler;
        private readonly Serilog.ILogger _logger = Substitute.For<Serilog.ILogger>();
        private int _badge;
        private bool _incoming;

        public ScheduledMonitoringTest()
        {
            Directory.CreateDirectory(_directory);
            _options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_directory, "account.db")};Pooling=False").Options;
            using var context = new AppDbContext(_options);
            context.Database.EnsureCreated();
            context.Accounts.AddRange(
                new Account { Id = 1, Username = "one", Server = "https://example.com" },
                new Account { Id = 2, Username = "two", Server = "https://example.com" });
            context.SaveChanges();
            context.FillAccountSettings();
            _factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            _factory.CreateDbContext().Returns(_ => new AppDbContext(_options));
            _browser.CurrentPage.Returns(_page);
            _browser.CurrentUrl.Returns("https://example.com/dorf1.php");
            _browser.Logger.Returns(_logger);
            _toDorf.HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns(call => JsonDocument.Parse(((string)call[0]).Contains("a.reports")
                    ? $"{{\"valid\":true,\"text\":\"{_badge}\"}}"
                    : $"[{{\"Id\":1,\"Name\":\"Village\",\"Coordinates\":\"\",\"Attack\":{_incoming.ToString().ToLowerInvariant()}}}]").RootElement.Clone());
            _page.Mouse.Returns(Substitute.For<IMouse>());
            _page.Locator(Arg.Any<string>(), Arg.Any<PageLocatorOptions>()).Returns(Substitute.For<ILocator>());
            var tooltip = Substitute.For<IJSHandle>();
            tooltip.JsonValueAsync<string>().Returns("Incoming attacks: 1");
            _page.WaitForFunctionAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<PageWaitForFunctionOptions>())
                .Returns(tooltip);
            _outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var reportMonitor = new ReportMonitor(_outbox, _logger);
            var incomingMonitor = new IncomingTroopsMonitor(_outbox, TimeProvider.System);
            _manager = new TaskManager(new RxQueue(_factory), _factory, _logger, reportMonitor, incomingMonitor);
            var reportScanner = new ReportBadgeScanner(_browser, reportMonitor, new DataService { AccountData = "one" },
                _factory, new LostReportTargetScanner(_browser));
            var incomingScanner = new IncomingTroopsScanner(_browser, incomingMonitor,
                new DataService { AccountData = "one" }, _factory, _manager);
            _handler = new ScheduledMonitoringTask.HandleBehavior(
                new ScheduledMonitoringTask(_factory, _browser, _toDorf, reportScanner, incomingScanner));
        }

        private void Set(int account, AccountSettingEnums key, int value)
        {
            using var context = new AppDbContext(_options);
            context.SaveAccountSetting(new AccountId(account), new() { [key] = value });
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task ScheduledScanRequiresEachNotificationSwitch(bool report, bool incoming)
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, report ? 1 : 0);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, incoming ? 1 : 0);
            Set(1, AccountSettingEnums.EnableAutoDeactivateLostReportTargets, 1);
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes, 2);
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes, 4);
            var task = new ScheduledMonitoringTask.Task(new(1));
            var before = DateTime.Now;
            var result = await _handler.HandleAsync(task, CancellationToken.None);
            if (report || incoming)
            {
                result.IsSuccess.ShouldBeTrue();
                task.ExecuteAt.ShouldBeInRange(before.AddMinutes(2), DateTime.Now.AddMinutes(4));
            }
            else result.IsFailed.ShouldBeTrue();
            await _toDorf.Received(report || incoming ? 1 : 0)
                .HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>());
            await _page.Received(report ? 1 : 0).EvaluateAsync<JsonElement>(
                Arg.Is<string>(script => script.Contains("a.reports")), Arg.Any<object>());
            await _page.Received(incoming ? 1 : 0).EvaluateAsync<JsonElement>(
                Arg.Is<string>(script => script.Contains(".villageList")), Arg.Any<object>());
            _logger.Received(incoming ? 1 : 0).Information(
                "Incoming troops scanned: {VillageName}, {AttackCount} attacks, {RaidCount} raids",
                "Village", 0, 0);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            _manager.GetTaskList(new(1)).OfType<CheckIncomingTroopsTask.Task>().Count().ShouldBe(incoming ? 1 : 0);
        }

        [Fact]
        public async Task ScheduledScanBypassesIncomingThrottleAndHonorsReportNotifications()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 1);
            var task = new ScheduledMonitoringTask.Task(new(1));
            await _handler.HandleAsync(task, CancellationToken.None);
            _badge = 2;
            await _handler.HandleAsync(task, CancellationToken.None);
            await _page.Received(4).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("0 -> 2");
        }

        [Fact]
        public async Task DisabledScheduleDoesNotScan()
        {
            var result = await _handler.HandleAsync(new ScheduledMonitoringTask.Task(new(1)), CancellationToken.None);
            result.IsFailed.ShouldBeTrue();
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public async Task LostReportFeatureStillScansWhenCalledFromFarmingWithoutTelegram()
        {
            Set(1, AccountSettingEnums.EnableAutoDeactivateLostReportTargets, 1);
            var monitor = new ReportMonitor(_outbox, _logger);
            var scanner = new ReportBadgeScanner(_browser, monitor, new DataService { AccountData = "one" },
                _factory, new LostReportTargetScanner(_browser));
            await scanner.Scan(new(1), CancellationToken.None, scheduled: true);
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            await scanner.Scan(new(1), CancellationToken.None);
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public async Task ScheduledIncomingOnlyNotifiesWhenEnabled()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            _incoming = true;
            var task = new ScheduledMonitoringTask.Task(new(1));
            await _handler.HandleAsync(task, CancellationToken.None);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            await _page.DidNotReceive().EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 1);
            await _handler.HandleAsync(task, CancellationToken.None);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("Incoming attacks: 0 -> 1");
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 0);
            await _handler.HandleAsync(task, CancellationToken.None);
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
        }

        [Fact]
        public void SavingIntervalWhileExecutingDoesNotChangeTheRunningTask()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 1);
            using var context = new AppDbContext(_options);
            _manager.UpdateScheduledMonitoring(new(1), context);
            var task = _manager.GetTaskList(new(1)).OfType<ScheduledMonitoringTask.Task>().Single();
            task.Stage = StageEnums.Executing;
            var scheduledAt = task.ExecuteAt;
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes, 3);
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes, 3);
            _manager.UpdateScheduledMonitoring(new(1), context, reschedule: true);
            task.ExecuteAt.ShouldBe(scheduledAt);
        }

        [Fact]
        public void SchedulingIsPerAccountAndChangesOrDisablesOnlySelectedAccount()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(2, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            Set(2, AccountSettingEnums.EnableReportNotifications, 1);
            using var context = new AppDbContext(_options);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.UpdateScheduledMonitoring(new(2), context);
            var first = _manager.GetTaskList(new(1)).OfType<ScheduledMonitoringTask.Task>().Single();
            var second = _manager.GetTaskList(new(2)).OfType<ScheduledMonitoringTask.Task>().Single();
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes, 3);
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes, 3);
            _manager.UpdateScheduledMonitoring(new(1), context, reschedule: true);
            first.ExecuteAt.ShouldBeInRange(DateTime.Now.AddSeconds(179), DateTime.Now.AddSeconds(181));
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 0);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            _manager.GetTaskList(new(2)).Single().ShouldBeSameAs(second);
        }

        [Theory]
        [InlineData("https://example.com/dorf2.php?foo=1", 1)]
        [InlineData("https://example.com/dorf1.php", 2)]
        [InlineData("https://example.com/report?next=dorf2.php", 2)]
        public async Task SwitchesOverviewBeforeScanning(string url, int destination)
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            _browser.CurrentUrl.Returns(url);
            var navigated = false;
            _toDorf.HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    call.Arg<ToDorfCommand.Command>().Dorf.ShouldBe(destination);
                    navigated = true;
                    return Result.Ok();
                });
            _page.EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>())
                .Returns(_ =>
                {
                    navigated.ShouldBeTrue();
                    return JsonDocument.Parse("""{"valid":true,"text":"0"}""").RootElement.Clone();
                });
            (await _handler.HandleAsync(new ScheduledMonitoringTask.Task(new(1)), CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
        }

        [Fact]
        public async Task NavigationFailureScansCurrentPageAndReschedules()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 1);
            _toDorf.HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(Result.Fail("Navigation failed"));
            var task = new ScheduledMonitoringTask.Task(new(1));
            var before = DateTime.Now;
            (await _handler.HandleAsync(task, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            task.ExecuteAt.ShouldBeGreaterThanOrEqualTo(before.AddMinutes(10));
            await _page.Received(2).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _logger.Received(1).Warning("Scheduled scan navigation failed; scanning current page: {Errors}",
                "Navigation failed");
        }

        [Fact]
        public async Task NavigationExceptionScansCurrentPage()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            _toDorf.HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(_ => ValueTask.FromException<Result>(new PlaywrightException("Navigation failed")));
            (await _handler.HandleAsync(new ScheduledMonitoringTask.Task(new(1)), CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _logger.Received(1).Warning(Arg.Any<PlaywrightException>(),
                "Scheduled scan navigation failed; scanning current page");
        }

        [Fact]
        public async Task UnavailableCurrentUrlStillScans()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            _browser.CurrentUrl.Returns("");
            (await _handler.HandleAsync(new ScheduledMonitoringTask.Task(new(1)), CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
            await _toDorf.DidNotReceive().HandleAsync(Arg.Any<ToDorfCommand.Command>(), Arg.Any<CancellationToken>());
            await _page.Received(1).EvaluateAsync<JsonElement>(Arg.Any<string>(), Arg.Any<object>());
            _logger.Received(1).Warning("Scheduled scan could not determine the current page; scanning without navigation");
        }

        [Theory]
        [InlineData("https://example.com/dorf2.php?foo=1", 1)]
        [InlineData("https://example.com/dorf1.php", 2)]
        [InlineData("https://example.com/report?next=dorf2.php", 2)]
        public async Task OverviewNavigationClicksAndWaitsForPageLoad(string url, int destination)
        {
            _browser.CurrentUrl.Returns(url);
            _browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(Result.Ok());
            _browser.WaitPageChanged(Arg.Any<string>()).Returns(Result.Ok());
            var handler = new ToDorfCommand.HandleBehavior(new ToDorfCommand(_browser));
            (await handler.HandleAsync(new ToDorfCommand.Command(destination), CancellationToken.None))
                .IsSuccess.ShouldBeTrue();
            _page.Received(1).Locator($"#navigation a[accesskey='{destination}']", Arg.Any<PageLocatorOptions>());
            await _browser.Received(1).Click(Arg.Any<ILocator>(), Arg.Any<string>());
            await _browser.Received(1).WaitPageChanged($"dorf{destination}.php");
        }

        [Fact]
        public void SchedulingTracksNotificationSwitches()
        {
            Set(1, AccountSettingEnums.EnableScheduledMonitoring, 1);
            using var context = new AppDbContext(_options);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
            Set(1, AccountSettingEnums.EnableReportNotifications, 1);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.GetTaskList(new(1)).OfType<ScheduledMonitoringTask.Task>().Count().ShouldBe(1);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 1);
            Set(1, AccountSettingEnums.EnableReportNotifications, 0);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.GetTaskList(new(1)).OfType<ScheduledMonitoringTask.Task>().Count().ShouldBe(1);
            Set(1, AccountSettingEnums.EnableIncomingTroopsNotifications, 0);
            _manager.UpdateScheduledMonitoring(new(1), context);
            _manager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Fact]
        public void MigrationFillsMissingSettingsForEachAccountWithoutOverwriting()
        {
            using var context = new AppDbContext(_options);
            context.AccountsSetting.Remove(context.AccountsSetting.Single(x => x.AccountId == 2 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes));
            context.SaveChanges();
            context.FillAccountSettings();
            context.AccountsSetting.Single(x => x.AccountId == 2 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes).Value.ShouldBe(10);
        }

        [Fact]
        public void ValidatesIntervalAndSupportsOlderImports()
        {
            var input = new AccountSettingInput();
            input.Set(new());
            input.ScheduledMonitoringInterval.Get().ShouldBe((10, 10));
            var validator = new AccountSettingInputValidator();
            input.ScheduledMonitoringInterval.Set(0, 10);
            validator.Validate(input).IsValid.ShouldBeFalse();
            input.ScheduledMonitoringInterval.Set(5, 4);
            validator.Validate(input).IsValid.ShouldBeFalse();
            input.ScheduledMonitoringInterval.Set(1, 1441);
            validator.Validate(input).IsValid.ShouldBeFalse();
            input.ScheduledMonitoringInterval.Set(1, 1440);
            validator.Validate(input).IsValid.ShouldBeTrue();
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(60, 1)]
        [InlineData(61, 2)]
        [InlineData(600, 10)]
        [InlineData(86400, 1440)]
        public void LegacyImportConvertsSecondsAndPreservesNewMinuteValues(int seconds, int minutes)
        {
            var settings = new Dictionary<AccountSettingEnums, int>
            {
                [AccountSettingEnums.ScheduledMonitoringIntervalMin] = seconds,
                [AccountSettingEnums.ScheduledMonitoringIntervalMax] = seconds,
            };
            ScheduledMonitoringSettings.ConvertLegacyIntervals(settings);
            settings[AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes].ShouldBe(minutes);
            settings[AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes].ShouldBe(minutes);
            settings[AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes] = 3;
            ScheduledMonitoringSettings.ConvertLegacyIntervals(settings);
            settings[AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes].ShouldBe(3);
        }

        [Fact]
        public void ExistingDatabaseMigratesIntervalsPerAccountOnlyOnce()
        {
            using var context = new AppDbContext(_options);
            context.AccountsSetting.RemoveRange(context.AccountsSetting.Where(x => x.AccountId == 1 &&
                (x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes ||
                 x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes)));
            context.AccountsSetting.AddRange(
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.ScheduledMonitoringIntervalMin, Value = 61 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.ScheduledMonitoringIntervalMax, Value = 600 });
            context.SaveChanges();
            context.FillAccountSettings();
            context.AccountsSetting.Single(x => x.AccountId == 1 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes).Value.ShouldBe(2);
            context.AccountsSetting.Single(x => x.AccountId == 1 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes).Value.ShouldBe(10);
            context.AccountsSetting.Single(x => x.AccountId == 2 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes).Value.ShouldBe(10);
            context.SaveAccountSetting(new(1), new() { [AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes] = 5 });
            context.FillAccountSettings();
            using var verify = new AppDbContext(_options);
            verify.AccountsSetting.Single(x => x.AccountId == 1 &&
                x.Setting == AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes).Value.ShouldBe(5);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1440)]
        public void FixedIntervalIsScheduledInMinutes(int minutes)
        {
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes, minutes);
            Set(1, AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes, minutes);
            using var context = new AppDbContext(_options);
            var before = DateTime.Now;
            var next = ScheduledMonitoringTask.NextRun(context, new(1));
            next.ShouldBeInRange(before.AddMinutes(minutes), DateTime.Now.AddMinutes(minutes));
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
