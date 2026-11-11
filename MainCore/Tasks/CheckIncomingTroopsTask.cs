using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class CheckIncomingTroopsTask
    {
        public sealed class Task(AccountId accountId) : AccountTask(accountId)
        {
            protected override string TaskName => "Check incoming attack/raid";

            public override bool CanStart(AppDbContext context) =>
                context.BooleanByName(AccountId, AccountSettingEnums.EnableIncomingTroopsNotifications);
        }

        // AccountTaskBehavior performs the scan after this task, just like other tasks.
        private static ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            task.ExecuteAt = DateTime.Now.AddMinutes(10);
            return ValueTask.FromResult(Result.Ok());
        }
    }
}
