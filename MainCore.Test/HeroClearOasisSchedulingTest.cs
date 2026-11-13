using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Errors;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MainCore.Test
{
    public class HeroClearOasisSchedulingTest
    {
        [Theory]
        [InlineData("stop")]
        [InlineData("rescheduled-stop")]
        [InlineData("retry")]
        [InlineData("exception")]
        [InlineData("unknown")]
        [InlineData("absent")]
        [InlineData("confirmation")]
        [InlineData("waiting")]
        [InlineData("success")]
        [InlineData("pause")]
        [InlineData("cancel")]
        public async Task HandlesHeroFailuresWithoutPausingOtherTasks(string outcome)
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            using (var context = new AppDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                context.AccountsSetting.AddRange(
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.TaskDelayMin, Value = 0 },
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.TaskDelayMax, Value = 1 });
                context.SaveChanges();
            }

            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var logger = Substitute.For<Serilog.ILogger>();
            var browser = Substitute.For<IChromeBrowser>();
            browser.Logger.Returns(logger);
            var handler = Substitute.For<IHandler<HeroClearFarmOasisTask.Task, Result>>();
            handler.HandleAsync(Arg.Any<HeroClearFarmOasisTask.Task>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var task = call.Arg<HeroClearFarmOasisTask.Task>();
                    switch (outcome)
                    {
                        case "exception":
                            throw new InvalidOperationException("unexpected failure");
                        case "confirmation":
                            task.ConfirmationAttempted = true;
                            throw new InvalidOperationException("confirmation failed");
                        case "waiting":
                            task.ExecuteAt = DateTime.Now.AddMinutes(1);
                            return ValueTask.FromResult((Result)Skip.Error.WithError("Waiting for hero"));
                        case "stop":
                            return ValueTask.FromResult((Result)Stop.Error.WithError("unreadable hero"));
                        case "rescheduled-stop":
                            task.ExecuteAt = DateTime.Now.AddMinutes(1);
                            return ValueTask.FromResult((Result)Stop.Error.WithError("unreadable hero"));
                        case "retry":
                            return ValueTask.FromResult((Result)Retry.Error.WithError("navigation failed"));
                        case "unknown":
                            return ValueTask.FromResult(Result.Fail("unknown failure"));
                        case "pause":
                            throw new OperationCanceledException();
                        case "cancel":
                            return ValueTask.FromResult(Cancel.Error);
                        case "success":
                            task.ExecuteAt = DateTime.Now.AddMinutes(1);
                            return ValueTask.FromResult(Result.Ok());
                        default:
                            return ValueTask.FromResult((Result)Skip.Error.WithError("Hero is not in the village"));
                    }
                });

            using var provider = new ServiceCollection()
                .AddScoped<DataService>()
                .AddScoped(_ => new AppDbContext(options))
                .AddSingleton(browser)
                .AddSingleton(handler)
                .BuildServiceProvider();
            var rxQueue = new RxQueue(factory);
            var outbox = new ReportNotificationOutbox(Path.Combine(Path.GetTempPath(), $"tbs-unused-{Guid.NewGuid():N}.db"));
            var manager = new TaskManager(rxQueue, factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            var timer = new TimerManager(manager,
                new CustomServiceScopeFactory(provider.GetRequiredService<IServiceScopeFactory>()), rxQueue, factory,
                new BotStopNotifier(factory, outbox, logger), logger);
            var account = new AccountId(1);
            var queue = manager.GetTaskQueue(account);
            queue.Status = StatusEnums.Online;
            var heroTask = new HeroClearFarmOasisTask.Task(account, new FarmId(10));
            var nextTask = new StartAdventureTask.Task(account) { ExecuteAt = DateTime.Now.AddMinutes(2) };
            queue.Tasks.AddRange([heroTask, nextTask]);

            await timer.Execute(account);

            await handler.Received(1).HandleAsync(heroTask, Arg.Any<CancellationToken>());
            queue.Tasks.ShouldContain(nextTask);
            queue.IsExecuting.ShouldBeFalse();
            if (outcome is "pause" or "cancel")
            {
                queue.Status.ShouldBe(StatusEnums.Paused);
                queue.Tasks.ShouldContain(heroTask);
            }
            else
            {
                queue.Status.ShouldBe(StatusEnums.Online);
                if (outcome is "waiting" or "success") queue.Tasks.ShouldContain(heroTask);
                else queue.Tasks.ShouldNotContain(heroTask);
            }
            if (outcome == "confirmation")
                logger.Received().Warning("Hero raid confirmation was attempted. Check the game for the raid outcome before starting another task.");
        }
    }
}
