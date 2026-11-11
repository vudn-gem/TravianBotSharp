using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.UI.ViewModels.Tabs;

using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class ReportMonitorTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-reports-{Guid.NewGuid():N}");
        private readonly ReportNotificationOutbox _outbox;
        private readonly ReportMonitor _monitor;
        private readonly IncomingTroopsMonitor _incomingMonitor;
        private readonly AccountId _accountId = new(1);

        public ReportMonitorTest()
        {
            _outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            _monitor = new ReportMonitor(_outbox, Substitute.For<Serilog.ILogger>());
            _incomingMonitor = new IncomingTroopsMonitor(_outbox, TimeProvider.System);
        }

        private void Observe(int count) =>
            _monitor.Observe(_accountId, _monitor.GetSession(_accountId), count, "test-account");

        [Fact]
        public void FirstScanAndUnchangedCountsDoNotNotify()
        {
            Observe(3);
            Observe(3);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public void DecreaseToZeroBecomesNewBaseline()
        {
            Observe(3);
            Observe(0);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            Observe(1);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("0 -> 1");
        }

        [Fact]
        public void DecreaseToOneDoesNotNotifyButNextIncreaseDoes()
        {
            Observe(3);
            Observe(1);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            Observe(2);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("1 -> 2");
        }

        [Fact]
        public void AccountsHaveIndependentBaselines()
        {
            Observe(0);
            var second = new AccountId(2);
            _monitor.Observe(second, _monitor.GetSession(second), 5, "second");
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            Observe(1);
            _outbox.GetNext(DateTime.UtcNow)!.AccountId.ShouldBe(1);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResumeAndRestartResetBaselineWithoutChangingFarmSchedule(bool restart)
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            var manager = new TaskManager(new RxQueue(factory), factory, Substitute.For<Serilog.ILogger>(), _monitor, _incomingMonitor);
            manager.SetStatus(_accountId, StatusEnums.Online);
            Observe(2);
            manager.SetStatus(_accountId, StatusEnums.Paused);
            if (restart)
            {
                manager.Clear(_accountId);
                manager.SetStatus(_accountId, StatusEnums.Starting);
            }
            var scheduledAt = DateTime.Now.AddHours(1);
            var task = new StartFarmListTask.Task(_accountId) { ExecuteAt = scheduledAt };
            manager.Add(task);
            manager.SetStatus(_accountId, StatusEnums.Online);
            task.ExecuteAt.ShouldBe(scheduledAt);
            Observe(5);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            Observe(6);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("5 -> 6");
        }

        [Fact]
        public void ScanFromOldSessionCannotRestoreBaselineAfterReset()
        {
            var oldSession = _monitor.GetSession(_accountId);
            _monitor.Reset(_accountId);
            _monitor.Observe(_accountId, oldSession, 3, "test");
            Observe(5);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public void RecreatedMonitorStartsWithNoBaselineButKeepsQueuedMessages()
        {
            Observe(0);
            Observe(1);
            var replacement = new ReportMonitor(_outbox, Substitute.For<Serilog.ILogger>());
            replacement.Observe(_accountId, replacement.GetSession(_accountId), 5, "test");
            var queued = _outbox.GetNext(DateTime.UtcNow)!;
            queued.Text.ShouldContain("0 -> 1");
            _outbox.Complete(queued.Id);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public void OutboxPersistsRetriesAndStopsAfterThreeFailures()
        {
            _outbox.Enqueue(_accountId, "test");
            var replacement = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var notification = replacement.GetNext(DateTime.UtcNow)!;
            replacement.RecordFailure(notification, true, TimeSpan.FromSeconds(120));
            replacement.GetNext(DateTime.UtcNow.AddSeconds(119)).ShouldBeNull();
            notification = replacement.GetNext(DateTime.UtcNow.AddMinutes(3))!;
            notification.Attempts.ShouldBe(1);
            replacement.RecordFailure(notification, true, TimeSpan.Zero);
            notification = replacement.GetNext(DateTime.UtcNow.AddMinutes(3))!;
            replacement.RecordFailure(notification, true, TimeSpan.Zero);
            notification.Failed.ShouldBeTrue();
            replacement.GetNext(DateTime.MaxValue).ShouldBeNull();
        }

        [Fact]
        public void FailedOutboxWriteDoesNotAdvanceBaseline()
        {
            Observe(0);
            var blockedPath = Path.Combine(_directory, "outbox.db");
            Directory.CreateDirectory(blockedPath);
            Should.Throw<Microsoft.Data.Sqlite.SqliteException>(() => Observe(1));
            Directory.Delete(blockedPath);
            Observe(1);
            _outbox.GetNext(DateTime.UtcNow)!.Text.ShouldContain("0 -> 1");
        }

        [Fact]
        public void PermanentDeliveryFailureIsNotRetried()
        {
            _outbox.Enqueue(_accountId, "test");
            var notification = _outbox.GetNext(DateTime.UtcNow)!;
            _outbox.RecordFailure(notification, false, null);
            notification.Failed.ShouldBeTrue();
            notification.Attempts.ShouldBe(1);
            _outbox.GetNext(DateTime.MaxValue).ShouldBeNull();
        }

        [Fact]
        public void DisablingAndReenablingStartsNewBaseline()
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            var manager = new TaskManager(new RxQueue(factory), factory, Substitute.For<Serilog.ILogger>(), _monitor, _incomingMonitor);
            using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().Options);
            Observe(0);
            context.TriggerTask(manager, _accountId,
                new() { [AccountSettingEnums.EnableReportNotifications] = 0 }, _monitor, _incomingMonitor);
            context.TriggerTask(manager, _accountId,
                new() { [AccountSettingEnums.EnableReportNotifications] = 1 }, _monitor, _incomingMonitor);
            manager.GetTaskList(_accountId).ShouldBeEmpty();
            Observe(5);
            _outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
