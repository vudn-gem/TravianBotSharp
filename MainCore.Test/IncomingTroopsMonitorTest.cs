using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Parsers;
using MainCore.Services;

using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class IncomingTroopsMonitorTest : IDisposable
    {
        private sealed class Clock : TimeProvider
        {
            public long Seconds { get; set; }
            public override long TimestampFrequency => 1;
            public override long GetTimestamp() => Seconds;
        }

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-incoming-{Guid.NewGuid():N}");
        private readonly Clock _clock = new();
        private readonly ReportNotificationOutbox _outbox;
        private readonly IncomingTroopsMonitor _monitor;
        private readonly AccountId _account = new(1);

        public IncomingTroopsMonitorTest()
        {
            _outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            _monitor = new IncomingTroopsMonitor(_outbox, _clock);
        }

        private void Scan(params IncomingVillage[] villages)
        {
            var session = _monitor.TryBeginScan(_account);
            session.ShouldNotBeNull();
            _monitor.Observe(_account, session.Value, villages.ToList(), "test");
        }

        private bool Consume()
        {
            var message = _outbox.GetNext(DateTime.UtcNow);
            if (message is null) return false;
            message.Text.ShouldContain("Incoming attack/raid");
            _outbox.Complete(message.Id);
            return true;
        }

        [Fact]
        public void FirstIncomingAlertsThenOnlyNoToYesAlerts()
        {
            Scan(new IncomingVillage(1, "village", "(1|2)", 1, 0));
            Consume().ShouldBeTrue();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "village", "(1|2)", 1, 0));
            Consume().ShouldBeFalse();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "village", "(1|2)", 0, 0));
            Consume().ShouldBeFalse();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "village", "(1|2)", 1, 0));
            Consume().ShouldBeTrue();
        }

        [Fact]
        public void MinimumIntervalIsPerAccountAndIncludesFailedAttempts()
        {
            _monitor.TryBeginScan(_account).ShouldNotBeNull();
            _clock.Seconds = 59;
            _monitor.TryBeginScan(_account).ShouldBeNull();
            _monitor.TryBeginScan(new(2)).ShouldNotBeNull();
            _clock.Seconds = 60;
            _monitor.TryBeginScan(_account).ShouldNotBeNull();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResumeOrRestartResetsStateWithoutBypassingMinimumInterval(bool restart)
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            var manager = new TaskManager(new RxQueue(factory), factory, Substitute.For<Serilog.ILogger>(),
                new ReportMonitor(_outbox, Substitute.For<Serilog.ILogger>()), _monitor);
            manager.SetStatus(_account, StatusEnums.Online);
            Scan(new IncomingVillage(1, "village", "", 1, 0));
            Consume().ShouldBeTrue();
            manager.SetStatus(_account, StatusEnums.Paused);
            if (restart) manager.Clear(_account);
            manager.SetStatus(_account, StatusEnums.Online);
            _monitor.TryBeginScan(_account).ShouldBeNull();
            _clock.Seconds = 60;
            Scan(new IncomingVillage(1, "village", "", 1, 0));
            Consume().ShouldBeTrue();
        }

        [Fact]
        public void MissingVillageRowsAndRenamingDoNotCreateDuplicateAlerts()
        {
            Scan(new IncomingVillage(1, "original", "", 1, 0));
            Consume().ShouldBeTrue();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(2, "other", "", 0, 0));
            _clock.Seconds += 60;
            Scan(new(1, "renamed", "", 1, 0), new(2, "other", "", 1, 0));
            Consume().ShouldBeTrue();
            Consume().ShouldBeFalse();
        }

        [Fact]
        public void OldSessionSnapshotIsIgnoredAfterReset()
        {
            var session = _monitor.TryBeginScan(_account)!.Value;
            _monitor.Reset(_account);
            _monitor.Observe(_account, session, [new(1, "village", "", 1, 0)], "test");
            Consume().ShouldBeFalse();
        }

        [Fact]
        public void FallbackIsDueTenMinutesAfterAttemptAndNeverInThePast()
        {
            _monitor.TryBeginScan(_account);
            _clock.Seconds = 599;
            _monitor.GetFallbackExecuteAt(_account).ShouldBeInRange(
                DateTime.Now, DateTime.Now.AddSeconds(2));
            _clock.Seconds = 601;
            _monitor.GetFallbackExecuteAt(_account).ShouldBeInRange(
                DateTime.Now.AddSeconds(-1), DateTime.Now.AddSeconds(1));
        }

        [Fact]
        public void AlertsWhenEitherCountIncreasesEvenIfTotalDoesNot()
        {
            Scan(new IncomingVillage(1, "v", "", 2, 1));
            var message = _outbox.GetNext(DateTime.UtcNow)!;
            message.Text.ShouldContain("Incoming attacks: 0 -> 2");
            message.Text.ShouldContain("Incoming raids: 0 -> 1");
            _outbox.Complete(message.Id);
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "v", "", 1, 2));
            Consume().ShouldBeTrue();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "v", "", 0, 1));
            Consume().ShouldBeFalse();
            _clock.Seconds += 60;
            Scan(new IncomingVillage(1, "v", "", 0, 2));
            Consume().ShouldBeTrue();
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
