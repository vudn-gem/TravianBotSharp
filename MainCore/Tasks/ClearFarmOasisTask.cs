using MainCore.Commands.Navigate;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class ClearFarmOasisTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        IDbContextFactory<AppDbContext> contextFactory,
        ClearOasisScanner scanner)
    {
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
            using var context = contextFactory.CreateDbContext();
            var settings = OasisRaidSettings.Read(context, task.AccountId, task.FarmId);
            if (settings.IsFailed) return Stop.Error.WithErrors(settings.Errors);
            var scan = await scanner.Scan(task.FarmId, settings.Value, task.ResumeTargetIndex, cancellationToken);
            if (scan.IsFailed) return Result.Fail(scan.Errors);

            task.ResumeTargetIndex = scan.Value.NextTargetIndex;
            if (scan.Value.RetryAt is DateTime retryAt)
            {
                if (retryAt <= DateTime.Now)
                    retryAt = DateTime.Now.AddSeconds(5);
                task.ExecuteAt = retryAt;
                return Skip.Error.WithError(
                    $"Insufficient troops. Resume from target index {task.ResumeTargetIndex} at {retryAt:yyyy-MM-dd HH:mm:ss}");
            }

            task.ResumeTargetIndex = 0;
            return Result.Ok();
        }
    }
}
