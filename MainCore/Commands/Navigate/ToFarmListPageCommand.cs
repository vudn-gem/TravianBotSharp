using MainCore.Commands.Update;

namespace MainCore.Commands.Navigate
{
    [Handler]
    public sealed partial class ToFarmListPageCommand(
        IDbContextFactory<AppDbContext> contextFactory,
        IChromeBrowser browser,
        SwitchVillageCommand.Handler switchVillageCommand,
        ToDorfCommand.Handler toDorfCommand,
        UpdateBuildingCommand.Handler updateBuildingCommand)
    {
        public sealed record Command(AccountId AccountId) : IAccountCommand;

        private async ValueTask<Result> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (await IsOnFarmListPage())
                return Result.Ok();

            cancellationToken.ThrowIfCancellationRequested();
            var villageId = GetHasRallypointVillage(command.AccountId);
            if (villageId == VillageId.Empty)
                return Skip.Error.WithError("No rallypoint found. Recheck & load village has rallypoint in Village>Build tab");
            var switched = await switchVillageCommand.HandleAsync(new(villageId), cancellationToken);
            if (switched.IsFailed) return switched;
            switched = await browser.Wait(browser.CurrentPage.Locator(
                $"#sidebarBoxVillageList .listEntry.village.active[data-did='{villageId.Value}']"));
            if (switched.IsFailed) return switched;
            var toDorf = await toDorfCommand.HandleAsync(new(2), cancellationToken);
            if (toDorf.IsFailed) return toDorf;
            var updated = await updateBuildingCommand.HandleAsync(new(villageId), cancellationToken);
            if (updated.IsFailed) return Result.Fail(updated.Errors);
            var fallback = new UriBuilder(browser.CurrentPage.Url)
            {
                Path = "/build.php",
                Query = $"gid=16&tt=99&newdid={villageId.Value}",
            };
            var result = await FarmListNavigation.Open(browser, fallback.Uri.AbsoluteUri, villageId);
            if (result.IsFailed) return result;

            result = await browser.Wait(FarmListParser.GetStartAllButton(browser.CurrentPage));
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private async Task<bool> IsOnFarmListPage()
        {
            var page = browser.CurrentPage;
            var farmListPanel = page.Locator("#rallyPointFarmList");
            if (await farmListPanel.CountAsync() == 0) return false;
            var startButtons = FarmListParser.GetStartAllButton(page);
            return await startButtons.CountAsync() > 0;
        }

        private VillageId GetHasRallypointVillage(AccountId accountId)
        {
            using var context = contextFactory.CreateDbContext();
            return context.Villages
                .Where(x => x.AccountId == accountId.Value)
                .Where(x => x.Buildings.Any(x => x.Type == BuildingEnums.RallyPoint && x.Level > 0))
                .OrderByDescending(x => x.IsActive)
                .Select(x => new VillageId(x.Id))
                .FirstOrDefault();
        }
    }
}
