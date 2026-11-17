using System.Data.Common;
using System.Globalization;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterSingleton<SleepNotificationService>]
    public sealed class SleepNotificationService(
        IDbContextFactory<AppDbContext> contextFactory,
        ReportNotificationOutbox outbox,
        ILogger logger)
    {
        public void NotifySleep(AccountId accountId, TimeSpan sleepDuration, DateTimeOffset expectedWakeUp)
        {
            try
            {
                using var context = contextFactory.CreateDbContext();
                if (!context.BooleanByName(accountId, AccountSettingEnums.EnableSleepNotifications)) return;

                var accessName = context.Accesses
                    .Where(x => x.AccountId == accountId.Value)
                    .OrderByDescending(x => x.LastUsed)
                    .Select(x => x.Username)
                    .FirstOrDefault();
                if (string.IsNullOrWhiteSpace(accessName))
                {
                    logger.Warning("Cannot queue sleep notification: no access name for account {AccountId}",
                        accountId.Value);
                    return;
                }

                var name = Limit(accessName, 200);
                var duration = FormatMinutes(sleepDuration);
                var wakeUpTime = expectedWakeUp.ToOffset(TimeSpan.FromHours(7))
                    .ToString("yyyy-MM-dd HH:mm:ss 'UTC+07:00'", CultureInfo.InvariantCulture);
                outbox.Enqueue(accountId,
                    $"[TBS] Sleep - {name}\nSleep time: {duration} minutes\nExpected wake up: {wakeUpTime}");
            }
            catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException)
            {
                logger.Error(ex, "Unable to queue sleep notification for account {AccountId}", accountId.Value);
            }
        }

        public void NotifyWakeUp(AccountId accountId, string accessName, TimeSpan activityDuration)
        {
            try
            {
                using var context = contextFactory.CreateDbContext();
                if (!context.BooleanByName(accountId, AccountSettingEnums.EnableSleepNotifications)) return;
                var name = Limit(accessName, 200);
                var duration = FormatMinutes(activityDuration);
                outbox.Enqueue(accountId, $"[TBS] Walkup - {name}\nActivity time: {duration} minutes");
            }
            catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException)
            {
                logger.Error(ex, "Unable to queue wake-up notification for account {AccountId}", accountId.Value);
            }
        }

        private static string FormatMinutes(TimeSpan duration) =>
            duration.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Limit(string text, int length) =>
            text.Length <= length ? text : text[..length] + "...";
    }
}
