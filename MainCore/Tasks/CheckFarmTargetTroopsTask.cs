using MainCore.Commands.Navigate;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class CheckFarmTargetTroopsTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        FarmTargetTroopsScanner scanner)
    {
        public sealed class Task(AccountId accountId, FarmId farmId) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            protected override string TaskName => $"Check oasis in farm list {FarmId.Value}";
            public override string Key => $"{AccountId}_{FarmId.Value}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            var result = await toFarmListPageCommand.HandleAsync(new(task.AccountId), cancellationToken);
            if (result.IsFailed) return result;
            return await scanner.Scan(task.FarmId, cancellationToken);
        }
    }
}
