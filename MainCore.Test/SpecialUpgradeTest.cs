using FluentResults;
using MainCore.Commands.Features.UpgradeBuilding;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using MainCore.Test.Parsers;
using MainCore.UI.Models.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class SpecialUpgradeTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(true, null, "0:10:01", false, false, "special")]
        [InlineData(true, 10, "0:10:00", false, false, "normal")]
        [InlineData(true, 10, "0:09:59", false, false, "normal")]
        [InlineData(true, 10, "25:00:00", true, false, "special")]
        [InlineData(true, 10, "", false, false, "normal")]
        [InlineData(true, 0, "", false, false, "special")]
        [InlineData(false, 0, "0:20:00", false, false, "normal")]
        [InlineData(true, 0, "", false, true, "normal")]
        public async Task ChoosesUpgradeButton(bool enabled, int? minimumMinutes, string duration,
            bool emptySite, bool excludedBuilding, string expected)
        {
            await using var browserContext = await CreateContextAsync();
            var page = await browserContext.NewPageAsync();
            var building = excludedBuilding ? BuildingEnums.Residence : BuildingEnums.Warehouse;
            await page.SetContentAsync($"""
                <div id="build" class="{(emptySite ? "gid0" : "gid10")}">
                    <div id="contract_building{(int)building}">
                        <div class="upgradeBuilding">
                            <div class="upgradeButtonsContainer">
                                <div class="section1">
                                    <button id="normal" class="{(emptySite ? "new" : "build")}">Normal</button>
                                    <span class="clocks">{duration}</span>
                                </div>
                                <div class="section2">
                                    <button id="special" class="videoFeatureButton">Special</button>
                                    <span class="clocks">0:01:00</span>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
                """);

            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options;
            using var seed = new AppDbContext(options);
            seed.Database.OpenConnection();
            seed.Database.EnsureCreated();
            seed.Accounts.Add(new Account { Id = 1, Username = "one", Server = "https://example.com" });
            seed.Villages.Add(new Village { Id = 1, AccountId = 1, Name = "Home" });
            seed.VillagesSetting.Add(new VillageSetting
                { VillageId = 1, Setting = VillageSettingEnums.UseSpecialUpgrade, Value = enabled ? 1 : 0 });
            if (minimumMinutes is int threshold)
                seed.VillagesSetting.Add(new VillageSetting
                    { VillageId = 1, Setting = VillageSettingEnums.SpecialUpgradeMinimumMinutes, Value = threshold });
            seed.SaveChanges();

            var handlerOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(seed.Database.GetDbConnection()).Options;
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(handlerOptions));
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            var clicks = new List<string?>();
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                clicks.Add(await call.Arg<ILocator>().GetAttributeAsync("id"));
                return Result.Fail("Stop after button selection.");
            });
            var handler = new HandleUpgradeCommand.HandleBehavior(
                new HandleUpgradeCommand(browser, factory, Substitute.For<Serilog.ILogger>()));

            await handler.HandleAsync(new(new VillageId(1), new NormalBuildPlan { Type = building }), CancellationToken.None);

            clicks.ShouldBe([expected]);
        }

        [Fact]
        public void LegacyVillageSettingsDefaultToTenAndExplicitZeroIsPreserved()
        {
            var input = new VillageSettingInput();
            input.Set([]);
            input.SpecialUpgradeMinimumMinutes.ShouldBe(10);
            input.SpecialUpgradeMinimumMinutes = 0;
            input.Set(input.Get());
            input.SpecialUpgradeMinimumMinutes.ShouldBe(0);
        }
    }
}
