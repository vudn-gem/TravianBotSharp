using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
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
            _context.SaveChanges();
            var firstSettings = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(10));
            var secondSettings = OasisRaidSettings.Read(_context, new AccountId(1), new FarmId(11));
            firstSettings.Value.AllowedAnimals.ShouldContain(40);
            firstSettings.Value.SafetyFactor.ShouldBe(5);
            secondSettings.Value.AllowedAnimals.ShouldBeEmpty();
            secondSettings.Value.SafetyFactor.ShouldBe(2);
            OasisRaidSettings.Read(_context, new AccountId(2), new FarmId(10)).IsFailed.ShouldBeTrue();
            first.SafetyFactor = 0;
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
        }

        [Fact]
        public void InputRoundTripsAllAnimalBitsAndSafetyFactor()
        {
            RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
            var input = new FarmOasisSettingInput();
            for (var mask = 0; mask < 1024; mask++)
            {
                input.Set(new FarmOasisSetting { FarmId = 10, AllowedAnimals = mask, SafetyFactor = 6 });
                var result = input.Get(11);
                result.FarmId.ShouldBe(11);
                result.AllowedAnimals.ShouldBe(mask);
                result.SafetyFactor.ShouldBe(6);
            }
        }

        public void Dispose()
        {
            _context.Dispose();
            _connection.Dispose();
        }
    }
}
