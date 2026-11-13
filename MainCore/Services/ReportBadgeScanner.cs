using System.Data.Common;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterScoped<ReportBadgeScanner>]
    public sealed class ReportBadgeScanner(
        IChromeBrowser browser,
        ReportMonitor monitor,
        DataService dataService,
        IDbContextFactory<AppDbContext> contextFactory,
        LostReportTargetScanner lostReportTargetScanner)
    {
        public void Reset(AccountId accountId) => monitor.Reset(accountId);

        public async Task<bool> Scan(AccountId accountId, CancellationToken cancellationToken, bool scheduled = false)
        {
            var requiresFarmPageReopen = false;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var context = contextFactory.CreateDbContext();
                var notify = context.BooleanByName(accountId, AccountSettingEnums.EnableReportNotifications);
                var deactivate = context.BooleanByName(accountId, AccountSettingEnums.EnableAutoDeactivateLostReportTargets);
                if (scheduled && !notify) return requiresFarmPageReopen;
                if (!notify && !deactivate)
                {
                    monitor.Reset(accountId);
                    return requiresFarmPageReopen;
                }
                var session = monitor.GetSession(accountId);
                var result = await ReportParser.GetUnreadCount(browser.CurrentPage);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsSuccess)
                {
                    var changed = monitor.Observe(accountId, session, result.Value, dataService.AccountData, notify);
                    if (changed && deactivate)
                    {
                        requiresFarmPageReopen = true;
                        try
                        {
                            await lostReportTargetScanner.Scan(cancellationToken);
                        }
                        finally
                        {
                            // Opening reports marks them read; do not treat our own badge decrease as a new trigger.
                            var finalCount = await ReportParser.GetUnreadCount(browser.CurrentPage);
                            if (finalCount.IsSuccess)
                                monitor.Observe(accountId, session, finalCount.Value, dataService.AccountData, notify);
                            else
                                browser.Logger.Warning("Report badge after target processing unavailable: {Reason}", finalCount.Errors[0].Message);
                        }
                    }
                }
                else
                {
                    browser.Logger.Warning("Report badge scan skipped: {Reason}", result.Errors[0].Message);
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                browser.Logger.Warning("Report badge scan failed; keeping the previous count until the next scan");
                return true;
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

            return requiresFarmPageReopen;
        }
    }
}
