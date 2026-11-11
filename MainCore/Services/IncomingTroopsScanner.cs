using System.Data.Common;
using System.Text.Json;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterScoped<IncomingTroopsScanner>]
    public sealed class IncomingTroopsScanner(
        IChromeBrowser browser, IncomingTroopsMonitor monitor,
        DataService dataService, IDbContextFactory<AppDbContext> contextFactory,
        TaskManager taskManager)
    {
        public async Task Scan(AccountId accountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var context = contextFactory.CreateDbContext();
                if (!context.BooleanByName(accountId, AccountSettingEnums.EnableIncomingTroopsNotifications))
                {
                    monitor.Reset(accountId);
                    taskManager.Remove<CheckIncomingTroopsTask.Task>(accountId);
                    return;
                }
                taskManager.ScheduleIncomingScan(accountId, monitor.GetFallbackExecuteAt(accountId));
                var session = monitor.TryBeginScan(accountId);
                if (!session.HasValue) return;
                taskManager.ScheduleIncomingScan(accountId, monitor.GetFallbackExecuteAt(accountId));
                var result = await IncomingTroopsParser.Read(browser.CurrentPage);
                cancellationToken.ThrowIfCancellationRequested();
                if (result.IsFailed)
                {
                    browser.Logger.Warning("Incoming troops scan skipped: {Reason}", result.Errors[0].Message);
                    return;
                }
                foreach (var village in result.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var counts = await IncomingTroopsParser.ReadCounts(browser.CurrentPage, village, cancellationToken);
                        if (counts.IsSuccess)
                        {
                            monitor.Observe(accountId, session.Value, [counts.Value], dataService.AccountData);
                            browser.Logger.Information(
                                "Incoming troops scanned: {VillageName}, {AttackCount} attacks, {RaidCount} raids",
                                counts.Value.Name, counts.Value.Attacks, counts.Value.Raids);
                        }
                        else
                            browser.Logger.Warning("Incoming counts skipped for village {VillageId}: {Reason}",
                                village.Id, counts.Errors[0].Message);
                    }
                    catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                    {
                        browser.Logger.Warning("Incoming tooltip unavailable for village {VillageId}; preserving counts", village.Id);
                    }
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException or DbException or DbUpdateException or IOException or UnauthorizedAccessException)
            {
                browser.Logger.Warning("Incoming troops scan failed ({ErrorType}); preserving previous state", ex.GetType().Name);
            }
        }
    }
}
