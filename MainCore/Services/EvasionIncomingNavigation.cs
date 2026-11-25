namespace MainCore.Services
{
    public static class EvasionIncomingNavigation
    {
        public static async Task<Result> Open(IChromeBrowser browser, VillageId villageId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = browser.CurrentPage;
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var origin) ||
                origin.Scheme is not ("https" or "http"))
                return Result.Fail("Cannot determine the account server for incoming navigation.");

            var link = page.Locator("#sidebarBoxLinklist")
                .GetByRole(AriaRole.Link, new() { Name = "Incoming Troops", Exact = true });
            try
            {
                if (await link.CountAsync() == 1 &&
                    Uri.TryCreate(origin, await link.GetAttributeAsync("href"), out var target) &&
                    IsIncomingUrl(target, origin) &&
                    await link.EvaluateAsync<bool>(@"node => !node.hasAttribute('disabled') &&
                        node.getAttribute('aria-disabled') !== 'true' && !node.closest('.disabled')"))
                {
                    var clicked = await browser.Click(link);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (clicked.IsSuccess)
                    {
                        await page.Locator("#content.buildRallyPoint .rallyPointOverviewContainer")
                            .WaitForAsync(new() { Timeout = 5000 });
                        if (await IsExpectedPage(page, origin, villageId)) return Result.Ok();
                    }
                    browser.Logger.Warning("Incoming Troops link did not open the expected village/filter; using URL fallback");
                }
                else browser.Logger.Warning("Incoming Troops link missing, ambiguous or unsuitable; using URL fallback");
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                browser.Logger.Warning(ex, "Incoming Troops link failed; using URL fallback");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var fallback = new UriBuilder(origin)
            {
                Path = "/building",
                Query = $"id=16&tt=1&filter=1&subfilters=1&newdid={villageId.Value}",
                Fragment = "",
            };
            var result = await browser.Navigate(fallback.Uri.AbsoluteUri);
            if (result.IsFailed) return result;
            result = await browser.Wait(page.Locator("#content.buildRallyPoint .rallyPointOverviewContainer"));
            if (result.IsFailed) return result;
            cancellationToken.ThrowIfCancellationRequested();
            return await IsExpectedPage(page, origin, villageId)
                ? Result.Ok() : Result.Fail("Incoming overview village/filter was not confirmed after navigation.");
        }

        private static bool IsIncomingUrl(Uri url, Uri origin) =>
            url.Scheme == origin.Scheme && url.Authority == origin.Authority &&
            url.AbsolutePath == "/building" && HeroOasisParser.Query(url, "id") == "16" &&
            HeroOasisParser.Query(url, "tt") == "1" && HeroOasisParser.Query(url, "filter") == "1" &&
            HeroOasisParser.Query(url, "subfilters") == "1";

        private static async Task<bool> IsExpectedPage(IPage page, Uri origin, VillageId villageId)
        {
            if (!Uri.TryCreate(page.Url, UriKind.Absolute, out var url) || !IsIncomingUrl(url, origin))
                return false;
            var active = page.Locator("#sidebarBoxVillageList div.listEntry.village.active");
            return await active.CountAsync() == 1 &&
                await active.GetAttributeAsync("data-did") == villageId.Value.ToString();
        }
    }
}
