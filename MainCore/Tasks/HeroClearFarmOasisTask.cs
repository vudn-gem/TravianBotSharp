using MainCore.Commands.Navigate;
using MainCore.Enums;
using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class HeroClearFarmOasisTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        IDbContextFactory<AppDbContext> contextFactory,
        HeroClearOasisScanner scanner)
    {
        public sealed class Task(AccountId accountId, FarmId farmId, string? farmName = null) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            public VillageId? Owner { get; set; }
            public VillageId? HeroVillage { get; set; }
            public HashSet<string> ProcessedSlots { get; } = [];
            public HashSet<string> RetryAttemptedSlots { get; } = [];
            public DateTime? HeroReturnAt { get; set; }
            public int TaskBufferMilliseconds { get; set; }
            public int RetryDelayMilliseconds { get; set; }
            public int CurrentPhaseTargetLimit { get; set; }
            public int CurrentPhaseSentCount { get; set; }
            public bool SentRaidThisRun { get; set; }
            public bool ConfirmationAttempted { get; set; }
            protected override string TaskName => $"Hero clear oasis in farm list {farmName ?? FarmId.Value.ToString()}";
            public override string Key => $"{AccountId}_{FarmId.Value}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.ConfirmationAttempted)
                return Stop.Error.WithError("Hero raid confirmation outcome is unknown. Check the game and remove this task before restarting it.");
            var resumingAfterHeroReturn = task.HeroReturnAt is DateTime;
            if (task.HeroReturnAt is DateTime returnAt)
            {
                var readyAt = returnAt.AddMilliseconds(task.TaskBufferMilliseconds <= 0 ? 1000 : task.TaskBufferMilliseconds);
                if (readyAt > DateTime.Now)
                {
                    task.ExecuteAt = task.ExecuteAt > readyAt ? task.ExecuteAt : readyAt;
                    return Skip.Error.WithError("Waiting for the hero's estimated return.");
                }
            }
            using var context = contextFactory.CreateDbContext();
            var settings = context.HeroOasisSettings.AsNoTracking().SingleOrDefault(x =>
                x.FarmId == task.FarmId.Value &&
                context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == task.AccountId.Value));
            if (settings is null) return Skip.Error.WithError("Hero oasis settings or farm list no longer exist.");
            var valid = HeroOasisSettings.Validate(settings);
            if (valid.IsFailed) return Skip.Error.WithErrors(valid.Errors);
            OasisRaidSettings? troopSettings = null;
            if (settings.UseTroop)
            {
                var saved = OasisRaidSettings.Read(context, task.AccountId, task.FarmId);
                if (saved.IsFailed) return Skip.Error.WithErrors(saved.Errors);
                troopSettings = saved.Value;
            }
            task.TaskBufferMilliseconds = Math.Max(200, context.ByName(task.AccountId,
                AccountSettingEnums.TaskDelayMin, AccountSettingEnums.TaskDelayMax));
            task.RetryDelayMilliseconds = Math.Max(400, context.ByName(task.AccountId,
                AccountSettingEnums.TaskDelayMin, AccountSettingEnums.TaskDelayMax) * 2);
            if (task.CurrentPhaseTargetLimit <= 0)
            {
                task.CurrentPhaseTargetLimit = Math.Max(1, context.ByName(task.AccountId,
                    AccountSettingEnums.ClearTargetsPerPhaseMin, AccountSettingEnums.ClearTargetsPerPhaseMax));
                task.CurrentPhaseSentCount = 0;
            }
            task.SentRaidThisRun = false;

            var opened = await toFarmListPageCommand.HandleAsync(
                new(task.AccountId, ForceOpen: resumingAfterHeroReturn), cancellationToken);
            if (opened.IsFailed) return opened;
            var scan = await scanner.Scan(task, settings, cancellationToken, troopSettings);
            if (scan.IsFailed) return scan;
            if (!task.SentRaidThisRun) return scan;
            task.CurrentPhaseSentCount++;
            if (task.CurrentPhaseSentCount < task.CurrentPhaseTargetLimit) return scan;
            var phaseDelay = Math.Max(1, context.ByName(task.AccountId,
                AccountSettingEnums.FarmIntervalMin, AccountSettingEnums.FarmIntervalMax));
            task.ExecuteAt = DateTime.Now.AddSeconds(phaseDelay);
            task.CurrentPhaseTargetLimit = Math.Max(1, context.ByName(task.AccountId,
                AccountSettingEnums.ClearTargetsPerPhaseMin, AccountSettingEnums.ClearTargetsPerPhaseMax));
            task.CurrentPhaseSentCount = 0;
            task.HeroReturnAt = null;
            return Skip.Error.WithError(
                $"Hero clear oasis phase limit reached. Next phase at {task.ExecuteAt:yyyy-MM-dd HH:mm:ss}.");
        }
    }
}
