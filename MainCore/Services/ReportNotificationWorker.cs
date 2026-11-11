using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace MainCore.Services
{
    public sealed class ReportNotificationWorker(
        ReportNotificationOutbox outbox,
        TelegramSettingsStore settingsStore,
        TelegramSender sender,
        ILogger logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var notification = outbox.GetNext(DateTime.UtcNow);
                    if (notification is not null)
                    {
                        await Deliver(notification, stoppingToken);
                    }
                }
                catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException)
                {
                    logger.Error("Report notification storage is unavailable ({ErrorType}); retrying later", ex.GetType().Name);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }

        private async Task Deliver(ReportNotification notification, CancellationToken cancellationToken)
        {
            TelegramSendResult result;
            try
            {
                result = await sender.Send(settingsStore.Load(), notification.Text, cancellationToken);
            }
            catch (Exception ex) when (ex is CryptographicException or JsonException or PlatformNotSupportedException)
            {
                result = new(false, false, "Unable to read protected Telegram settings. Save the settings again on this Windows account.");
            }

            if (result.Success)
            {
                outbox.Complete(notification.Id);
                logger.Information("Telegram report notification {NotificationId} delivered for account {AccountId}",
                    notification.Id, notification.AccountId);
            }
            else
            {
                outbox.RecordFailure(notification, result.Retryable, result.RetryAfter);
                logger.Warning(
                    "Telegram report notification {NotificationId} for account {AccountId}: {Reason}; attempt {Attempt}; failed: {Failed}",
                    notification.Id, notification.AccountId, result.Error, notification.Attempts, notification.Failed);
            }
        }
    }
}
