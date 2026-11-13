using System.Data.Common;

using MainCore.Infrasturecture.Extensions;

namespace MainCore.Services
{
    [RegisterSingleton<BotStopNotifier>]
    public sealed class BotStopNotifier(
        IDbContextFactory<AppDbContext> contextFactory,
        ReportNotificationOutbox outbox,
        ILogger logger)
    {
        public void Notify(AccountId accountId, string taskName, string reason)
        {
            try
            {
                using var context = contextFactory.CreateDbContext();
                if (!context.BooleanByName(accountId, AccountSettingEnums.EnableBotStopNotifications)) return;
                var account = context.Accounts
                    .Where(x => x.Id == accountId.Value)
                    .Select(x => new { x.Username, x.Server })
                    .SingleOrDefault();
                if (account is null)
                {
                    logger.Warning("Cannot queue bot stop notification: account {AccountId} not found", accountId.Value);
                    return;
                }

                var server = Uri.TryCreate(account.Server, UriKind.Absolute, out var uri) ? uri.Host : "(unknown)";
                outbox.Enqueue(accountId,
                    $"[TBS] Bot paused due to an error\nAccount: {Limit(account.Username, 200)} (ID {accountId.Value})\nServer: {Limit(server, 200)}\nTask: {Limit(taskName, 300)}\nReason: {reason}\nCheck the TBS logs for details.\nDetected: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            }
            catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException)
            {
                logger.Error(ex, "Unable to queue bot stop notification for account {AccountId}", accountId.Value);
            }
        }

        private static string Limit(string text, int length) =>
            text.Length <= length ? text : text[..length] + "...";
    }
}
