using System.Text.Json;

using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class InspectEvasionTask(
        IDbContextFactory<AppDbContext> contextFactory, IChromeBrowser browser,
        TaskManager taskManager, TimeProvider timeProvider)
    {
        public sealed class Task(AccountId accountId, VillageId villageId) : VillageTask(accountId, villageId)
        {
            protected override string TaskName => "Inspect incoming for evasion";
            public HashSet<long> PlannedMovements { get; } = [];

            public override bool CanStart(AppDbContext context) =>
                context.VillagesSetting.Any(x => x.VillageId == VillageId.Value &&
                    x.Setting == VillageSettingEnums.EvasionEnable && x.Value == 1);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            try
            {
                return await Inspect(task, cancellationToken);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Failed(task, ex.Message);
            }
        }

        private async ValueTask<Result> Inspect(Task task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var context = contextFactory.CreateDbContext();
            var settings = EvasionSettings.Read(context, task.AccountId, task.VillageId);
            if (settings.IsFailed) return Failed(task, string.Join("; ", settings.Errors.Select(x => x.Message)));
            if (!settings.Value.Enabled)
            {
                taskManager.RemoveEvasion(task.AccountId, task.VillageId);
                return Skip.Error.WithError("Evasion planning is disabled.");
            }

            var opened = await EvasionIncomingNavigation.Open(browser, task.VillageId, cancellationToken);
            if (opened.IsFailed) return Failed(task, string.Join("; ", opened.Errors.Select(x => x.Message)));
            // Use the live countdown, not data-value or the server's time-of-day (which can cross midnight).
            var observedAt = timeProvider.GetLocalNow().DateTime;
            var incoming = await EvasionIncomingParser.Read(browser.CurrentPage, observedAt);
            cancellationToken.ThrowIfCancellationRequested();
            if (incoming.IsFailed) return Failed(task, string.Join("; ", incoming.Errors.Select(x => x.Message)));
            var queue = taskManager.GetTaskQueue(task.AccountId);
            lock (queue)
            {
                if (!queue.Tasks.Contains(task))
                    return Skip.Error.WithError("Evasion inspection was removed while reading incoming.");
                settings = EvasionSettings.Read(context, task.AccountId, task.VillageId);
                if (settings.IsFailed) return Failed(task, string.Join("; ", settings.Errors.Select(x => x.Message)));
                if (!settings.Value.Enabled)
                {
                    taskManager.RemoveEvasion(task.AccountId, task.VillageId);
                    return Skip.Error.WithError("Evasion planning was disabled while reading incoming.");
                }

                var ids = incoming.Value.Select(x => x.MovementId).ToHashSet();
                foreach (var pending in queue.Tasks.OfType<EvasionReadinessTask.Task>()
                    .Where(x => x.VillageId == task.VillageId && !x.ManualTest && !ids.Contains(x.Incoming.MovementId)).ToList())
                    taskManager.Remove(task.AccountId, pending);
                task.PlannedMovements.IntersectWith(ids);
                foreach (var movement in incoming.Value)
                {
                    if (movement.Arrival <= observedAt || task.PlannedMovements.Contains(movement.MovementId)) continue;
                    var executeAt = movement.Arrival.AddSeconds(-settings.Value.LeadSeconds);
                    var readiness = new EvasionReadinessTask.Task(task.AccountId, task.VillageId, movement, settings.Value)
                    {
                        ExecuteAt = executeAt > observedAt ? executeAt : observedAt,
                    };
                    if (taskManager.TryAdd(readiness))
                    {
                        task.PlannedMovements.Add(movement.MovementId);
                        browser.Logger.Information(
                            "Evasion preparation scheduled: village {VillageId}, movement {MovementId}, arrival {Arrival}, preparation {ExecuteAt}; resource transfer unavailable",
                            task.VillageId.Value, movement.MovementId, movement.Arrival, readiness.ExecuteAt);
                    }
                }
                if (incoming.Value.Count > 0) task.ExecuteAt = observedAt.AddMinutes(1);
            }
            return Result.Ok();
        }

        private Result Failed(Task task, string reason)
        {
            browser.Logger.Warning("Evasion inspection failed for village {VillageId}: {Reason}; retrying in one minute",
                task.VillageId.Value, reason);
            task.ExecuteAt = timeProvider.GetLocalNow().DateTime.AddMinutes(1);
            return Skip.Error.WithError(reason);
        }
    }
}
