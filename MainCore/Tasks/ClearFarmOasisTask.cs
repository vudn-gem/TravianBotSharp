using MainCore.Commands.Navigate;
using MainCore.Enums;
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
        public sealed class Task(AccountId accountId, FarmId farmId, string? farmName = null) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            public int ResumeTargetIndex { get; set; } = 0;
            protected override string TaskName => $"Clear oasis in farm list {farmName ?? FarmId.Value.ToString()}";
            public override string Key => $"{AccountId}_{FarmId.Value}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            var result = await toFarmListPageCommand.HandleAsync(new(task.AccountId), cancellationToken);
            if (result.IsFailed) return result;
            using var context = contextFactory.CreateDbContext();
            var settings = OasisRaidSettings.Read(context, task.AccountId, task.FarmId);
            if (settings.IsFailed) return Stop.Error.WithErrors(settings.Errors);
            var maxTargetsPerPhase = Math.Max(1, context.ByName(task.AccountId,
                AccountSettingEnums.ClearTargetsPerPhaseMin, AccountSettingEnums.ClearTargetsPerPhaseMax));
            var scan = await scanner.Scan(
                task.FarmId,
                settings.Value,
                task.ResumeTargetIndex,
                maxTargetsPerPhase,
                cancellationToken);
            if (scan.IsFailed) return Result.Fail(scan.Errors);

            task.ResumeTargetIndex = scan.Value.NextTargetIndex;
            if (scan.Value.RetryAt is DateTime retryAt)
            {
                if (retryAt <= DateTime.Now)
                {
                    var taskBuffer = Math.Max(200, context.ByName(task.AccountId,
                        AccountSettingEnums.TaskDelayMin, AccountSettingEnums.TaskDelayMax));
                    retryAt = DateTime.Now.AddMilliseconds(taskBuffer);
                }
                task.ExecuteAt = retryAt;
                return Skip.Error.WithError(
                    $"Insufficient troops. Resume from target index {task.ResumeTargetIndex} at {retryAt:yyyy-MM-dd HH:mm:ss}");
            }
            if (scan.Value.PhaseLimited)
            {
                var phaseDelay = Math.Max(1, context.ByName(task.AccountId,
                    AccountSettingEnums.FarmIntervalMin, AccountSettingEnums.FarmIntervalMax));
                task.ExecuteAt = DateTime.Now.AddSeconds(phaseDelay);
                return Skip.Error.WithError(
                    $"Clear oasis phase limit reached. Resume from target index {task.ResumeTargetIndex} at {task.ExecuteAt:yyyy-MM-dd HH:mm:ss}");
            }

            task.ResumeTargetIndex = 0;
            return Result.Ok();
        }
    }
}
