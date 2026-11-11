using System.Security.Cryptography;
using System.Text;

using MainCore.Services;

namespace MainCore.Test
{
    public sealed class TelegramSettingsStoreTest
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        [Fact(Skip = "DPAPI is only available on Windows.", SkipUnless = nameof(IsWindows))]
        public void CredentialsRoundTripWithoutPlainTextOnDisk()
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tbs-telegram-{Guid.NewGuid():N}");
            var path = Path.Combine(directory, "telegram-settings.bin");
            try
            {
                var store = new TelegramSettingsStore(path);
                store.Load().IsValid.ShouldBeFalse();
                store.Save(new TelegramCredentials { BotToken = "123:fake-token", ChatId = "-456" });
                var saved = new TelegramSettingsStore(path).Load();
                saved.BotToken.ShouldBe("123:fake-token");
                saved.ChatId.ShouldBe("-456");
                Encoding.UTF8.GetString(File.ReadAllBytes(path)).ShouldNotContain("fake-token");

                File.WriteAllBytes(path, [1, 2, 3]);
                Should.Throw<CryptographicException>(() => store.Load());
            }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        }
    }
}
