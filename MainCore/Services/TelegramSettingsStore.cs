using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MainCore.Services
{
    public sealed class TelegramCredentials
    {
        public string BotToken { get; set; } = "";
        public string ChatId { get; set; } = "";

        public bool IsValid =>
            Regex.IsMatch(BotToken, @"\A[0-9]+:[A-Za-z0-9_-]+\z", RegexOptions.CultureInvariant) &&
            Regex.IsMatch(ChatId, @"\A-?[0-9]+\z", RegexOptions.CultureInvariant);
    }

    public sealed class TelegramSettingsStore(string filePath)
    {
        private readonly Lock _sync = new();

        public TelegramCredentials Load()
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Telegram credentials require Windows.");
            lock (_sync)
            {
                if (!File.Exists(filePath)) return new TelegramCredentials();
                var encrypted = File.ReadAllBytes(filePath);
                var json = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                try
                {
                    return JsonSerializer.Deserialize<TelegramCredentials>(json)
                        ?? throw new JsonException("Invalid Telegram settings.");
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(json);
                }
            }
        }

        public void Save(TelegramCredentials credentials)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Telegram credentials require Windows.");
            if (!credentials.IsValid) throw new ArgumentException("Enter a valid bot token and numeric chat ID.");
            lock (_sync)
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(credentials);
                try
                {
                    var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    var temporaryPath = filePath + ".tmp";
                    File.WriteAllBytes(temporaryPath, encrypted);
                    File.Move(temporaryPath, filePath, overwrite: true);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(json);
                }
            }
        }
    }
}
