using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.UI.Models.Input;
using MainCore.UI.Models.Validators;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class UpgradeTaskDelayTest
    {
        [Theory]
        [InlineData(0, 0, true)]
        [InlineData(250, 500, true)]
        [InlineData(-1, 0, false)]
        [InlineData(500, 250, false)]
        public void ValidatesAndRoundTripsVillageSetting(int min, int max, bool valid)
        {
            var input = new VillageSettingInput();
            input.Set(new());
            input.UpgradeTaskDelay.Get().ShouldBe((0, 0));
            input.UpgradeTaskDelay.Set(min, max);

            var settings = input.Get();
            var reloaded = new VillageSettingInput();
            reloaded.Set(settings);
            reloaded.UpgradeTaskDelay.Get().ShouldBe((min, max));
            new VillageSettingInputValidator().Validate(reloaded).IsValid.ShouldBe(valid);
        }

        [Fact]
        public void MigratesLegacyDelayOnceAndStoresRangesPerVillage()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
            using var context = new AppDbContext(options);
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            context.Accounts.AddRange(
                new Account { Id = 1, Username = "one", Server = "https://example.com" },
                new Account { Id = 2, Username = "two", Server = "https://example.com" });
            context.Villages.AddRange(
                new Village { Id = 11, AccountId = 1, Name = "First" },
                new Village { Id = 12, AccountId = 1, Name = "Second" },
                new Village { Id = 21, AccountId = 2, Name = "Other account" });
            context.AccountsSetting.AddRange(
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.UpgradeTaskDelayMin, Value = 250 },
                new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.UpgradeTaskDelayMax, Value = 250 });
            context.SaveChanges();

            int UpgradeBuffer(int id) => context.ByName(new VillageId(id), VillageSettingEnums.UpgradeTaskDelayMin,
                VillageSettingEnums.UpgradeTaskDelayMax);

            UpgradeBuffer(11).ShouldBe(0);
            context.FillVillageSettings();
            UpgradeBuffer(11).ShouldBe(250);
            UpgradeBuffer(12).ShouldBe(250);
            UpgradeBuffer(21).ShouldBe(0);

            context.SaveVillageSetting(new(11), new()
            {
                [VillageSettingEnums.UpgradeTaskDelayMin] = 500,
                [VillageSettingEnums.UpgradeTaskDelayMax] = 500,
            });
            context.Villages.Add(new Village { Id = 13, AccountId = 1, Name = "New" });
            context.SaveChanges();
            context.FillVillageSettings(new(1), new(13));
            context.FillVillageSettings();

            UpgradeBuffer(11).ShouldBe(500);
            UpgradeBuffer(12).ShouldBe(250);
            UpgradeBuffer(13).ShouldBe(0);
        }
    }
}
