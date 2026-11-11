using MainCore.Commands.Navigate;
using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class ClearFarmOasisTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        IDbContextFactory<AppDbContext> contextFactory,
        ClearOasisScanner scanner)
    {
        private static readonly IReadOnlyDictionary<AccountSettingEnums, int> OasisAnimalUnitBySetting =
            new Dictionary<AccountSettingEnums, int>
            {
                { AccountSettingEnums.AllowRaidRatOasis, 31 },
                { AccountSettingEnums.AllowRaidSpiderOasis, 32 },
                { AccountSettingEnums.AllowRaidSnakeOasis, 33 },
                { AccountSettingEnums.AllowRaidBatOasis, 34 },
                { AccountSettingEnums.AllowRaidWildBoarOasis, 35 },
                { AccountSettingEnums.AllowRaidWolfOasis, 36 },
                { AccountSettingEnums.AllowRaidBearOasis, 37 },
                { AccountSettingEnums.AllowRaidCrocodileOasis, 38 },
                { AccountSettingEnums.AllowRaidTigerOasis, 39 },
                { AccountSettingEnums.AllowRaidElephantOasis, 40 },
            };

        public sealed class Task(AccountId accountId, FarmId farmId) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            public int ResumeTargetIndex { get; set; } = 0;
            protected override string TaskName => $"Clear oasis in farm list {FarmId.Value}";
            public override string Key => $"{AccountId}_{FarmId.Value}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            var result = await toFarmListPageCommand.HandleAsync(new(task.AccountId), cancellationToken);
            if (result.IsFailed) return result;
            var (allowedOasisAnimals, safetyFactor) = ReadRaidSettings(task.AccountId);
            var scan = await scanner.Scan(task.FarmId, allowedOasisAnimals, safetyFactor, task.ResumeTargetIndex, cancellationToken);
            if (scan.IsFailed) return Result.Fail(scan.Errors);

            task.ResumeTargetIndex = scan.Value.NextTargetIndex;
            if (scan.Value.RetryAt is DateTime retryAt)
            {
                if (retryAt <= DateTime.Now)
                    retryAt = DateTime.Now.AddSeconds(5);
                task.ExecuteAt = retryAt;
                return Skip.Error.WithError(
                    $"Insufficient Steppe Riders. Resume from target index {task.ResumeTargetIndex} at {retryAt:yyyy-MM-dd HH:mm:ss}");
            }

            task.ResumeTargetIndex = 0;
            return Result.Ok();
        }

        private (HashSet<int> AllowedOasisAnimals, int SafetyFactor) ReadRaidSettings(AccountId accountId)
        {
            using var context = contextFactory.CreateDbContext();
            var allowed = new HashSet<int>();
            foreach (var (setting, unitId) in OasisAnimalUnitBySetting)
            {
                if (context.BooleanByName(accountId, setting))
                    allowed.Add(unitId);
            }
            var safetyFactor = context.ByName(accountId, AccountSettingEnums.OasisRaidSafetyFactor);
            return (allowed, Math.Max(1, safetyFactor));
        }
    }
}
