using System.Collections.Concurrent;

using MainCore.Infrasturecture.Extensions;
using MainCore.Services.Scheduling;
using MainCore.Tasks.Base;

namespace MainCore.Services
{
    [RegisterSingleton<TaskManager>]
    public sealed class TaskManager(
        RxQueue rxQueue,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger logger,
        ReportMonitor reportMonitor,
        IncomingTroopsMonitor incomingTroopsMonitor)
    {
        private readonly ConcurrentDictionary<AccountId, AccountSchedulerActor> _actors = [];
        private readonly ConcurrentDictionary<AccountId, List<ConquestTask.Task>> _conquestHistory = [];

        private void SaveConquest(ConquestTask.Task task)
        {
            lock (task)
            {
                using var context = contextFactory.CreateDbContext();
                var plan = ConquestPlanPersistence.Capture(task);
                var existing = context.ConquestPlans.Find(plan.Id);
                if (existing is null) context.ConquestPlans.Add(plan);
                else context.Entry(existing).CurrentValues.SetValues(plan);
                context.SaveChanges();
            }
        }

        public void RestoreConquests()
        {
            using var context = contextFactory.CreateDbContext();
            foreach (var plan in context.ConquestPlans.AsNoTracking().ToList())
            {
                var accountId = new AccountId(plan.AccountId);
                if (!context.Accounts.Any(account => account.Id == plan.AccountId)) continue;
                var history = _conquestHistory.GetOrAdd(accountId, _ => []);
                lock (history)
                {
                    if (history.Any(task => task.PlanId == plan.Id)) continue;
                    var ownsVillage = context.Villages.Any(village =>
                        village.Id == plan.VillageId && village.AccountId == plan.AccountId);
                    var task = ConquestPlanPersistence.Restore(plan, DateTime.Now, ownsVillage);
                    task.Persist = () => SaveConquest(task);
                    task.Persist();
                    history.Add(task);
                    if (!task.IsFinished) GetActor(accountId).Add(task, first: false);
                }
                rxQueue.Enqueue(new TasksModified(accountId));
            }
        }

        public List<ConquestTask.Task> GetConquests(AccountId accountId)
        {
            var history = _conquestHistory.GetOrAdd(accountId, _ => []);
            lock (history) return history.ToList();
        }

        public bool CancelConquest(AccountId accountId, ConquestTask.Task task)
        {
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                if (task.AccountId != accountId || !queue.Tasks.Contains(task) || !task.RequestCancel())
                    return false;
                task.Status = "Canceling";
                if (task.Stage != StageEnums.Executing)
                {
                    queue.Tasks.Remove(task);
                    task.Status = "Canceled";
                    task.IsFinished = true;
                }
                task.Persist?.Invoke();
            }
            rxQueue.Enqueue(new TasksModified(accountId));
            return true;
        }

        public BaseTask? GetCurrentTask(AccountId accountId)
        {
            var tasks = GetTaskList(accountId);
            return tasks.Find(x => x.Stage == StageEnums.Executing);
        }

        public async Task StopCurrentTask(AccountId accountId)
        {
            var cts = GetCancellationTokenSource(accountId);
            if (cts is not null) await cts.CancelAsync();

            BaseTask? currentTask;
            do
            {
                currentTask = GetCurrentTask(accountId);
                if (currentTask is null) break;
                await Task.Delay(500);
            }
            while (currentTask.Stage != StageEnums.Waiting);
            SetStatus(accountId, StatusEnums.Paused);
        }

        public void AddOrUpdate<T>(T task, bool first = false) where T : AccountTask
        {
            if (!ValidateTaskOwnership(task))
            {
                return;
            }

            var actor = GetActor(task.AccountId);
            actor.AddOrUpdate(task, first);
        }

        public void Add<T>(T task, bool first = false) where T : AccountTask
        {
            if (!ValidateTaskOwnership(task))
            {
                return;
            }

            var actor = GetActor(task.AccountId);
            actor.Add(task, first);
            rxQueue.Enqueue(new TasksModified(task.AccountId));
        }

        public bool TryAdd<T>(T task) where T : AccountTask
        {
            if (!ValidateTaskOwnership(task))
            {
                return false;
            }

            var actor = GetActor(task.AccountId);
            var queue = actor.GetQueue();
            lock (queue)
            {
                if (queue.Tasks.OfType<T>().Any(existing => existing.Key == task.Key))
                    return false;
                if (task is ConquestTask.Task conquest)
                {
                    conquest.Persist = () => SaveConquest(conquest);
                    conquest.Persist();
                    var history = _conquestHistory.GetOrAdd(task.AccountId, _ => []);
                    lock (history) history.Add(conquest);
                }
                actor.Add(task, first: false);
            }
            rxQueue.Enqueue(new TasksModified(task.AccountId));
            return true;
        }

        public bool IsExist<T>(AccountId accountId) where T : BaseTask
        {
            var tasks = GetTaskList(accountId)
                .OfType<T>();
            return tasks.Any(x => x.Key == $"{accountId}");
        }

        public bool IsExist<T>(AccountId accountId, VillageId villageId) where T : BaseTask
        {
            var tasks = GetTaskList(accountId)
                .OfType<T>();
            return tasks.Any(x => x.Key == $"{accountId}-{villageId}");
        }

        public void Remove(AccountId accountId, BaseTask task)
        {
            if (task is ConquestTask.Task conquest)
            {
                if (!conquest.IsFinished)
                {
                    if (conquest.ConfirmationAttempted) conquest.Status = "Check in game";
                    else
                    {
                        conquest.RequestCancel();
                        conquest.Status = "Canceled";
                    }
                    conquest.IsFinished = true;
                }
                conquest.Persist?.Invoke();
            }
            var actor = GetActor(accountId);
            actor.Remove(task);
        }

        public ClearOasisRemovalResult RemoveClearOasis(AccountId accountId, FarmId farmId)
        {
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                var task = queue.Tasks.OfType<ClearFarmOasisTask.Task>()
                    .FirstOrDefault(x => x.FarmId == farmId);
                if (task is null) return ClearOasisRemovalResult.NotFound;
                if (task.Stage == StageEnums.Executing) return ClearOasisRemovalResult.Executing;
                queue.Tasks.Remove(task);
            }
            rxQueue.Enqueue(new TasksModified(accountId));
            return ClearOasisRemovalResult.Removed;
        }

        public bool TryAddHeroClearOasis(AccountId accountId, FarmId farmId)
        {
            using var context = contextFactory.CreateDbContext();
            if (!context.FarmLists.Any(x => x.Id == farmId.Value && x.AccountId == accountId.Value))
                return false;
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                if (queue.Tasks.OfType<HeroClearFarmOasisTask.Task>().Any()) return false;
                queue.Tasks.Add(new HeroClearFarmOasisTask.Task(accountId, farmId));
            }
            rxQueue.Enqueue(new TasksModified(accountId));
            return true;
        }

        public ClearOasisRemovalResult RemoveHeroClearOasis(AccountId accountId, FarmId farmId)
        {
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                var task = queue.Tasks.OfType<HeroClearFarmOasisTask.Task>().SingleOrDefault(x => x.FarmId == farmId);
                if (task is null) return ClearOasisRemovalResult.NotFound;
                if (task.Stage == StageEnums.Executing) return ClearOasisRemovalResult.Executing;
                queue.Tasks.Remove(task);
            }
            rxQueue.Enqueue(new TasksModified(accountId));
            return ClearOasisRemovalResult.Removed;
        }

        public void Remove<T>(AccountId accountId) where T : AccountTask
        {
            var actor = GetActor(accountId);
            actor.Remove<T>();
        }

        public void Remove<T>(AccountId accountId, VillageId villageId) where T : VillageTask
        {
            var actor = GetActor(accountId);
            actor.Remove<T>(villageId);
        }

        public void Clear(AccountId accountId)
        {
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                foreach (var task in queue.Tasks.OfType<ConquestTask.Task>())
                {
                    if (task.RequestCancel())
                    {
                        task.Status = task.Stage == StageEnums.Executing ? "Canceling" : "Canceled";
                        task.IsFinished = task.Stage != StageEnums.Executing;
                    }
                    else if (task.ConfirmationAttempted && !task.IsFinished)
                    {
                        task.Status = "Check in game";
                    }
                    task.Persist?.Invoke();
                }
            }
            reportMonitor.Reset(accountId);
            incomingTroopsMonitor.Reset(accountId);
            var actor = GetActor(accountId);
            actor.Clear();
        }

        public void ScheduleIncomingScan(AccountId accountId, DateTime executeAt)
        {
            AddOrUpdate(new CheckIncomingTroopsTask.Task(accountId) { ExecuteAt = executeAt });
        }

        public void UpdateScheduledMonitoring(AccountId accountId, AppDbContext context, bool reschedule = false)
        {
            var enabled = new ScheduledMonitoringTask.Task(accountId).CanStart(context);
            var executeAt = enabled ? ScheduledMonitoringTask.NextRun(context, accountId) : default;
            var queue = GetTaskQueue(accountId);
            lock (queue)
            {
                var existing = queue.Tasks.OfType<ScheduledMonitoringTask.Task>().FirstOrDefault();
                if (!enabled)
                {
                    if (existing is not null) queue.Tasks.Remove(existing);
                }
                else if (existing is null)
                {
                    queue.Tasks.Add(new ScheduledMonitoringTask.Task(accountId) { ExecuteAt = executeAt });
                }
                else if (reschedule && existing.Stage != StageEnums.Executing)
                {
                    existing.ExecuteAt = executeAt;
                }
            }
            rxQueue.Enqueue(new TasksModified(accountId));
        }

        public List<BaseTask> GetTaskList(AccountId accountId)
        {
            var actor = GetActor(accountId);
            return actor.GetTaskList();
        }

        public StatusEnums GetStatus(AccountId accountId)
        {
            var actor = GetActor(accountId);
            return actor.GetStatus();
        }

        public void SetStatus(AccountId accountId, StatusEnums status)
        {
            var actor = GetActor(accountId);
            if (actor.GetStatus() != status)
            {
                reportMonitor.Reset(accountId);
                incomingTroopsMonitor.Reset(accountId);
            }
            actor.SetStatus(status);
        }

        private CancellationTokenSource? GetCancellationTokenSource(AccountId accountId)
        {
            var actor = GetActor(accountId);
            return actor.GetCancellationTokenSource();
        }

        public bool IsExecuting(AccountId accountId)
        {
            var actor = GetActor(accountId);
            return actor.IsExecuting();
        }

        public TaskQueue GetTaskQueue(AccountId accountId)
        {
            var actor = GetActor(accountId);
            return actor.GetQueue();
        }

        private AccountSchedulerActor GetActor(AccountId accountId)
        {
            return _actors.GetOrAdd(accountId, static (id, state) => new AccountSchedulerActor(id, state), rxQueue);
        }

        private bool ValidateTaskOwnership(AccountTask task)
        {
            if (task.AccountId == AccountId.Empty)
            {
                var reason = $"Rejected {task.GetType().Name}: AccountId is empty";
                logger.Warning("{Reason}", reason);
                return false;
            }

            if (task is VillageTask villageTask)
            {
                using var context = contextFactory.CreateDbContext();
                var villageBelongToAccount = context.Villages
                    .Any(x => x.Id == villageTask.VillageId.Value && x.AccountId == task.AccountId.Value);

                if (!villageBelongToAccount)
                {
                    var reason = $"Rejected {task.GetType().Name}: village {villageTask.VillageId.Value} does not belong to account {task.AccountId.Value}";
                    logger.Warning("{Reason}", reason);
                    return false;
                }
            }
            return true;
        }
    }

    public enum ClearOasisRemovalResult
    {
        Removed,
        NotFound,
        Executing,
    }

    public class TaskQueue
    {
        public bool IsExecuting { get; set; } = false;
        public StatusEnums Status { get; set; } = StatusEnums.Offline;
        public CancellationTokenSource? CancellationTokenSource { get; set; }
        public List<BaseTask> Tasks { get; } = [];
    }
}