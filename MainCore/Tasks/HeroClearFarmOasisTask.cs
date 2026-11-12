using MainCore.Commands.Navigate;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class HeroClearFarmOasisTask(
        IHandler<ToFarmListPageCommand.Command, Result> toFarmListPageCommand,
        IDbContextFactory<AppDbContext> contextFactory,
        HeroClearOasisScanner scanner)
    {
        public sealed class Task(AccountId accountId, FarmId farmId) : AccountTask(accountId)
        {
            public FarmId FarmId { get; } = farmId;
            public VillageId? Owner { get; set; }
            public HashSet<string> ProcessedSlots { get; } = [];
            public DateTime? HeroReturnAt { get; set; }
            public bool ConfirmationAttempted { get; set; }
            protected override string TaskName => $"Hero clear oasis in farm list {FarmId.Value}";
            public override string Key => $"{AccountId}_{FarmId.Value}";
            public override bool CanStart(AppDbContext context) =>
                context.FarmLists.Any(x => x.Id == FarmId.Value && x.AccountId == AccountId.Value);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.ConfirmationAttempted)
                return Stop.Error.WithError("Hero raid confirmation outcome is unknown. Check the game and remove this task before restarting it.");
            if (task.HeroReturnAt is DateTime returnAt && returnAt.AddSeconds(5) > DateTime.Now)
            {
                task.ExecuteAt = returnAt.AddSeconds(5);
                return Skip.Error.WithError("Waiting for the hero's estimated return.");
            }
            using var context = contextFactory.CreateDbContext();
            var settings = context.HeroOasisSettings.AsNoTracking().SingleOrDefault(x =>
                x.FarmId == task.FarmId.Value &&
                context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == task.AccountId.Value));
            if (settings is null) return Skip.Error.WithError("Hero oasis settings or farm list no longer exist.");
            var valid = HeroOasisSettings.Validate(settings);
            if (valid.IsFailed) return Skip.Error.WithErrors(valid.Errors);

            var opened = await toFarmListPageCommand.HandleAsync(new(task.AccountId), cancellationToken);
            if (opened.IsFailed) return opened;
            return await scanner.Scan(task, settings, cancellationToken);
        }
    }
}
