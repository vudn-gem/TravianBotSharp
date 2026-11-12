using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public class ConquestCancellationTest
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancelAffectsOnlySelectedTaskAndDoesNotPauseAccount(bool executing)
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            var logger = Substitute.For<Serilog.ILogger>();
            var outbox = new ReportNotificationOutbox(Path.Combine(Path.GetTempPath(), $"tbs-unused-{Guid.NewGuid():N}.db"));
            var manager = new TaskManager(new RxQueue(factory), factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            var account = new AccountId(1);
            var queue = manager.GetTaskQueue(account);
            queue.Status = StatusEnums.Online;
            var selected = CreateTask();
            selected.Stage = executing ? StageEnums.Executing : StageEnums.Waiting;
            var other = CreateTask();
            queue.Tasks.AddRange([selected, other]);

            manager.CancelConquest(new AccountId(2), selected).ShouldBeFalse();
            selected.CancelRequested.ShouldBeFalse();
            manager.CancelConquest(account, selected).ShouldBeTrue();
            selected.CancelToken.IsCancellationRequested.ShouldBeTrue();
            selected.Status.ShouldBe(executing ? "Canceling" : "Canceled");
            queue.Tasks.Contains(selected).ShouldBe(executing);
            queue.Tasks.ShouldContain(other);
            manager.GetStatus(account).ShouldBe(StatusEnums.Online);
        }

        [Fact]
        public void ConfirmationAndFinishedTasksCannotBeCanceled()
        {
            var task = CreateTask();
            task.ConfirmationAttempted = true;
            task.RequestCancel().ShouldBeFalse();
            task.CancelRequested.ShouldBeFalse();
            task.ConfirmationAttempted = false;
            task.IsFinished = true;
            task.RequestCancel().ShouldBeFalse();
        }

        private static ConquestTask.Task CreateTask() => new(
            new AccountId(1), new VillageId(2), 3, 4,
            [new ConquestWave([19, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])],
            DateTime.Now.AddHours(2), DateTime.Now.AddHours(1), 0.4);
    }
}
