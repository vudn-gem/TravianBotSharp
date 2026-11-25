using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Extensions.Hosting;

namespace MainCore.Services
{
    public sealed class TelegramCommandWorker(
        TelegramSettingsStore settings, TelegramCommandCursorStore cursors,
        TelegramCommandClient client, TelegramCommandReceiver receiver, ILogger logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            TelegramCredentials? active = null;
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(2);
                try
                {
                    var credentials = settings.Load();
                    if (credentials.CanReceiveCommands)
                    {
                        var offset = cursors.Load(credentials);
                        var starting = active is null || active.BotToken != credentials.BotToken || active.ChatId != credentials.ChatId;
                        var batch = await client.Poll(credentials, starting ? -1 : offset ?? -1, stoppingToken);
                        if (!batch.Ok || batch.Updates is null)
                        {
                            logger.Warning("Telegram command polling rejected (status {Status}); verify credentials, disable webhooks and other receivers",
                                batch.ErrorCode);
                            delay = TimeSpan.FromSeconds(Math.Clamp(batch.Parameters?.RetryAfter ?? 10, 1, 3600));
                        }
                        else if (StillEnabled(credentials))
                        {
                            if (starting || offset is null)
                            {
                                receiver.BeginSession(credentials, batch.Updates);
                                active = credentials;
                                logger.Information("Telegram command receiver ready; pending messages were discarded. Send /tbs_command for help.");
                            }
                            else
                            {
                                foreach (var update in batch.Updates.OrderBy(x => x.Id))
                                {
                                    stoppingToken.ThrowIfCancellationRequested();
                                    if (!StillEnabled(credentials)) break;
                                    await receiver.Process(credentials, update, stoppingToken);
                                }
                            }
                        }
                        else active = null;
                    }
                    else active = null;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException)
                {
                    logger.Warning("Telegram command polling timed out; retrying");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or CryptographicException or
                    PlatformNotSupportedException or IOException or UnauthorizedAccessException or DbException or DbUpdateException or OverflowException)
                {
                    // Transport exceptions can contain the bot token; never log their message or URI.
                    logger.Warning("Telegram command receiver unavailable ({ErrorType}); retrying", ex.GetType().Name);
                    delay = TimeSpan.FromSeconds(10);
                }
                await Task.Delay(delay, stoppingToken);
            }
        }

        private bool StillEnabled(TelegramCredentials polled)
        {
            var current = settings.Load();
            return current.CanReceiveCommands && current.BotToken == polled.BotToken && current.ChatId == polled.ChatId;
        }
    }
}
