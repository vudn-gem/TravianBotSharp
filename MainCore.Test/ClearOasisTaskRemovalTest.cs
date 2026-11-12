using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class ClearOasisTaskRemovalTest
    {
        [Fact]
        public void RemovesOnlyWaitingTaskForSelectedFarmAndAccount()
        {
            var manager = CreateManager();
            var account = new AccountId(1);
            var otherAccount = new AccountId(2);
            var selected = new ClearFarmOasisTask.Task(account, new FarmId(10));
            var otherFarm = new ClearFarmOasisTask.Task(account, new FarmId(20));
            var otherAccountTask = new ClearFarmOasisTask.Task(otherAccount, new FarmId(10));
            var otherTask = new CheckFarmTargetTroopsTask.Task(account, new FarmId(10));
            manager.Add(selected);
            manager.Add(otherFarm);
            manager.Add(otherAccountTask);
            manager.Add(otherTask);

            manager.RemoveClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.Removed);
            manager.GetTaskList(account).ShouldNotContain(selected);
            manager.GetTaskList(account).ShouldContain(otherFarm);
            manager.GetTaskList(account).ShouldContain(otherTask);
            manager.GetTaskList(otherAccount).ShouldContain(otherAccountTask);
            manager.RemoveClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.NotFound);
        }

        [Fact]
        public void DoesNotRemoveExecutingTask()
        {
            var manager = CreateManager();
            var account = new AccountId(1);
            var task = new ClearFarmOasisTask.Task(account, new FarmId(10)) { Stage = StageEnums.Executing };
            manager.Add(task);

            manager.RemoveClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.Executing);
            manager.GetTaskList(account).ShouldContain(task);
            task.Stage = StageEnums.Waiting;
            manager.RemoveClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.Removed);
        }

        private static TaskManager CreateManager()
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            var logger = Substitute.For<Serilog.ILogger>();
            var outbox = new ReportNotificationOutbox(Path.Combine(Path.GetTempPath(), $"tbs-unused-{Guid.NewGuid():N}.db"));
            return new TaskManager(new RxQueue(factory), factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
        }
    }
}
