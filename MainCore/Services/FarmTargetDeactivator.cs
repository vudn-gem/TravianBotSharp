namespace MainCore.Services
{
    public static class FarmTargetDeactivator
    {
        public static async Task<Result<bool>> Deactivate(
            IChromeBrowser browser, FarmId farmId, FarmTargetSlot target, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1)
                return Result.Fail("Target slot is missing or ambiguous.");
            if (await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')"))
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
            if (await openMenu.CountAsync() < 1)
                return Result.Fail("Unable to find target context menu trigger.");
            var result = await browser.Click(openMenu.First);
            if (result.IsFailed) return Result.Fail(result.Errors);

            ILocator deactivate = row.Locator("td.openContextMenu .contextMenuWrapper .contextMenu button.entry.deactivate");
            if (await deactivate.CountAsync() != 1)
                deactivate = page.Locator(".contextMenuWrapper .contextMenu button.entry.deactivate");
            if (await deactivate.CountAsync() != 1)
                deactivate = row.Locator(".menu [data-action='deactivate'], .menu button[data-action='deactivate']");
            if (await deactivate.CountAsync() != 1)
                return Result.Fail("Unable to find target deactivation menu action.");
            result = await browser.Wait(deactivate);
            if (result.IsFailed) return Result.Fail(result.Errors);
            cancellationToken.ThrowIfCancellationRequested();
            result = await browser.Click(deactivate);
            if (result.IsFailed) return Result.Fail(result.Errors);
            return Result.Ok(true);
        }

        public static async Task<bool> IsConfirmed(IPage page, FarmId farmId, FarmTargetSlot target)
        {
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            return await row.CountAsync() == 1 &&
                await row.Locator("td.target a").GetAttributeAsync("href") == target.Href &&
                await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')");
        }
    }
}
