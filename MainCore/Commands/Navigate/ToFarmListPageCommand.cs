namespace MainCore.Commands.Navigate
{
    [Handler]
    public sealed partial class ToFarmListPageCommand(IChromeBrowser browser)
    {
        public sealed record Command(AccountId AccountId, bool ForceOpen = false) : IAccountCommand;

        private async ValueTask<Result> HandleAsync(
            Command command,
            CancellationToken cancellationToken)
        {
            if (!command.ForceOpen && await IsOnFarmListPage())
                return Result.Ok();

            cancellationToken.ThrowIfCancellationRequested();
            var page = browser.CurrentPage;
            var villageId = await VillagePanelParser.GetCurrentVillageId(page);
            if (villageId == VillageId.Empty)
                return Result.Fail("Unable to determine the active village before opening Farm List.");

            if (!await FarmListNavigation.IsShortcutAvailable(page))
            {
                var villages = page.Locator("#sidebarBoxVillageList .listEntry.village");
                if (await villages.CountAsync() == 0)
                    return Skip.Error.WithError("Farm List shortcut is unavailable and no village is listed.");
                var firstVillage = villages.First;
                var firstVillageIdText = await firstVillage.GetAttributeAsync("data-did");
                if (!int.TryParse(firstVillageIdText, out var firstVillageId) || firstVillageId <= 0)
                    return Result.Fail("Unable to determine the first village for Farm List shortcut fallback.");

                villageId = new VillageId(firstVillageId);
                if (await VillagePanelParser.GetCurrentVillageId(page) != villageId)
                {
                    var switched = await browser.Click(VillagePanelParser.GetVillageSwitchLink(page, villageId));
                    if (switched.IsFailed) return switched;
                    switched = await browser.Wait(VillagePanelParser.GetActiveVillageNode(page, villageId));
                    if (switched.IsFailed) return switched;
                }
            }

            var fallback = new UriBuilder(browser.CurrentPage.Url)
            {
                Path = "/building",
                Query = $"id=16&tt=99&newdid={villageId.Value}",
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

    }
}
