using MainCore.Entities;
using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using MainCore.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public class ConquestPersistenceTest
    {
        private static readonly DateTime Now = new(2026, 11, 12, 18, 0, 0);

        private static ConquestTask.Task CreateTask() => new(
            new AccountId(1), new VillageId(2), -3, 34,
            [new ConquestWave([19, 0, 0, 0, 0, 0, 0, 20, 0, 0, 0], 44, 41)],
            Now.AddDays(1).AddHours(2), Now.AddDays(1), 0.4)
        {
            OriginName = "Home (-10|24)",
            Attempts = 1,
        };

        [Fact]
        public void PlanSurvivesClosingAndReopeningDatabase()
        {
            var path = Path.Combine(Path.GetTempPath(), $"tbs-conquest-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            try
            {
                var original = CreateTask();
                using (var context = new AppDbContext(options))
                {
                    // This is also the upgrade path for an existing database.
                    context.InitializeConquestPlans();
                    context.InitializeConquestPlans();
                    context.ConquestPlans.Add(ConquestPlanPersistence.Capture(original));
                    context.SaveChanges();
                }
                using (var context = new AppDbContext(options))
                {
                    var restored = ConquestPlanPersistence.Restore(context.ConquestPlans.Single(), Now, true);
                    restored.PlanId.ShouldBe(original.PlanId);
                    restored.Key.ShouldBe(original.Key);
                    restored.OriginName.ShouldBe(original.OriginName);
                    restored.PrepareAt.ShouldBe(original.PrepareAt);
                    restored.DelaySeconds.ShouldBe(0.4);
                    restored.Attempts.ShouldBe(1);
                    restored.Waves[0].CatapultTarget1.ShouldBe(44);
                    restored.Waves[0].CatapultTarget2.ShouldBe(41);
                    restored.Status.ShouldBe("Scheduled");
                    restored.IsFinished.ShouldBeFalse();
                }
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [InlineData("Confirming", "Check in game")]
        [InlineData("Canceling", "Canceled")]
        [InlineData("Past", "Expired")]
        [InlineData("Attempts", "Failed")]
        [InlineData("Missing village", "Failed")]
        [InlineData("Completed", "Completed")]
        [InlineData("Failed", "Failed")]
        public void UnsafeOrFinishedPlansAreNeverRequeued(string scenario, string expectedStatus)
        {
            var plan = ConquestPlanPersistence.Capture(CreateTask());
            if (scenario == "Confirming") plan.ConfirmationAttempted = true;
            if (scenario == "Canceling") plan.CancelRequested = true;
            if (scenario == "Past") plan.SendAt = Now;
            if (scenario == "Attempts") plan.Attempts = 2;
            if (scenario is "Completed" or "Failed")
            {
                plan.IsFinished = true;
                plan.Status = scenario;
            }
            var restored = ConquestPlanPersistence.Restore(plan, Now, scenario != "Missing village");
            restored.Status.ShouldBe(expectedStatus);
            restored.IsFinished.ShouldBeTrue();
        }

        [Fact]
        public void ManagerRestoresOnceAndPersistsCancellationAcrossRestarts()
        {
            var path = Path.Combine(Path.GetTempPath(), $"tbs-conquest-queue-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            try
            {
                using (var context = new AppDbContext(options))
                {
                    context.Database.EnsureCreated();
                    context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                    context.Villages.Add(new Village { Id = 2, AccountId = 1, Name = "Home" });
                    context.SaveChanges();
                }
                var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
                factory.CreateDbContext().Returns(_ => new AppDbContext(options));
                var logger = Substitute.For<Serilog.ILogger>();
                TaskManager CreateManager()
                {
                    var outbox = new ReportNotificationOutbox(Path.Combine(Path.GetTempPath(), $"tbs-unused-{Guid.NewGuid():N}.db"));
                    return new TaskManager(new RxQueue(factory), factory, logger,
                        new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
                }
                var task = new ConquestTask.Task(new(1), new(2), -3, 34, CreateTask().Waves,
                    DateTime.Now.AddDays(1).AddHours(2), DateTime.Now.AddDays(1), 0.4);
                CreateManager().TryAdd(task).ShouldBeTrue();

                var restarted = CreateManager();
                restarted.RestoreConquests();
                restarted.RestoreConquests();
                var accountId = new AccountId(1);
                restarted.GetConquests(accountId).Count.ShouldBe(1);
                restarted.GetTaskList(accountId).Count.ShouldBe(1);
                restarted.GetStatus(accountId).ShouldBe(MainCore.Enums.StatusEnums.Offline);
                restarted.CancelConquest(accountId, restarted.GetConquests(accountId).Single()).ShouldBeTrue();

                var nextRestart = CreateManager();
                nextRestart.RestoreConquests();
                nextRestart.GetTaskList(accountId).ShouldBeEmpty();
                nextRestart.GetConquests(accountId).Single().Status.ShouldBe("Canceled");
            }
            finally { File.Delete(path); }
        }
    }
}
