namespace MainCore.Services
{
    public static class FarmTargetDeactivator
    {
        public static async Task<Result<bool>> DeactivateFromFarmList(
            IChromeBrowser browser, FarmId farmId, FarmTargetSlot target, CancellationToken cancellationToken)
            => await UpdateFromFarmList(browser, farmId, target, false, cancellationToken);

        public static async Task<Result<bool>> ActivateFromFarmList(
            IChromeBrowser browser, FarmId farmId, FarmTargetSlot target, CancellationToken cancellationToken)
            => await UpdateFromFarmList(browser, farmId, target, true, cancellationToken);

        private static async Task<Result<bool>> UpdateFromFarmList(
            IChromeBrowser browser, FarmId farmId, FarmTargetSlot target, bool activate,
            CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1)
                return Result.Fail("Target slot is missing or ambiguous.");
            var isDisabled = await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')");
            if (isDisabled != activate)
                return Result.Ok(false);
            var matches = await row.EvaluateAsync<bool>(@"(node, href) => {
                const url = new URL(href, location.href);
                const target = node.querySelector('td.target a');
                if (!target) return false;
                const targetUrl = new URL(target.getAttribute('href'), location.href);
                return targetUrl.searchParams.get('x') === url.searchParams.get('x') &&
                    targetUrl.searchParams.get('y') === url.searchParams.get('y');
            }", target.Href);
            if (!matches) return Result.Fail("Farm target coordinates do not match.");
            cancellationToken.ThrowIfCancellationRequested();

            ILocator openMenu = row.Locator("td.openContextMenu > a");
            if (await openMenu.CountAsync() != 1)
                openMenu = row.Locator("td.openContextMenu > button, .actions .menuButton, .actions > button, .actions > a");
            if (await openMenu.CountAsync() != 1)
                return Result.Fail("Target context menu trigger is missing or ambiguous.");
            var result = await browser.Click(openMenu);
            if (result.IsFailed) return Result.Fail(result.Errors);

            var action = activate ? "activate" : "deactivate";
            ILocator menuAction = row.Locator($"td.openContextMenu .contextMenuWrapper .contextMenu button.entry.{action}");
            if (await menuAction.CountAsync() != 1)
                menuAction = page.Locator($".contextMenuWrapper .contextMenu button.entry.{action}");
            if (await menuAction.CountAsync() != 1)
                menuAction = row.Locator($".menu [data-action='{action}'], .menu button[data-action='{action}']");
            if (await menuAction.CountAsync() != 1)
                return Result.Fail($"Unable to find target {action} menu action.");
            result = await browser.Wait(menuAction);
            if (result.IsFailed) return Result.Fail(result.Errors);
            cancellationToken.ThrowIfCancellationRequested();
            result = await browser.Click(menuAction);
            if (result.IsFailed) return Result.Fail(result.Errors);
            return Result.Ok(true);
        }

        public static async Task<bool> IsConfirmed(
            IPage page, FarmId farmId, FarmTargetSlot target, bool activate = false)
        {
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1 ||
                await row.Locator("td.target a").GetAttributeAsync("href") != target.Href)
                return false;
            var isDisabled = await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')");
            return isDisabled == !activate;
        }
    }
}
