using System.Data.Common;

namespace MainCore.Services
{
    [RegisterSingleton<TelegramCommandReceiver>]
    public sealed class TelegramCommandReceiver(
        TelegramCommandCursorStore cursors, TelegramCommandDispatcher dispatcher,
        TelegramSender sender, TimeProvider timeProvider, ILogger logger)
    {
        public void BeginSession(TelegramCredentials credentials, IEnumerable<TelegramUpdate> pending)
        {
            var lastId = pending.Select(x => x.Id).DefaultIfEmpty(-1).Max();
            cursors.Save(credentials, checked(lastId + 1));
        }

        public async Task Process(TelegramCredentials credentials, TelegramUpdate update, CancellationToken cancellationToken)
        {
            var offset = cursors.Load(credentials);
            if (update.Id < 0 || update.Id == long.MaxValue || offset is null || update.Id < offset) return;

            // Claim before executing: a crash can lose a command, but must never repeat a raid command.
            cursors.Save(credentials, update.Id + 1);
            var message = update.Message;
            if (message is null || !message.IsAuthorized(credentials)) return;
            if (message.Text is null || !message.Text.StartsWith('/')) return;

            var age = timeProvider.GetUtcNow().ToUnixTimeSeconds() - message.Date;
            string reply;
            if (age > 120 || age < -30)
                reply = "Command expired (maximum age: 2 minutes). Send the command again.";
            else
            {
                try
                {
                    reply = await dispatcher.Execute(message.Text, cancellationToken);
                }
                catch (Exception ex) when (ex is DbException or DbUpdateException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.Error("Telegram command failed ({ErrorType}); check TBS before retrying", ex.GetType().Name);
                    reply = "Command failed. Check TBS status and logs before retrying; it may have partially completed.";
                }
            }

            foreach (var chunk in ChunkReply(reply))
            {
                var result = await sender.Send(credentials, chunk, cancellationToken);
                if (!result.Success)
                {
                    logger.Warning("Telegram command reply failed: {Reason}. The command will not be executed again.", result.Error);
                    break;
                }
            }
        }

        public static IEnumerable<string> ChunkReply(string reply)
        {
            const int limit = 3500;
            while (reply.Length > limit)
            {
                var end = reply.LastIndexOf('\n', limit, limit);
                if (end <= 0) end = limit;
                if (char.IsHighSurrogate(reply[end - 1])) end--;
                yield return reply[..end];
                reply = reply[end..].TrimStart('\n');
            }
            if (reply.Length > 0) yield return reply;
        }
    }
}
