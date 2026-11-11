using System.Data.Common;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterScoped<ReportBadgeScanner>]
    public sealed class ReportBadgeScanner(
        IChromeBrowser browser,
        ReportMonitor monitor,
        DataService dataService,
        IDbContextFactory<AppDbContext> contextFactory)
    {
        public async Task Scan(AccountId accountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var session = monitor.GetSession(accountId);
            try
            {
                using var context = contextFactory.CreateDbContext();
                if (!context.BooleanByName(accountId, AccountSettingEnums.EnableReportNotifications))
                {
                    monitor.Reset(accountId);
                    return;
                }
                var result = await ReportParser.GetUnreadCount(browser.CurrentPage);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsSuccess)
                {
                    monitor.Observe(accountId, session, result.Value, dataService.AccountData);
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
        }
    }
}
