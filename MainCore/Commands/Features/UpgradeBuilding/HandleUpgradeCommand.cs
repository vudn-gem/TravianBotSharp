using MainCore.Infrasturecture.Extensions;

namespace MainCore.Commands.Features.UpgradeBuilding
{
    [Handler]
    public sealed partial class HandleUpgradeCommand(
        IChromeBrowser browser,
        IDbContextFactory<AppDbContext> contextFactory,
        ILogger logger)
    {
        public sealed record Command(VillageId VillageId, NormalBuildPlan Plan) : IVillageCommand;

        private async ValueTask<Result> HandleAsync(Command command)
        {
            var (villageId, plan) = command;

            LogBuildingInfo(villageId, plan);

            Result result;
            if (!UnskippableBuildings.Contains(plan.Type) && await CanUseSpecialUpgrade(villageId, plan.Type))
            {
                result = await SpecialUpgrade(plan.Type);
                if (result.IsFailed) return result;
            }
            else
            {
                result = await Upgrade(plan.Type);
                if (result.IsFailed) return result;
            }

            return Result.Ok();
        }

        private static readonly List<BuildingEnums> UnskippableBuildings =
        [
            BuildingEnums.Residence,
            BuildingEnums.Palace,
            BuildingEnums.CommandCenter,
        ];

        private void LogBuildingInfo(VillageId villageId, NormalBuildPlan plan)
        {
            using var context = contextFactory.CreateDbContext();

            var queueBuilding = context.QueueBuildings
                .Where(x => x.VillageId == villageId.Value)
                .FirstOrDefault(x => x.Location == plan.Location);

            if (queueBuilding is not null)
            {
                logger.Information("{Type} at location {Location} is in queue at level {Level}", queueBuilding.Type, queueBuilding.Location, queueBuilding.Level);
            }
            else
            {
                var building = context.Buildings
                   .Where(x => x.VillageId == villageId.Value)
                   .FirstOrDefault(x => x.Location == plan.Location);

                if (building is not null)
                {
                    logger.Information("{Type} at location {Location} is at level {Level}", building.Type, building.Location, building.Level);
                }
            }
        }

        private async Task<bool> CanUseSpecialUpgrade(VillageId villageId, BuildingEnums building)
        {
            int minimumMinutes;
            using (var context = contextFactory.CreateDbContext())
            {
                if (!context.BooleanByName(villageId, VillageSettingEnums.UseSpecialUpgrade)) return false;
                var key = VillageSettingEnums.SpecialUpgradeMinimumMinutes;
                minimumMinutes = context.ByName(villageId, [key])
                    .GetValueOrDefault(key, AppDbContext.VillageDefaultSettings[key]);
            }

            if (minimumMinutes == 0) return true;
            if (minimumMinutes < 0)
            {
                logger.Warning("Invalid special upgrade minimum time {Minutes}; using the normal upgrade button.", minimumMinutes);
                return false;
            }

            var duration = await UpgradeParser.ReadUpgradeDuration(browser.CurrentPage, building);
            if (duration is null)
            {
                logger.Warning("Could not read the normal upgrade duration for {Building}; using the normal upgrade button.", building);
                return false;
            }
            return duration.Value > TimeSpan.FromMinutes(minimumMinutes);
        }

        private async Task<Result> SpecialUpgrade(BuildingEnums building)
        {
            var button = await UpgradeParser.GetSpecialButton(browser.CurrentPage, building);
            var result = await browser.Click(button);
            if (result.IsFailed) return result;

            result = await HandleAds();
            if (result.IsFailed) return result;
            return Result.Ok();
        }

        private async Task<Result> HandleAds()
        {
            var page = browser.CurrentPage;
            var videoFeature = page.Locator("#videoFeature");

            var result = await browser.Wait(videoFeature);
            if (result.IsFailed) return result;

            var classess = await videoFeature.GetAttributeAsync("class") ?? "";
            if (classess.Contains("infoScreen"))
            {
                var checkBoxDontShowAgain = page.Locator("#videoFeature label.checkbox input[name=\"preference\"]");
                result = await browser.Click(checkBoxDontShowAgain);
                if (result.IsFailed) return result;

                var buttonWatchAds = page.Locator("#videoFeature div.buttonWrapper button");
                result = await browser.Click(buttonWatchAds);
                if (result.IsFailed) return result;
            }
            await Task.Delay(20_000);
            result = await browser.WaitPageChanged("(resources|village)");
            if (result.IsFailed) return result;
            return Result.Ok();
        }

        private async Task<Result> Upgrade(BuildingEnums building)
        {
            var button = await UpgradeParser.GetNormalButton(browser.CurrentPage, building);
            var result = await browser.Click(button);
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged("(resources|village)");
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}
