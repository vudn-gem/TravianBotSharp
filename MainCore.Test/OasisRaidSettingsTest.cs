using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.UI.Models.Input;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ReactiveUI.Builder;

namespace MainCore.Test
{
    public sealed class OasisRaidSettingsTest : IDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        private readonly AppDbContext _context;

        public OasisRaidSettingsTest()
        {
            _connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
            _context = new AppDbContext(options);
            _context.Database.EnsureCreated();
            _context.Accounts.AddRange(
                new Account { Id = 1, Username = "first", Server = "https://example.com" },
                new Account { Id = 2, Username = "second", Server = "https://example.com" });
            _context.FarmLists.AddRange(
                new Farm { Id = 10, AccountId = 1, Name = "first" },
                new Farm { Id = 11, AccountId = 1, Name = "second" },
                new Farm { Id = 20, AccountId = 2, Name = "third" });
            _context.SaveChanges();
        }

        [Fact]
        public void HeroReservationIsAccountWideAndRemovalDoesNotAffectOtherTasks()
        {
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ =>
                new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options));
            var logger = Substitute.For<Serilog.ILogger>();
            var outbox = new ReportNotificationOutbox(Path.Combine(Path.GetTempPath(), $"tbs-unused-{Guid.NewGuid():N}.db"));
            var manager = new TaskManager(new RxQueue(factory), factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, TimeProvider.System));
            var account = new AccountId(1);
            manager.TryAddHeroClearOasis(account, new FarmId(10)).ShouldBeTrue();
            manager.TryAddHeroClearOasis(account, new FarmId(11)).ShouldBeFalse();
            manager.TryAddHeroClearOasis(new AccountId(2), new FarmId(10)).ShouldBeFalse();
            manager.TryAddHeroClearOasis(new AccountId(2), new FarmId(20)).ShouldBeTrue();
            var task = manager.GetTaskList(account).OfType<HeroClearFarmOasisTask.Task>().Single();
            task.Stage = StageEnums.Executing;
            manager.RemoveHeroClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.Executing);
            task.Stage = StageEnums.Waiting;
            manager.RemoveHeroClearOasis(account, new FarmId(10)).ShouldBe(ClearOasisRemovalResult.Removed);
            manager.GetTaskList(new AccountId(2)).OfType<HeroClearFarmOasisTask.Task>().Count().ShouldBe(1);
            manager.TryAddHeroClearOasis(account, new FarmId(11)).ShouldBeTrue();
        }

        [Fact]
        public void InitializesIndependentHeroSettingsAndCascadesDeletion()
        {
            _context.InitializeFarmOasisSettings();
            _context.HeroOasisSettings.Count().ShouldBe(3);
            var setting = _context.HeroOasisSettings.Single(x => x.FarmId == 10);
            setting.MinHeroHealth = 70;
            setting.AllowedAnimals = 7;
            _context.SaveChanges();
            _context.InitializeFarmOasisSettings();
            _context.HeroOasisSettings.Single(x => x.FarmId == 10).MinHeroHealth.ShouldBe(70);
            _context.HeroOasisSettings.Single(x => x.FarmId == 11).AllowedAnimals.ShouldBe(0);
            _context.FarmOasisSettings.Single(x => x.FarmId == 10).AllowedAnimals.ShouldBe(0);
            _context.FarmLists.Where(x => x.Id == 10).ExecuteDelete();
            _context.HeroOasisSettings.AsNoTracking().Any(x => x.FarmId == 10).ShouldBeFalse();
        }

        [Fact]
        public void UpgradesOldDatabaseAndMigratesAccountSettingsOnlyOnce()
        {
            _context.Database.ExecuteSqlRaw("DROP TABLE \"FarmOasisSettings\"");
            _context.AccountsSetting.AddRange(
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.AllowRaidRatOasis, Value = 1 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.OasisRaidSafetyFactor, Value = 3 },
                new AccountSetting { AccountId = 2, Setting = AccountSettingEnums.AllowRaidWolfOasis, Value = 1 },
                new AccountSetting { AccountId = 2, Setting = AccountSettingEnums.OasisRaidSafetyFactor, Value = 4 });
            _context.SaveChanges();
            _context.InitializeFarmOasisSettings();

            var first = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(10));
            var second = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(11));
            var third = OasisRaidSettings.Read(_context, new AccountId(2), new FarmId(20));
            first.IsSuccess.ShouldBeTrue();
            second.IsSuccess.ShouldBeTrue();
            third.IsSuccess.ShouldBeTrue();
            first.Value.AllowedAnimals.ShouldContain(31);
            first.Value.SafetyFactor.ShouldBe(3);
            second.Value.AllowedAnimals.ShouldContain(31);
            third.Value.AllowedAnimals.ShouldContain(36);
            third.Value.AllowedAnimals.ShouldNotContain(31);
            third.Value.SafetyFactor.ShouldBe(4);

            var changed = _context.FarmOasisSettings.Single(x => x.FarmId == 11);
            changed.AllowedAnimals = 1 << 9;
            changed.SafetyFactor = 5;
            _context.SaveChanges();
            _context.InitializeFarmOasisSettings();
            var reread = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(11));
            reread.Value.AllowedAnimals.ShouldContain(40);
            reread.Value.AllowedAnimals.ShouldNotContain(31);
            reread.Value.SafetyFactor.ShouldBe(5);
            _context.FarmOasisSettings.Count().ShouldBe(3);
        }

        [Fact]
        public void SettingsAreIndependentAndEnforceAccountOwnership()
        {
            _context.InitializeFarmOasisSettings();
            var first = _context.FarmOasisSettings.Single(x => x.FarmId == 10);
            first.AllowedAnimals = 1 << 9;
            first.SafetyFactor = 5;
            first.AttackTroop = OasisAttackTroop.Marksman;
            first.RaidSteppeRiders = 0;
            first.RaidMarksmen = 2;
            first.RaidMarauders = 1;
            first.LimitAnimal = 7;
            _context.SaveChanges();
            var firstSettings = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(10));
            var secondSettings = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(11));
            firstSettings.Value.AllowedAnimals.ShouldContain(40);
            firstSettings.Value.SafetyFactor.ShouldBe(5);
            firstSettings.Value.AttackTroop.ShouldBe(OasisAttackTroop.Marksman);
            firstSettings.Value.RequiredTroops(0).ShouldBe((0L, 2L, 1L));
            firstSettings.Value.LimitAnimal.ShouldBe(7);
            secondSettings.Value.AllowedAnimals.ShouldBeEmpty();
            secondSettings.Value.SafetyFactor.ShouldBe(2);
            secondSettings.Value.AttackTroop.ShouldBe(OasisAttackTroop.SteppeRider);
            secondSettings.Value.RequiredTroops(0).ShouldBe((2L, 0L, 0L));
            secondSettings.Value.LimitAnimal.ShouldBe(0);
            OasisRaidSettings.Read(_context, new AccountId(2), new FarmId(10)).IsFailed.ShouldBeTrue();
            first.SafetyFactor = 0;
            _context.SaveChanges();
            OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(10)).IsFailed.ShouldBeTrue();
            first.SafetyFactor = 2;
            first.LimitAnimal = -1;
            _context.SaveChanges();
            OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(10)).IsFailed.ShouldBeTrue();
        }

        [Fact]
        public void DeletingFarmRemovesItsSettings()
        {
            _context.Database.ExecuteSqlRaw("DROP TABLE \"FarmOasisSettings\"");
            _context.InitializeFarmOasisSettings();
            _context.FarmLists.Where(x => x.Id == 10).ExecuteDelete();
            _context.FarmOasisSettings.AsNoTracking().Any(x => x.FarmId == 10).ShouldBeFalse();
        }

        [Fact]
        public void NewFarmCanBeInsertedWithIndependentDefaults()
        {
            _context.InitializeFarmOasisSettings();
            _context.FarmLists.Add(new Farm { Id = 12, AccountId = 1, Name = "new" });
            _context.FarmOasisSettings.Add(new FarmOasisSetting { FarmId = 12 });
            _context.SaveChanges();
            var result = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(12));
            result.IsSuccess.ShouldBeTrue();
            result.Value.SafetyFactor.ShouldBe(2);
            result.Value.AllowedAnimals.ShouldBeEmpty();
            result.Value.AttackTroop.ShouldBe(OasisAttackTroop.SteppeRider);
            result.Value.RequiredTroops(0).ShouldBe((2L, 0L, 0L));
        }

        [Fact]
        public void UpgradesPreviousFarmSettingsWithoutLosingValues()
        {
            _context.Database.ExecuteSqlRaw("DROP TABLE \"FarmOasisSettings\"");
            _context.Database.ExecuteSqlRaw("""
                CREATE TABLE "FarmOasisSettings" (
                    "FarmId" INTEGER PRIMARY KEY,
                    "AllowedAnimals" INTEGER NOT NULL,
                    "SafetyFactor" INTEGER NOT NULL,
                    FOREIGN KEY ("FarmId") REFERENCES "FarmLists" ("Id") ON DELETE CASCADE
                )
                """);
            _context.Database.ExecuteSqlRaw("""INSERT INTO "FarmOasisSettings" VALUES (10, 4, 5)""");
            _context.InitializeFarmOasisSettings();
            _context.InitializeFarmOasisSettings();
            var settings = _context.FarmOasisSettings.AsNoTracking().Single(x => x.FarmId == 10);
            settings.AllowedAnimals.ShouldBe(4);
            settings.SafetyFactor.ShouldBe(5);
            settings.AttackTroop.ShouldBe(OasisAttackTroop.SteppeRider);
            settings.RaidSteppeRiders.ShouldBe(2);
            settings.RaidMarksmen.ShouldBe(0);
            settings.RaidMarauders.ShouldBe(0);
            settings.LimitAnimal.ShouldBe(0);
        }

        [Fact]
        public void UpgradesTroopSettingsWithUnlimitedAnimalDefault()
        {
            _context.Database.ExecuteSqlRaw("DROP TABLE \"FarmOasisSettings\"");
            _context.Database.ExecuteSqlRaw("""
                CREATE TABLE "FarmOasisSettings" (
                    "FarmId" INTEGER PRIMARY KEY,
                    "AllowedAnimals" INTEGER NOT NULL,
                    "SafetyFactor" INTEGER NOT NULL,
                    "AttackTroop" INTEGER NOT NULL,
                    "RaidSteppeRiders" INTEGER NOT NULL,
                    "RaidMarksmen" INTEGER NOT NULL,
                    "RaidMarauders" INTEGER NOT NULL
                )
                """);
            _context.Database.ExecuteSqlRaw("""INSERT INTO "FarmOasisSettings" VALUES (10, 4, 5, 1, 0, 3, 1)""");
            _context.InitializeFarmOasisSettings();
            _context.InitializeFarmOasisSettings();
            var settings = _context.FarmOasisSettings.AsNoTracking().Single(x => x.FarmId == 10);
            settings.AttackTroop.ShouldBe(OasisAttackTroop.Marksman);
            settings.RaidMarksmen.ShouldBe(3);
            settings.RaidMarauders.ShouldBe(1);
            settings.LimitAnimal.ShouldBe(0);
        }

        [Theory]
        [InlineData(OasisAttackTroop.SteppeRider, 12, 0, 0)]
        [InlineData(OasisAttackTroop.Marksman, 0, 12, 0)]
        [InlineData(OasisAttackTroop.Marauder, 0, 0, 12)]
        public void SelectedAttackTroopUsesAnimalCountAndSafetyFactor(
            OasisAttackTroop troop, long steppe, long marksmen, long marauders)
        {
            var settings = new OasisRaidSettings(new HashSet<int> { 31 }, 3, troop, 1, 2, 3);
            settings.RequiredTroops(4).ShouldBe((steppe, marksmen, marauders));
            settings.RequiredTroops(0).ShouldBe((1L, 2L, 3L));
        }

        [Theory]
        [InlineData(2, 0, 0, true)]
        [InlineData(1, 1, 0, true)]
        [InlineData(1, 1, 1, true)]
        [InlineData(0, 2, 0, true)]
        [InlineData(0, 0, 0, false)]
        [InlineData(-1, 2, 0, false)]
        [InlineData(1000000, 0, 0, false)]
        public void ValidatesEmptyOasisTroopCounts(int steppe, int marksmen, int marauders, bool valid) =>
            OasisRaidSettings.IsValidRaidComposition(steppe, marksmen, marauders).ShouldBe(valid);

        [Fact]
        public void InputRoundTripsAllAnimalBitsAndSafetyFactor()
        {
            RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
            var input = new FarmOasisSettingInput();
            for (var mask = 0; mask < 1024; mask++)
            {
                input.Set(new FarmOasisSetting
                {
                    FarmId = 10, AllowedAnimals = mask, SafetyFactor = 6,
                    AttackTroop = OasisAttackTroop.Marauder, RaidSteppeRiders = 1, RaidMarksmen = 2, RaidMarauders = 3,
                    LimitAnimal = 17,
                });
                var result = input.Get(11);
                result.FarmId.ShouldBe(11);
                result.AllowedAnimals.ShouldBe(mask);
                result.SafetyFactor.ShouldBe(6);
                result.AttackTroop.ShouldBe(OasisAttackTroop.Marauder);
                result.RaidSteppeRiders.ShouldBe(1);
                result.RaidMarksmen.ShouldBe(2);
                result.RaidMarauders.ShouldBe(3);
                result.LimitAnimal.ShouldBe(17);
            }
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }
    }
}
