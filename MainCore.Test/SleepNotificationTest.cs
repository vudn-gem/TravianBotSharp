using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.UI.Models.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class SleepNotificationTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-sleep-{Guid.NewGuid():N}");
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        [Fact]
        public void QueuesSleepAndWakeMessagesWithAccessNameOnlyWhenEnabled()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            using (var context = new AppDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Accounts.AddRange(
                    new Account { Id = 1, Username = "enabled", Server = "https://example.com" },
                    new Account { Id = 2, Username = "disabled", Server = "https://example.com" });
                context.AccountsSetting.Add(new AccountSetting
                {
                    AccountId = 1,
                    Setting = AccountSettingEnums.EnableSleepNotifications,
                    Value = 1,
                });
                context.Accesses.AddRange(
                    new Access
                    {
                        AccountId = 1, Username = "older", Password = "", ProxyHost = "",
                        ProxyUsername = "", ProxyPassword = "", Useragent = "",
                        LastUsed = DateTime.Now.AddHours(-1),
                    },
                    new Access
                    {
                        AccountId = 1, Username = "current-access", Password = "", ProxyHost = "",
                        ProxyUsername = "", ProxyPassword = "", Useragent = "", LastUsed = DateTime.Now,
                    });
                context.SaveChanges();
            }

            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var notifier = new SleepNotificationService(factory, outbox, Substitute.For<Serilog.ILogger>());

            var expectedWakeUp = new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero);
            notifier.NotifySleep(new AccountId(1), TimeSpan.FromMinutes(90), expectedWakeUp);
            notifier.NotifyWakeUp(new AccountId(1), "wake-access", TimeSpan.FromMinutes(120));
            notifier.NotifySleep(new AccountId(2), TimeSpan.FromMinutes(30), expectedWakeUp);
            notifier.NotifyWakeUp(new AccountId(2), "disabled-access", TimeSpan.FromMinutes(45));

            var sleep = outbox.GetNext(DateTime.UtcNow);
            sleep.ShouldNotBeNull();
            sleep.AccountId.ShouldBe(1);
            sleep.Text.ShouldBe("[TBS] Sleep - current-access\nSleep time: 90 minutes\nExpected wake up: 2026-11-17 14:00:00 UTC+07:00");
            outbox.Complete(sleep.Id);

            var wake = outbox.GetNext(DateTime.UtcNow);
            wake.ShouldNotBeNull();
            wake.AccountId.ShouldBe(1);
            wake.Text.ShouldBe("[TBS] Walkup - wake-access\nActivity time: 120 minutes");
            outbox.Complete(wake.Id);
            outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
        }

        [Fact]
        public void SleepNotificationSettingRoundTripsAndDefaultsToDisabled()
        {
            var input = new AccountSettingInput();
            input.Set([]);
            input.EnableSleepNotifications.ShouldBeFalse();
            input.EnableSleepNotifications = true;
            input.Get()[AccountSettingEnums.EnableSleepNotifications].ShouldBe(1);
            input.Set(input.Get());
            input.EnableSleepNotifications.ShouldBeTrue();
            AppDbContext.AccountDefaultSettings[AccountSettingEnums.EnableSleepNotifications].ShouldBe(0);
        }

        public void Dispose()
        {
            _connection.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
