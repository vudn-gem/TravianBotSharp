using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Errors;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.UI.Models.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MainCore.Test
{
    public sealed class BotStopNotificationTest : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-stop-{Guid.NewGuid():N}");
        private readonly SqliteConnection _connection = new("Data Source=:memory:");

        [Theory]
        [InlineData(true, "stop", true, StatusEnums.Paused)]
        [InlineData(true, "screenshot-failure", true, StatusEnums.Paused)]
        [InlineData(false, "stop", false, StatusEnums.Paused)]
        [InlineData(true, "skip", false, StatusEnums.Online)]
        [InlineData(true, "cancel", false, StatusEnums.Paused)]
        [InlineData(true, "manual-stop", false, StatusEnums.Paused)]
        [InlineData(true, "success", false, StatusEnums.Online)]
        public async Task NotifiesOnlyWhenEnabledAndTaskFailurePausesAccount(
            bool enabled, string outcome, bool expectedNotification, StatusEnums expectedStatus)
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            using (var context = new AppDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                context.AccountsSetting.AddRange(
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.EnableBotStopNotifications, Value = enabled ? 1 : 0 },
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.TaskDelayMin, Value = 0 },
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.TaskDelayMax, Value = 1 });
                context.SaveChanges();
            }
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var logger = Substitute.For<Serilog.ILogger>();
            var browser = Substitute.For<IChromeBrowser>();
            browser.Logger.Returns(logger);
            if (outcome == "screenshot-failure")
                browser.Screenshot().Returns(_ => Task.FromException<string>(new InvalidOperationException("screenshot failed")));
            else
                browser.Screenshot().Returns(Task.FromResult("screenshot.png"));
            var handler = Substitute.For<IHandler<StartAdventureTask.Task, Result>>();
            TaskManager? manager = null;
            Result result = outcome switch
            {
                "stop" => Stop.Error.WithError("private diagnostic"),
                "screenshot-failure" => Stop.Error.WithError("private diagnostic"),
                "manual-stop" => Stop.Error.WithError("private diagnostic"),
                "skip" => Skip.Error,
                "cancel" => Cancel.Error,
                _ => Result.Ok(),
            };
            handler.HandleAsync(Arg.Any<StartAdventureTask.Task>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    if (outcome == "manual-stop")
                        manager!.GetTaskQueue(new AccountId(1)).CancellationTokenSource!.Cancel();
                    return ValueTask.FromResult(result);
                });

            using var provider = new ServiceCollection()
                .AddScoped<DataService>()
                .AddScoped(_ => new AppDbContext(options))
                .AddSingleton(browser)
                .AddSingleton(handler)
                .BuildServiceProvider();
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var rxQueue = new RxQueue(factory);
            manager = new TaskManager(rxQueue, factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            var timer = new TimerManager(manager,
                new CustomServiceScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
                rxQueue, factory, new BotStopNotifier(factory, outbox, logger), logger);
            var accountId = new AccountId(1);
            var queue = manager.GetTaskQueue(accountId);
            queue.Status = StatusEnums.Online;
            var task = new StartAdventureTask.Task(accountId);
            queue.Tasks.Add(task);

            if (outcome == "screenshot-failure")
                await Should.ThrowAsync<InvalidOperationException>(() => timer.Execute(accountId));
            else
                await timer.Execute(accountId);
            await timer.Execute(accountId);

            queue.Status.ShouldBe(expectedStatus);
            var notification = outbox.GetNext(DateTime.UtcNow);
            if (expectedNotification)
            {
                notification.ShouldNotBeNull();
                notification.AccountId.ShouldBe(accountId.Value);
                notification.Text.ShouldContain("Account: test");
                notification.Text.ShouldContain("Server: example.com");
                notification.Text.ShouldContain(task.Description);
                notification.Text.ShouldContain("Task failed");
                notification.Text.ShouldNotContain("private diagnostic");
                outbox.Complete(notification.Id);
                outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            }
            else notification.ShouldBeNull();
        }

        [Fact]
        public async Task SchedulerFailurePausesAccountAndQueuesNotification()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            using (var context = new AppDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                context.AccountsSetting.Add(new AccountSetting
                {
                    AccountId = 1, Setting = AccountSettingEnums.EnableBotStopNotifications, Value = 1
                });
                context.SaveChanges();
            }
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var logger = Substitute.For<Serilog.ILogger>();
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var rxQueue = new RxQueue(factory);
            var manager = new TaskManager(rxQueue, factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            using var provider = new ServiceCollection().BuildServiceProvider();
            var timer = new TimerManager(manager,
                new CustomServiceScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()),
                rxQueue, factory, new BotStopNotifier(factory, outbox, logger), logger);
            var accountId = new AccountId(1);
            var queue = manager.GetTaskQueue(accountId);
            queue.Status = StatusEnums.Online;
            var task = new StartAdventureTask.Task(accountId);
            queue.Tasks.Add(task);
            try
            {
                timer.Start(accountId);
                ReportNotification? notification = null;
                for (var i = 0; i < 100 && notification is null; i++)
                {
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                    notification = outbox.GetNext(DateTime.UtcNow);
                }
                notification.ShouldNotBeNull();
                notification.Text.ShouldContain("Unexpected scheduler error");
                queue.Status.ShouldBe(StatusEnums.Paused);
                queue.IsExecuting.ShouldBeFalse();
                task.Stage.ShouldBe(StageEnums.Waiting);
            }
            finally
            {
                timer.Shutdown();
            }
        }

        [Fact]
        public void NotificationSettingIsIndependentPerAccount()
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
                    AccountId = 1, Setting = AccountSettingEnums.EnableBotStopNotifications, Value = 1
                });
                context.SaveChanges();
            }
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var notifier = new BotStopNotifier(factory, outbox, Substitute.For<Serilog.ILogger>());

            notifier.Notify(new AccountId(2), "task", "Task failed");
            outbox.GetNext(DateTime.UtcNow).ShouldBeNull();
            notifier.Notify(new AccountId(1), "task", "Task failed");
            outbox.GetNext(DateTime.UtcNow)!.AccountId.ShouldBe(1);
        }

        [Fact]
        public void PerAccountSettingRoundTripsAndDefaultsToDisabled()
        {
            var input = new AccountSettingInput();
            input.Set([]);
            input.EnableBotStopNotifications.ShouldBeFalse();
            input.EnableBotStopNotifications = true;
            input.Get()[AccountSettingEnums.EnableBotStopNotifications].ShouldBe(1);
            input.Set(input.Get());
            input.EnableBotStopNotifications.ShouldBeTrue();
            AppDbContext.AccountDefaultSettings[AccountSettingEnums.EnableBotStopNotifications].ShouldBe(0);
        }

        public void Dispose()
        {
            _connection.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
