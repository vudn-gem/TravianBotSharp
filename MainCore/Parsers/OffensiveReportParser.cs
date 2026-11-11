using System.Text.Json;

namespace MainCore.Parsers
{
    public sealed record UnreadLostReport(string Id, string Href);

    public static class OffensiveReportParser
    {
        public static async Task<Result<List<UnreadLostReport>>> ReadUnreadLostReports(IPage page)
        {
            var snapshot = await page.EvaluateAsync<JsonElement>(@"() => {
                const tables = Array.from(document.querySelectorAll('table'))
                    .filter(table => table.querySelector('input.report[name=""ids[]""]'));
                if (tables.length !== 1) return null;
                return Array.from(tables[0].querySelectorAll('tr'))
                    .filter(row => row.querySelector('td.sub.newMessage .messageStatusUnread') &&
                        row.querySelector('td.sub .iReport3'))
                    .map(row => ({
                        Id: row.querySelector('input.report[name=""ids[]""]')?.value,
                        Href: row.querySelector('td.sub div a[href*=""id=""]')?.getAttribute('href')
                    }));
            }");
            if (snapshot.ValueKind != JsonValueKind.Array)
                return Result.Fail("Offensive report table is missing or ambiguous.");
            var reports = snapshot.Deserialize<List<UnreadLostReport>>();
            if (reports is null || reports.Any(report =>
                    string.IsNullOrWhiteSpace(report.Id) || !report.Id.All(char.IsAsciiDigit) ||
                    string.IsNullOrWhiteSpace(report.Href)) ||
                reports.Select(report => report.Id).Distinct().Count() != reports.Count)
                return Result.Fail("Unread lost reports contain invalid or duplicate entries.");
            return Result.Ok(reports);
        }

        public static async Task<Result<VillageId>> ReadAttackerVillage(IPage page)
        {
            var id = await page.EvaluateAsync<int>(@"() => {
                const attackers = document.querySelectorAll('.body .role.attacker');
                if (attackers.length !== 1 || !attackers[0].querySelector('.outcome .attackLost')) return 0;
                const links = attackers[0].querySelectorAll('.troopHeadline a.village');
                if (links.length !== 1) return 0;
                const clean = text => (text || '').replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '').trim();
                const name = clean(links[0].textContent);
                if (!name) return 0;
                const villages = Array.from(document.querySelectorAll('#sidebarBoxVillageList .listEntry.village[data-did]'))
                    .filter(row => clean(row.querySelector('.name')?.textContent) === name);
                return villages.length === 1 ? Number(villages[0].getAttribute('data-did')) : 0;
            }");
            return id > 0 ? Result.Ok(new VillageId(id)) :
                Result.Fail("Lost report attacker village is missing, not owned, or ambiguous.");
        }

        public static async Task<bool> HasAvailableRaidListButton(IPage page)
        {
            var button = page.Locator("button:has(i.reportButton.raidList)");
            return await button.CountAsync() == 1 && await button.IsVisibleAsync() &&
                await button.IsEnabledAsync() &&
                !await button.EvaluateAsync<bool>("node => node.classList.contains('disabled')");
        }

        public static async Task<Result<string>> ReadSelectedFarmList(IPage page)
        {
            var form = page.Locator("#farmListTargetForm");
            if (await form.CountAsync() != 1 || !await form.IsVisibleAsync())
                return Result.Fail("Edit target dialog is missing or ambiguous.");
            if (!await form.EvaluateAsync<bool>(
                "node => node.closest('.contentV2')?.querySelector('h3')?.textContent.trim() === 'Edit target'"))
                return Result.Fail("Expected Edit target dialog; refusing to create a new target.");
            var list = form.Locator("select[name='listId']");
            var checkbox = form.Locator(".deactivateSlot input[type='checkbox'][name='isActive']");
            if (await list.CountAsync() != 1 || await checkbox.CountAsync() != 1)
                return Result.Fail("Edit target controls are missing or ambiguous.");
            var selected = await list.InputValueAsync();
            return !string.IsNullOrEmpty(selected) && selected.All(char.IsAsciiDigit) ?
                Result.Ok(selected) : Result.Fail("No farm list selected; refusing to add or guess a target.");
        }
    }
}
