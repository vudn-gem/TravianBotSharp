namespace MainCore.Services
{
    public static class RallyPointSendTroopsTab
    {
        public static ILocator SendForm(IPage page) =>
            page.Locator("#build .a2b form[action*='tt=2']:has(#troops)");

        public static async Task<bool> HasVillage(IPage page, VillageId villageId)
        {
            var active = page.Locator("#sidebarBoxVillageList .listEntry.village.active");
            return await active.CountAsync() == 1 &&
                await active.GetAttributeAsync("data-did") == villageId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static async Task<Result> Submit(IPage page)
        {
            var button = SendForm(page).Locator("button[type='submit']");
            if (await button.CountAsync() != 1 || !await button.IsEnabledAsync())
                return Result.Fail("The Rally Point troop form submit button is missing, ambiguous or disabled.");
            await button.ClickAsync();
            await page.Locator("#troopSendForm #confirmSendTroops").WaitForAsync();
            return Result.Ok();
        }

        public static async Task<Result> Run(
            IChromeBrowser browser, VillageId villageId, Func<IPage, Task<Result>> action,
            CancellationToken cancellationToken)
        {
            var mainPage = browser.CurrentPage;
            var origin = new Uri(mainPage.Url);
            Result result = Result.Fail("Rally Point send was not processed.");
            Result closed = Result.Ok();
            try
            {
                var opened = await Open(browser, villageId, origin, cancellationToken);
                if (opened.IsFailed) result = opened;
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    result = await action(browser.CurrentPage);
                }
            }
            finally
            {
                if (browser.CurrentPage != mainPage)
                {
                    closed = await browser.CloseNewTab();
                    if (closed.IsFailed)
                        browser.Logger.Error("Could not close the Rally Point send tab: {Errors}",
                            string.Join(Environment.NewLine, closed.Errors.Select(x => x.Message)));
                }
            }
            return closed.IsFailed ? Result.Fail(result.Errors.Concat(closed.Errors)) : result;
        }

        private static async Task<Result> Open(
            IChromeBrowser browser, VillageId villageId, Uri origin, CancellationToken cancellationToken)
        {
            var mainPage = browser.CurrentPage;
            var shortcut = mainPage.Locator(
                "#sidebarBoxVillageList .quickLinks a[data-load-tooltip='quickLink']:has(svg.RallyPointSendTroops)");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await shortcut.CountAsync() == 1 &&
                    Uri.TryCreate(origin, await shortcut.GetAttributeAsync("href"), out var target) &&
                    target.Scheme == origin.Scheme && target.Authority == origin.Authority &&
                    await shortcut.EvaluateAsync<bool>(@"node => !node.hasAttribute('disabled') &&
                        node.getAttribute('aria-disabled') !== 'true' && !node.closest('.disabled')"))
                {
                    var clicked = await browser.Click(shortcut, openInNewTab: true);
                    if (clicked.IsSuccess && browser.CurrentPage != mainPage)
                    {
                        await SendForm(browser.CurrentPage).WaitForAsync(new() { Timeout = 5000 });
                        if (await IsCorrectPage(browser.CurrentPage, origin, villageId)) return Result.Ok();
                    }
                }
                browser.Logger.Warning("RallyPointSendTroops shortcut unavailable or incorrect; using village URL fallback in a new tab");
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                browser.Logger.Warning(ex, "RallyPointSendTroops shortcut failed; using URL fallback in a new tab");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (browser.CurrentPage == mainPage)
            {
                var opened = await browser.OpenNewTab();
                if (opened.IsFailed) return opened;
                if (browser.CurrentPage == mainPage)
                    return Result.Fail("Rally Point send did not open a separate tab.");
            }
            var fallback = new UriBuilder(origin)
            {
                Path = "/building", Query = "id=16&tt=2", Fragment = "",
            };
            var result = await browser.Navigate(fallback.Uri.AbsoluteUri);
            if (result.IsFailed) return result;
            result = await browser.Wait(SendForm(browser.CurrentPage));
            if (result.IsFailed) return result;
            return await IsCorrectPage(browser.CurrentPage, origin, villageId)
                ? Result.Ok() : Result.Fail("Rally Point Send troops opened the wrong page or village.");
        }

        private static async Task<bool> IsCorrectPage(IPage page, Uri origin, VillageId villageId) =>
            Uri.TryCreate(page.Url, UriKind.Absolute, out var current) &&
            current.Scheme == origin.Scheme && current.Authority == origin.Authority &&
            HeroOasisParser.Query(current, "tt") == "2" &&
            await HasVillage(page, villageId);
    }
}
