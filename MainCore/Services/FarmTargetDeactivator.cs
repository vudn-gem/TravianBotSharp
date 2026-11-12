namespace MainCore.Services
{
    public static class FarmTargetDeactivator
    {
        public static async Task<Result<bool>> Deactivate(
            IChromeBrowser browser, FarmId farmId, FarmTargetSlot target, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var edit = await FarmTargetTroopsParser.FindEditLink(page, farmId, target.SlotId);
            if (edit.IsFailed) return Result.Fail(edit.Errors);
            var result = await browser.Click(edit.Value);
            if (result.IsFailed) return Result.Fail(result.Errors);
            result = await browser.Wait(page.Locator("#farmListTargetForm"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            var selected = await OffensiveReportParser.ReadSelectedFarmList(page);
            if (selected.IsFailed) return Result.Fail(selected.Errors);
            if (selected.Value != farmId.Value.ToString())
                return Result.Fail("Edit dialog selected a different farm list.");
            var matches = await page.Locator("#farmListTargetForm").EvaluateAsync<bool>(@"(form, href) => {
                const url = new URL(href, location.href);
                return form.querySelector('input[name=""x""]')?.value === url.searchParams.get('x') &&
                    form.querySelector('input[name=""y""]')?.value === url.searchParams.get('y');
            }", target.Href);
            if (!matches) return Result.Fail("Edit dialog target coordinates do not match.");
            cancellationToken.ThrowIfCancellationRequested();
            var checkbox = page.Locator("#farmListTargetForm .deactivateSlot input[name='isActive']");
            if (await checkbox.IsCheckedAsync()) return Result.Ok(false);
            await checkbox.CheckAsync(new LocatorCheckOptions { Timeout = 3000 });
            result = await browser.Click(page.Locator("#farmListTargetForm .actionButtons button.save[type='submit']"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            await page.Locator("#farmListTargetForm").WaitForAsync(new LocatorWaitForOptions
                { State = WaitForSelectorState.Hidden, Timeout = 5000 });
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
