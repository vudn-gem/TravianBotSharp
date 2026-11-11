using System.Data.Common;

using MainCore.Infrasturecture.Extensions;
using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class CheckReportsTask(
        IChromeBrowser browser,
        ReportMonitor monitor,
        DataService dataService,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        public sealed class Task(AccountId accountId) : AccountTask(accountId)
        {
            protected override string TaskName => "Check report badge";

            public override bool CanStart(AppDbContext context) =>
                context.BooleanByName(AccountId, AccountSettingEnums.EnableReportNotifications);
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var context = contextFactory.CreateDbContext();
            if (!task.CanStart(context))
            {
                monitor.Reset(task.AccountId);
                return Skip.Error.WithError("Report notifications are disabled");
            }
            var session = monitor.GetSession(task.AccountId);
            try
            {
                var result = await ReportParser.GetUnreadCount(browser.CurrentPage);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsSuccess)
                {
                    monitor.Observe(task.AccountId, session, result.Value, dataService.AccountData);
                }
                else
                {
                    browser.Logger.Warning("Report badge scan skipped: {Reason}", result.Errors[0].Message);
                }
            }
            catch (PlaywrightException)
            {
                browser.Logger.Warning("Report badge scan failed; keeping the previous count until the next scan");
            }
            catch (DbException ex)
            {
                browser.Logger.Error(ex, "Unable to queue report notification; keeping the previous count");
            }
            catch (DbUpdateException ex)
            {
                browser.Logger.Error(ex, "Unable to queue report notification; keeping the previous count");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                browser.Logger.Error(ex, "Unable to access notification storage; keeping the previous count");
            }
            finally
            {
                task.ExecuteAt = DateTime.Now.AddSeconds(60);
            }

            return Result.Ok();
        }
    }
}
