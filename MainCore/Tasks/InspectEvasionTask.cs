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
            public bool ExpectIncoming { get; init; }

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
            if (task.ExpectIncoming && incoming.Value.Count == 0)
                return Failed(task, "Incoming was detected in the village list, but no attack/raid movements were found in the overview.");
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
                if (!queue.EvasionPlannedMovements.TryGetValue(task.VillageId, out var planned))
                    queue.EvasionPlannedMovements[task.VillageId] = planned = [];
                planned.IntersectWith(ids);
                var groups = new List<List<EvasionIncoming>>();
                var lead = TimeSpan.FromSeconds(settings.Value.LeadSeconds);
                if (!queue.EvasionCompletedArrivals.TryGetValue(task.VillageId, out var completedArrivals))
                    queue.EvasionCompletedArrivals[task.VillageId] = completedArrivals = [];
                foreach (var expired in completedArrivals.Where(x => observedAt > x.Value &&
                    observedAt - x.Value > lead).Select(x => x.Key).ToList())
                    completedArrivals.Remove(expired);
                var activeIds = incoming.Value.Where(x => x.Arrival > observedAt)
                    .Select(x => x.MovementId).ToHashSet();
                // Keep recently executed arrivals as anchors even after they disappear from the overview.
                var previous = completedArrivals.Where(x => !activeIds.Contains(x.Key))
                    .Select(x => new EvasionIncoming(x.Key, false, x.Value));
                foreach (var movement in incoming.Value.Where(x => x.Arrival > observedAt).Concat(previous)
                    .OrderBy(x => x.Arrival).ThenBy(x => x.MovementId))
                {
                    if (groups.Count == 0 || movement.Arrival - groups[^1][0].Arrival > lead)
                        groups.Add([]);
                    groups[^1].Add(movement);
                }
                var heads = groups.Select(x => x[0].MovementId).ToHashSet();
                foreach (var pending in queue.Tasks.OfType<EvasionReadinessTask.Task>()
                    .Where(x => x.VillageId == task.VillageId && !x.ManualTest && !x.PreparationAttempted).ToList())
                {
                    if (!heads.Contains(pending.Incoming.MovementId))
                    {
                        taskManager.Remove(task.AccountId, pending);
                        planned.Remove(pending.Incoming.MovementId);
                    }
                }
                foreach (var movement in incoming.Value)
                {
                    if (movement.ArrivalDifferenceSeconds is int difference)
                    {
                        if (Math.Abs(difference) > 5)
                            browser.Logger.Warning(
                                "Evasion arrival clock differs by {DifferenceSeconds} seconds for village {VillageId}, movement {MovementId}; countdown remains authoritative (local now {ObservedAt}, displayed arrival {DisplayedArrival})",
                                difference, task.VillageId.Value, movement.MovementId, observedAt, movement.DisplayedArrival);
                        else
                            browser.Logger.Information(
                                "Evasion arrival clock checked for village {VillageId}, movement {MovementId}: local now {ObservedAt}, displayed arrival {DisplayedArrival}, difference {DifferenceSeconds} seconds",
                                task.VillageId.Value, movement.MovementId, observedAt, movement.DisplayedArrival, difference);
                    }
                    else
                        browser.Logger.Warning(
                            "Evasion arrival clock could not be checked for village {VillageId}, movement {MovementId}: displayed arrival time missing or invalid; using countdown",
                            task.VillageId.Value, movement.MovementId);
                }
                foreach (var group in groups)
                {
                    var movement = group[0];
                    var completed = group.Any(x => completedArrivals.ContainsKey(x.MovementId) ||
                        planned.Contains(x.MovementId) &&
                        !queue.Tasks.OfType<EvasionReadinessTask.Task>().Any(p =>
                            p.VillageId == task.VillageId && !p.ManualTest && !p.PreparationAttempted &&
                            p.Incoming.MovementId == x.MovementId));
                    var pending = queue.Tasks.OfType<EvasionReadinessTask.Task>()
                        .FirstOrDefault(x => x.VillageId == task.VillageId && !x.ManualTest &&
                            !x.PreparationAttempted && x.Incoming.MovementId == movement.MovementId);
                    if (completed)
                    {
                        if (pending is not null)
                        {
                            taskManager.Remove(task.AccountId, pending);
                            planned.Remove(movement.MovementId);
                        }
                        continue;
                    }
                    if (pending is not null)
                    {
                        pending.IncomingCandidates = group.Select(x => x.MovementId).ToList();
                        continue;
                    }
                    var executeAt = movement.Arrival.AddSeconds(-settings.Value.LeadSeconds);
                    var readiness = new EvasionReadinessTask.Task(task.AccountId, task.VillageId, movement, settings.Value)
                    {
                        ExecuteAt = executeAt > observedAt ? executeAt : observedAt,
                        IncomingCandidates = group.Select(x => x.MovementId).ToList(),
                        PlanningQueue = queue,
                    };
                    if (taskManager.TryAdd(readiness))
                    {
                        planned.Add(movement.MovementId);
                        browser.Logger.Information(
                            "Evasion preparation scheduled: village {VillageId}, movement {MovementId}, arrival {Arrival}, preparation {ExecuteAt}; resource transfer unavailable",
                            task.VillageId.Value, movement.MovementId, movement.Arrival, readiness.ExecuteAt);
                    }
                }
                browser.Logger.Information("Evasion inspection read {MovementCount} incoming movements for village {VillageId}",
                    incoming.Value.Count, task.VillageId.Value);
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
