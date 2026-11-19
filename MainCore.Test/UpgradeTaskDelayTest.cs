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
        public void ValidatesAndRoundTripsAccountSetting(int min, int max, bool valid)
        {
            var input = new AccountSettingInput();
            input.Set(new());
            input.UpgradeTaskDelay.Get().ShouldBe((0, 0));
            input.UpgradeTaskDelay.Set(min, max);

            var settings = input.Get();
            var reloaded = new AccountSettingInput();
            reloaded.Set(settings);
            reloaded.UpgradeTaskDelay.Get().ShouldBe((min, max));
            new AccountSettingInputValidator().Validate(reloaded).IsValid.ShouldBe(valid);
        }

        [Fact]
        public void BackfillsZeroAndKeepsBothDelayRangesIndependent()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
            using var context = new AppDbContext(options);
            context.Database.OpenConnection();
            context.Database.EnsureCreated();
            context.Accounts.Add(new Account { Id = 1, Username = "one", Server = "https://example.com" });
            context.SaveChanges();

            int UpgradeBuffer() => context.ByName(new(1), AccountSettingEnums.UpgradeTaskDelayMin,
                AccountSettingEnums.UpgradeTaskDelayMax, missingRangeDelay: 0);

            UpgradeBuffer().ShouldBe(0);
            context.FillAccountSettings();
            var settings = context.AccountsSetting.ToDictionary(x => x.Setting, x => x.Value);
            settings[AccountSettingEnums.UpgradeTaskDelayMin].ShouldBe(0);
            settings[AccountSettingEnums.UpgradeTaskDelayMax].ShouldBe(0);

            context.SaveAccountSetting(new(1), new()
            {
                [AccountSettingEnums.UpgradeTaskDelayMin] = 250,
                [AccountSettingEnums.UpgradeTaskDelayMax] = 250,
                [AccountSettingEnums.TaskDelayMin] = 1200,
                [AccountSettingEnums.TaskDelayMax] = 1200,
            });
            UpgradeBuffer().ShouldBe(250);
            context.ByName(new AccountId(1), AccountSettingEnums.TaskDelayMin, AccountSettingEnums.TaskDelayMax)
                .ShouldBe(1200);
        }
    }
}
