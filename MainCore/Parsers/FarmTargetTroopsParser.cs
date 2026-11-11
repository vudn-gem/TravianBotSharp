using System.Text.Json;

namespace MainCore.Parsers
{
    public sealed record FarmTargetSlot(string SlotId, string Href, string Name, bool Disabled);

    public static class FarmTargetTroopsParser
    {
        public static ILocator GetList(IPage page, FarmId farmId) =>
            page.Locator($"#rallyPointFarmList .farmListWrapper:has(.farmListHeader [data-list='{farmId.Value}'])");

        public static async Task<Result<VillageId>> ReadOwnerVillage(IPage page, FarmId farmId)
        {
            var id = await page.EvaluateAsync<int>(@"listId => {
                const lists = document.querySelectorAll(
                    `#rallyPointFarmList .farmListWrapper:has(.farmListHeader [data-list='${listId}'])`);
                if (lists.length !== 1) return 0;
                const headers = lists[0].closest('.villageWrapper')?.querySelectorAll(':scope > .villageHeader .villageName');
                if (!headers || headers.length !== 1) return 0;
                const clean = text => (text || '').replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '').trim();
                const name = clean(headers[0].textContent);
                if (!name) return 0;
                const villages = Array.from(document.querySelectorAll('#sidebarBoxVillageList .listEntry.village[data-did]'))
                    .filter(row => clean(row.querySelector('.name')?.textContent) === name);
                const id = villages.length === 1 ? Number(villages[0].getAttribute('data-did')) : 0;
                return Number.isSafeInteger(id) && id > 0 ? id : 0;
            }", farmId.Value);
            return id > 0 ? Result.Ok(new VillageId(id)) :
                Result.Fail("Farm list owner village is missing, not owned, or ambiguous.");
        }

        public static async Task<Result<List<FarmTargetSlot>>> ReadTargets(IPage page, FarmId farmId)
        {
            var list = GetList(page, farmId);
            if (await list.CountAsync() != 1)
                return Result.Fail("Selected farm list is missing or ambiguous.");
            var snapshot = await list.EvaluateAsync<JsonElement>(@"node => {
                const table = node.querySelector('table.slots');
                if (!table) return null;
                return Array.from(table.querySelectorAll('tbody tr.slot')).map(row => ({
                    SlotId: row.querySelector('input[name=""selectOne""]')?.getAttribute('data-slot-id'),
                    Href: row.querySelector('td.target a')?.getAttribute('href'),
                    Name: row.querySelector('td.target a')?.textContent.trim(),
                    Disabled: row.classList.contains('disabled')
                }));
            }");
            if (snapshot.ValueKind != JsonValueKind.Array)
                return Result.Fail("Farm target table is unavailable.");
            var targets = snapshot.Deserialize<List<FarmTargetSlot>>();
            if (targets is null || targets.Any(x => string.IsNullOrWhiteSpace(x.SlotId) ||
                    !x.SlotId.All(char.IsAsciiDigit) || string.IsNullOrWhiteSpace(x.Href)) ||
                targets.Select(x => x.SlotId).Distinct().Count() != targets.Count)
                return Result.Fail("Farm targets contain invalid or duplicate slots.");
            return Result.Ok(targets);
        }

        public static async Task<Result<bool>> HasTroops(IPage page, string targetHref)
        {
            var snapshot = await page.EvaluateAsync<JsonElement>(@"href => {
                const tiles = document.querySelectorAll('#dialogContent #tileDetails.oasis');
                if (tiles.length !== 1) return null;
                const tile = tiles[0], expected = new URL(href, location.href);
                const center = tile.querySelector('a[data-centermap]');
                if (!center) return null;
                const actual = new URL(center.getAttribute('href'), location.href);
                if (actual.searchParams.get('x') !== expected.searchParams.get('x') ||
                    actual.searchParams.get('y') !== expected.searchParams.get('y')) return null;
                const tables = tile.querySelectorAll('#map_details > table[id=""troop_info""]:not(.rep)');
                if (tables.length !== 1) return null;
                const clean = text => (text || '').replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '').trim();
                const rows = Array.from(tables[0].querySelectorAll('tr'));
                const troops = rows.filter(row => row.querySelector('img.unit'));
                if (troops.length === 0)
                    return rows.some(row => clean(row.textContent).toLowerCase() === 'none') ? [] : null;
                return troops.map(row => clean(row.querySelector('td.val')?.textContent));
            }", targetHref);
            if (snapshot.ValueKind != JsonValueKind.Array)
                return Result.Fail("Matching oasis troop table is missing, unknown, or ambiguous.");
            var hasTroops = false;
            foreach (var value in snapshot.EnumerateArray())
            {
                var text = value.GetString();
                if (string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit) ||
                    !long.TryParse(text, out var count))
                    return Result.Fail("Oasis troop count is invalid or unknown.");
                hasTroops |= count > 0;
            }
            return Result.Ok(hasTroops);
        }

        public static async Task<Result<ILocator>> FindEditLink(IPage page, FarmId farmId, string slotId)
        {
            var links = page.Locator("#dialogContent #tileDetails .options a[onclick]")
                .Filter(new LocatorFilterOptions { HasText = "Edit farm list" });
            ILocator? found = null;
            for (var i = 0; i < await links.CountAsync(); i++)
            {
                var link = links.Nth(i);
                var script = await link.GetAttributeAsync("onclick") ?? "";
                if (!System.Text.RegularExpressions.Regex.IsMatch(script, $@"\blistId:\s*{farmId.Value}\s*,") ||
                    !System.Text.RegularExpressions.Regex.IsMatch(script, $@"\beditSlotIds:\s*\[\s*{slotId}\s*\]") ||
                    !script.Contains("SLOT_DIALOG_TYPE_EDIT", StringComparison.Ordinal)) continue;
                if (found is not null) return Result.Fail("Matching farm target edit link is ambiguous.");
                found = link;
            }
            return found is null ? Result.Fail("No edit link for the selected farm list and slot.") : Result.Ok(found);
        }
    }
}
