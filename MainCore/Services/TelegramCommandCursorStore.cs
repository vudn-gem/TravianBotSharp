using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MainCore.Services
{
    public sealed class TelegramCommandCursorStore(string filePath)
    {
        private readonly Lock _sync = new();
        private sealed record Cursor(string Identity, long Offset);

        private static string Identity(TelegramCredentials credentials) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{credentials.BotToken}|{credentials.ChatId}")));

        public long? Load(TelegramCredentials credentials)
        {
            lock (_sync)
            {
                if (!File.Exists(filePath)) return null;
                var cursor = JsonSerializer.Deserialize<Cursor>(File.ReadAllText(filePath))
                    ?? throw new JsonException("Invalid Telegram command cursor.");
                if (cursor.Offset < 0) throw new JsonException("Invalid Telegram command offset.");
                return cursor.Identity == Identity(credentials) ? cursor.Offset : null;
            }
        }

        public void Save(TelegramCredentials credentials, long offset)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            lock (_sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                var temporary = filePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(new Cursor(Identity(credentials), offset)));
                File.Move(temporary, filePath, overwrite: true);
            }
        }
    }
}
