using System.Text.Json;

using MainCore.Commands.Features;
using MainCore.Commands.Navigate;
using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class EvasionReadinessTask(IChromeBrowser browser, TimeProvider timeProvider,
        IDbContextFactory<AppDbContext> contextFactory,
        IHandler<ToBuildingByTypeCommand.Command, Result> toBuildingCommand,
        IHandler<TrainTroopCommand.Command, Result> trainTroopCommand,
        IHandler<EvadeTroopsCommand.Command, Result> evadeTroopsCommand)
    {
        public sealed class Task(AccountId accountId, VillageId villageId, EvasionIncoming incoming, EvasionSettings settings,
            bool manualTest = false)
            : VillageTask(accountId, villageId)
        {
            private readonly long _keyMovementId = incoming.MovementId;
            public EvasionIncoming Incoming { get; private set; } = incoming;
            public IReadOnlyList<long> IncomingCandidates { get; set; } = [incoming.MovementId];
            internal TaskQueue? PlanningQueue { get; init; }
            public EvasionSettings Settings { get; } = settings;
            public bool ManualTest { get; } = manualTest;
            public bool TrainingAttempted { get; set; }
            public bool PreparationAttempted { get; set; }
            public override string Key => ManualTest ? $"{AccountId}-{VillageId}-evasion-test" : $"{AccountId}-{VillageId}-{_keyMovementId}";
            protected override string TaskName => ManualTest ? "Test evasion (LIVE)" : $"Evasion preparation (#{Incoming.MovementId})";
            public override string Description => ManualTest
                ? $"LIVE evasion test in village {VillageId.Value}; train {Settings.TrainTroop}, raid enabled {Settings.EvadeTroops}, target ({Settings.X}|{Settings.Y})"
                : $"Evasion preparation for incoming #{Incoming.MovementId}";
            public override bool CanStart(AppDbContext context) =>
                context.BooleanByName(VillageId, VillageSettingEnums.EvasionEnable);

            internal bool SelectIncoming(EvasionIncoming movement)
            {
                if (PlanningQueue is not { } queue) return false;
                lock (queue)
                {
                    if (!queue.Tasks.Contains(this) ||
                        !queue.EvasionPlannedMovements.TryGetValue(VillageId, out var planned))
                        return false;
                    planned.Add(movement.MovementId);
                    Incoming = movement;
                    return true;
                }
            }

            internal bool RecordPreparation()
            {
                if (PlanningQueue is not { } queue) return true;
                lock (queue)
                {
                    if (!queue.Tasks.Contains(this)) return false;
                    if (!queue.EvasionCompletedArrivals.TryGetValue(VillageId, out var completed))
                        queue.EvasionCompletedArrivals[VillageId] = completed = [];
                    completed[Incoming.MovementId] = Incoming.Arrival;
                    return true;
                }
            }
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task.PreparationAttempted)
                return Skipped(task, "Evasion preparation was already attempted; no automatic retry.");
            task.PreparationAttempted = true;
            try
            {
                if (task.ManualTest)
                {
                    using var testContext = contextFactory.CreateDbContext();
                    var confirmed = EvasionSettings.Read(testContext, task.AccountId, task.VillageId);
                    if (confirmed.IsFailed || confirmed.Value != task.Settings)
                        return Skipped(task, "Saved settings changed after test confirmation; live test canceled.");
                    browser.Logger.Warning("Starting LIVE evasion test in village {VillageId}; no incoming required", task.VillageId.Value);
                }
                else if (task.IncomingCandidates.Count > 1)
                {
                    var overview = await EvasionIncomingNavigation.Open(browser, task.VillageId, cancellationToken);
                    if (overview.IsFailed) return Skipped(task, string.Join("; ", overview.Errors.Select(x => x.Message)));
                    var observedAt = timeProvider.GetLocalNow().DateTime;
                    var incoming = await EvasionIncomingParser.Read(browser.CurrentPage, observedAt);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (incoming.IsFailed) return Skipped(task, string.Join("; ", incoming.Errors.Select(x => x.Message)));
                    var movement = incoming.Value.Where(x =>
                            task.IncomingCandidates.Contains(x.MovementId) && x.Arrival > observedAt)
                        .OrderBy(x => x.Arrival)
                        .ThenBy(x => x.MovementId != task.Incoming.MovementId)
                        .ThenBy(x => x.MovementId)
                        .FirstOrDefault();
                    if (movement is null) return Skipped(task, "No incoming movement remains in this evasion group.");
                    if (movement.MovementId != task.Incoming.MovementId)
                    {
                        if (!task.SelectIncoming(movement))
                            return Skipped(task, "Evasion plan was removed while checking the incoming group.");
                        browser.Logger.Information(
                            "Evasion group switched to remaining movement {MovementId} in village {VillageId}",
                            movement.MovementId, task.VillageId.Value);
                    }
                }
                if (!task.ManualTest && !task.RecordPreparation())
                    return Skipped(task, "Evasion plan was removed before preparation.");
                var training = await Train(task, cancellationToken);
                if (training.IsFailed)
                    browser.Logger.Warning("Evasion training phase skipped/failed; checking troop evasion independently: {Errors}",
                        string.Join("; ", training.Errors.Select(x => x.Message)));
                cancellationToken.ThrowIfCancellationRequested();
                using var context = contextFactory.CreateDbContext();
                var current = EvasionSettings.Read(context, task.AccountId, task.VillageId);
                if (current.IsFailed) return Skipped(task, string.Join("; ", current.Errors.Select(x => x.Message)));
                if (task.ManualTest && current.Value != task.Settings)
                    return Skipped(task, "Saved settings changed during the live test; no troop send.");
                if (!current.Value.Enabled) return Skipped(task, "Evasion is disabled.");
                if (task.Incoming.Arrival <= timeProvider.GetLocalNow().DateTime)
                    return Skipped(task, "Incoming arrival has passed; no troop evasion.");
                if (current.Value.EvadeTroops)
                {
                    var evaded = await evadeTroopsCommand.HandleAsync(new(task.AccountId, task.VillageId,
                        task.Incoming, current.Value.X, current.Value.Y, task.ManualTest), cancellationToken);
                    if (evaded.IsFailed)
                        return Skipped(task, $"Troop evasion was not confirmed; no automatic retry. {string.Join("; ", evaded.Errors.Select(x => x.Message))}");
                }
                else browser.Logger.Information("Troop evasion disabled in village {VillageId}", task.VillageId.Value);
                if (current.Value.ResourceDestination is not null)
                    return Skipped(task, "Resource transfer is not implemented; no resources were sent.");
                if (training.IsFailed)
                    return Skipped(task, "Evasion preparation completed with a skipped/failed training phase; see the training warning.");
                return Result.Ok();
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Skipped(task, $"Evasion preparation failed; no automatic retry. Check the game. {ex.Message}");
            }
        }

        private async ValueTask<Result> Train(Task task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task.TrainingAttempted)
                return Skipped(task, "Training was already attempted; check the game before training again.");
            using var context = contextFactory.CreateDbContext();
            var current = EvasionSettings.Read(context, task.AccountId, task.VillageId);
            if (current.IsFailed) return Skipped(task, string.Join("; ", current.Errors.Select(x => x.Message)));
            if (!current.Value.Enabled) return Skipped(task, "Evasion is disabled.");
            if (task.Incoming.Arrival <= timeProvider.GetLocalNow().DateTime)
                return Skipped(task, "Incoming arrival has already passed.");

            var troop = current.Value.TrainTroop;
            if (troop != TroopEnums.None)
            {
                try
                {
                    var deadline = task.Incoming.Arrival;
                    if (!task.ManualTest)
                    {
                        var overview = await EvasionIncomingNavigation.Open(browser, task.VillageId, cancellationToken);
                        if (overview.IsFailed) return Skipped(task, string.Join("; ", overview.Errors.Select(x => x.Message)));
                        var observedAt = timeProvider.GetLocalNow().DateTime;
                        var incoming = await EvasionIncomingParser.Read(browser.CurrentPage, observedAt);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (incoming.IsFailed) return Skipped(task, string.Join("; ", incoming.Errors.Select(x => x.Message)));
                        var movement = incoming.Value.SingleOrDefault(x => x.MovementId == task.Incoming.MovementId);
                        if (movement is null) return Skipped(task, "Incoming movement is no longer present; training skipped.");
                        if (movement.Arrival < deadline) deadline = movement.Arrival;
                    }
                    var tribe = (TribeEnums)context.ByName(task.VillageId, VillageSettingEnums.Tribe);
                    var building = tribe.GetInfantryTroops().Contains(troop) ? BuildingEnums.Barracks :
                        tribe.GetCavalryTroops().Contains(troop) ? BuildingEnums.Stable : BuildingEnums.Workshop;
                    var opened = await toBuildingCommand.HandleAsync(new(task.VillageId, building), cancellationToken);
                    if (opened.IsFailed) return Skipped(task, string.Join("; ", opened.Errors.Select(x => x.Message)));
                    cancellationToken.ThrowIfCancellationRequested();
                    current = EvasionSettings.Read(context, task.AccountId, task.VillageId);
                    if (current.IsFailed) return Skipped(task, string.Join("; ", current.Errors.Select(x => x.Message)));
                    if (!current.Value.Enabled || current.Value.TrainTroop != troop)
                        return Skipped(task, "Evasion training settings changed during navigation.");
                    if (deadline <= timeProvider.GetLocalNow().DateTime)
                        return Skipped(task, "Incoming arrival passed during navigation.");

                    // Claim the attempt before any form interaction so a task retry cannot train twice.
                    task.TrainingAttempted = true;
                    var trained = await trainTroopCommand.HandleAsync(
                        new(troop, 0, false, TrainMaximum: true, SubmitBefore: deadline,
                            ValidateBeforeSubmit: () =>
                            {
                                var latest = EvasionSettings.Read(context, task.AccountId, task.VillageId);
                                if (latest.IsFailed) return Result.Fail(latest.Errors);
                                return latest.Value.Enabled && latest.Value.TrainTroop == troop
                                    ? Result.Ok() : Skip.Error.WithError("Evasion training settings changed while filling the form.");
                            }), cancellationToken);
                    if (trained.IsFailed)
                        return Skipped(task, $"Evasion training was not confirmed; no automatic retry. {string.Join("; ", trained.Errors.Select(x => x.Message))}");
                    browser.Logger.Information("Evasion maximum training submitted for {Troop} in village {VillageId}, movement {MovementId}",
                        troop, task.VillageId.Value, task.Incoming.MovementId);
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return Skipped(task, $"Evasion training failed; check the game before retrying. {ex.Message}");
                }
            }
            else browser.Logger.Information("Evasion troop training skipped (None) in village {VillageId}", task.VillageId.Value);

            return Result.Ok();
        }

        private Result Skipped(Task task, string reason)
        {
            browser.Logger.Warning("Evasion readiness for village {VillageId}, movement {MovementId}: {Reason}",
                task.VillageId.Value, task.Incoming.MovementId, reason);
            return Skip.Error.WithError(reason);
        }
    }
}
