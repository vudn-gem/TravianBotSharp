namespace MainCore.Services
{
    public static class FarmListPreparation
    {
        public static async Task<Result> Prepare(IChromeBrowser browser, FarmId farmId, VillageId owner)
        {
            var page = browser.CurrentPage;
            if (await VillagePanelParser.GetCurrentVillageId(page) != owner)
                return Result.Fail("Farm list owner village switch was not confirmed.");
            var resolved = await FarmTargetTroopsParser.ReadOwnerVillage(page, farmId);
            if (resolved.IsFailed) return Result.Fail(resolved.Errors);
            if (resolved.Value != owner) return Result.Fail("Farm list owner changed during scan.");
            var list = FarmTargetTroopsParser.GetList(page, farmId);
            var header = page.Locator(
                $"#rallyPointFarmList .villageWrapper:has(.farmListHeader [data-list='{farmId.Value}']) > .villageHeader");
            if (await header.CountAsync() != 1) return Result.Fail("Farm list village header is missing or ambiguous.");
            if (await header.EvaluateAsync<bool>("node => node.classList.contains('collapsed')"))
            {
                var expanded = await browser.Click(header.Locator("a.expandCollapse"));
                if (expanded.IsFailed) return expanded;
                await page.WaitForFunctionAsync("""
                    id => {
                        const list = document.querySelector(`#rallyPointFarmList .farmListWrapper:has(.farmListHeader [data-list='${id}'])`);
                        const header = list?.closest('.villageWrapper')?.querySelector(':scope > .villageHeader');
                        return header?.classList.contains('expanded') && !header.classList.contains('collapsed');
                    }
                    """, farmId.Value);
            }
            if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
            {
                var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                if (expanded.IsFailed) return expanded;
            }
            await page.WaitForFunctionAsync("""
                id => {
                    const lists = document.querySelectorAll(`#rallyPointFarmList .farmListWrapper:has(.farmListHeader [data-list='${id}'])`);
                    if (lists.length !== 1) return false;
                    const list = lists[0];
                    const header = list.closest('.villageWrapper')?.querySelector(':scope > .villageHeader');
                    if (!header || header.classList.contains('collapsed')) return false;
                    if (!list.classList.contains('expanded') || list.classList.contains('collapsed')) return false;
                    const table = list.querySelector('table.slots');
                    if (!table) return false;
                    const counter = table.querySelector('td.addTarget');
                    if (!counter) return table.querySelectorAll('tbody tr.slot').length > 0;
                    const text = counter.textContent.replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '');
                    const match = text.match(/(\d+)\s*\/\s*\d+\s*$/);
                    return !!match && table.querySelectorAll('tbody tr.slot').length === Number(match[1]);
                }
                """, farmId.Value);
            return Result.Ok();
        }
    }
}
