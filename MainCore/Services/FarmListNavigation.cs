namespace MainCore.Services
{
    public static class FarmListNavigation
    {
        public static async Task<Result> Open(
            IChromeBrowser browser, string fallbackUrl, VillageId? owner = null, bool preferRallyPointTab = false)
        {
            var page = browser.CurrentPage;
            try
            {
                var currentUri = new Uri(page.Url);
                var button = page.Locator(
                    "#sidebarBoxVillageList .quickLinks a[data-load-tooltip='quickLink']:has(svg.RallyPointFarmList)");
                var source = "shortcut";
                if (preferRallyPointTab &&
                    await page.Locator("#content.buildRallyPoint").CountAsync() == 1)
                {
                    var tabs = page.Locator("#content.buildRallyPoint .contentNavi.subNavi a.tabItem[href]");
                    var matches = new List<ILocator>();
                    for (var i = 0; i < await tabs.CountAsync(); i++)
                    {
                        var tab = tabs.Nth(i);
                        var href = await tab.GetAttributeAsync("href");
                        if (Uri.TryCreate(currentUri, href, out var url) &&
                            url.Scheme == currentUri.Scheme && url.Authority == currentUri.Authority &&
                            url.AbsolutePath == "/build.php" && HeroOasisParser.Query(url, "tt") == "99")
                            matches.Add(tab);
                    }
                    if (matches.Count == 1)
                    {
                        button = matches[0];
                        source = "Rally Point tab";
                    }
                    else
                        browser.Logger.Warning("Farm list Rally Point tab missing or ambiguous ({Count}); trying shortcut", matches.Count);
                }
                var count = await button.CountAsync();
                if (count == 1)
                {
                    var clicked = await browser.Click(button);
                    if (clicked.IsSuccess)
                    {
                        await page.WaitForURLAsync(url =>
                            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                            uri.Scheme == currentUri.Scheme && uri.Authority == currentUri.Authority &&
                            uri.AbsolutePath == "/build.php" &&
                            HeroOasisParser.Query(uri, "tt") == "99", new() { Timeout = 5000 });
                        await page.Locator("#rallyPointFarmList").WaitForAsync(new() { Timeout = 5000 });
                        if (owner is null || await HasExpectedVillage(page, owner.Value))
                            return Result.Ok();
                        browser.Logger.Warning("Farm list {Source} opened the wrong village; falling back to URL", source);
                    }
                    else
                    {
                        browser.Logger.Warning("Farm list {Source} click failed: {Errors}; falling back to URL", source,
                            string.Join("; ", clicked.Errors.Select(error => error.Message)));
                    }
                }
                else
                {
                    browser.Logger.Warning("Farm list {Source} missing or ambiguous ({Count}); falling back to URL", source, count);
                }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                browser.Logger.Warning("Farm list navigation click failed ({ErrorType}); falling back to URL", ex.GetType().Name);
            }

            var result = await browser.Navigate(fallbackUrl);
            if (result.IsFailed) return result;
            result = await browser.Wait(page.Locator("#rallyPointFarmList"));
            if (result.IsFailed) return result;
            if (owner is not null && !await HasExpectedVillage(page, owner.Value))
                return Result.Fail("Farm list owner village was not confirmed after URL fallback.");
            return Result.Ok();
        }

        private static async Task<bool> HasExpectedVillage(IPage page, VillageId owner)
        {
            var active = page.Locator("#sidebarBoxVillageList .listEntry.village.active");
            return await active.CountAsync() == 1 &&
                await active.GetAttributeAsync("data-did") == owner.Value.ToString();
        }
    }
}
