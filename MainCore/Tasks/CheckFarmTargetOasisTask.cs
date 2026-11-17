using MainCore.Commands.Navigate;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class CheckFarmTargetOasisTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        FarmTargetTroopsScanner scanner,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        public sealed class Task(AccountId accountId, FarmId farmId, bool activate = false, bool synchronize = false) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            public bool Activate { get; } = activate;
            public bool Synchronize { get; } = synchronize;
            protected override string TaskName => $"{(Synchronize ? "Check" : Activate ? "Activate" : "Deactive")} oasis in farm list {FarmId.Value}";
            public override string Key => $"{AccountId}_{FarmId.Value}_{(Synchronize ? "Check" : Activate.ToString())}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            HashSet<int> allowedAnimals;
            int safeAnimal;
            using (var context = contextFactory.CreateDbContext())
            {
                var setting = context.FarmOasisSettings.AsNoTracking().SingleOrDefault(x =>
                    x.FarmId == task.FarmId.Value &&
                    context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == task.AccountId.Value));
                if (setting is null)
                    return Result.Fail("Farm list check oasis settings are missing.");
                allowedAnimals = Enumerable.Range(31, 10)
                    .Where(id => (setting.CheckAllowedAnimals & (1 << (id - 31))) != 0)
                    .ToHashSet();
                safeAnimal = setting.SafeAnimal;
            }
            var result = await toFarmListPageCommand.HandleAsync(new(task.AccountId), cancellationToken);
            if (result.IsFailed) return result;
            return await scanner.Scan(task.FarmId, task.Activate, allowedAnimals, safeAnimal, cancellationToken, task.Synchronize);
        }
    }
}
