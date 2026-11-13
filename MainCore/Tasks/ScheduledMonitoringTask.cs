using MainCore.Infrasturecture.Extensions;
using MainCore.Commands.Navigate;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class ScheduledMonitoringTask(
        IDbContextFactory<AppDbContext> contextFactory,
        IChromeBrowser browser,
        IHandler<ToDorfCommand.Command, Result> toDorfCommand,
        ReportBadgeScanner reportScanner,
        IncomingTroopsScanner incomingScanner)
    {
        public sealed class Task(AccountId accountId) : AccountTask(accountId)
        {
            protected override string TaskName => "Scheduled report and incoming scan";

            public override bool CanStart(AppDbContext context) =>
                context.BooleanByName(AccountId, AccountSettingEnums.EnableScheduledMonitoring) &&
                (context.BooleanByName(AccountId, AccountSettingEnums.EnableReportNotifications) ||
                 context.BooleanByName(AccountId, AccountSettingEnums.EnableIncomingTroopsNotifications));
        }

        public static DateTime NextRun(AppDbContext context, AccountId accountId)
        {
            var min = context.ByName(accountId, AccountSettingEnums.ScheduledMonitoringIntervalMin);
            var max = context.ByName(accountId, AccountSettingEnums.ScheduledMonitoringIntervalMax);
            if (min < 1 || max > 86400 || min > max)
                throw new InvalidOperationException("Scheduled scan interval must be between 1 and 86400 seconds, with minimum not exceeding maximum.");
            return DateTime.Now.AddSeconds(Random.Shared.Next(min, max + 1));
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            using (var context = contextFactory.CreateDbContext())
            {
                if (!task.CanStart(context)) return Skip.Error.WithError("Scheduled monitoring has no enabled notification scan.");
            }

            if (Uri.TryCreate(browser.CurrentUrl, UriKind.Absolute, out var currentUrl))
            {
                var destination = currentUrl.AbsolutePath == "/dorf2.php" ? 1 : 2;
                try
                {
                    var navigation = await toDorfCommand.HandleAsync(new(destination), cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (navigation.IsFailed)
                        browser.Logger.Warning("Scheduled scan navigation failed; scanning current page: {Errors}",
                            string.Join("; ", navigation.Errors.Select(error => error.Message)));
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    browser.Logger.Warning(ex, "Scheduled scan navigation failed; scanning current page");
                }
            }
            else
                browser.Logger.Warning("Scheduled scan could not determine the current page; scanning without navigation");
            cancellationToken.ThrowIfCancellationRequested();

            await reportScanner.Scan(task.AccountId, cancellationToken, scheduled: true);
            await incomingScanner.Scan(task.AccountId, cancellationToken, scheduled: true);
            cancellationToken.ThrowIfCancellationRequested();

            using var updatedContext = contextFactory.CreateDbContext();
            if (task.CanStart(updatedContext))
                task.ExecuteAt = NextRun(updatedContext, task.AccountId);
            return Result.Ok();
        }
    }
}
