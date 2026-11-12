using System.Text.Json;

namespace MainCore.Parsers
{
    public sealed record FarmTargetSlot(string SlotId, string Href, string Name, bool Disabled);
    public sealed record OasisAnimalTroop(int UnitId, string Name, long Count);
    public sealed record RallyPointDraft(
        long AvailableSteppeRiders,
        long AvailableMarksmen,
        long AvailableMarauders,
        string SendTroopsUrl);

    public static class FarmTargetTroopsParser
    {
        public static readonly IReadOnlyDictionary<int, string> OasisAnimalNames = new Dictionary<int, string>
        {
            { 31, "Rat" },
            { 32, "Spider" },
            { 33, "Snake" },
            { 34, "Bat" },
            { 35, "Wild Boar" },
            { 36, "Wolf" },
            { 37, "Bear" },
            { 38, "Crocodile" },
            { 39, "Tiger" },
            { 40, "Elephant" },
        };

        public static ILocator GetList(IPage page, FarmId farmId) =>
            page.Locator($"#rallyPointFarmList .farmListWrapper:has(.farmListHeader [data-list='{farmId.Value}'])");

        public static string BuildRallyPointTabUrl(string pageUrl, int tabIndex)
        {
            var uri = new UriBuilder(pageUrl);
            var query = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : "", StringComparer.Ordinal);
            query["tt"] = tabIndex.ToString();
            uri.Query = string.Join("&", query.Select(kv => $"{kv.Key}={kv.Value}"));
            return uri.Uri.AbsoluteUri;
        }

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
                if (node.classList.contains('collapsed'))
                    return null;
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

        public static async Task<Result<List<OasisAnimalTroop>>> ReadOasisAnimalTroops(IPage page, string targetHref)
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
                return troops.map(row => ({
                    UnitClass: row.querySelector('img.unit')?.getAttribute('class') ?? '',
                    Count: clean(row.querySelector('td.val')?.textContent)
                }));
            }", targetHref);
            if (snapshot.ValueKind != JsonValueKind.Array)
                return Result.Fail("Matching oasis troop table is missing, unknown, or ambiguous.");
            var troops = new List<OasisAnimalTroop>();
            foreach (var value in snapshot.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object)
                    return Result.Fail("Oasis troop row is invalid.");
                var unitClass = value.GetProperty("UnitClass").GetString() ?? "";
                var countText = value.GetProperty("Count").GetString() ?? "";
                var match = System.Text.RegularExpressions.Regex.Match(unitClass, @"\bu(?<id>\d+)\b");
                if (!match.Success || !int.TryParse(match.Groups["id"].Value, out var unitId) ||
                    !OasisAnimalNames.TryGetValue(unitId, out var name))
                    return Result.Fail("Oasis troop type is invalid or unknown.");
                if (string.IsNullOrEmpty(countText) || !countText.All(char.IsAsciiDigit) ||
                    !long.TryParse(countText, out var count))
                    return Result.Fail("Oasis troop count is invalid or unknown.");
                troops.Add(new OasisAnimalTroop(unitId, name, count));
            }
            return Result.Ok(troops);
        }

        public static async Task<Result<RallyPointDraft>> ReadSteppeRiderDraft(IPage page)
        {
            var form = page.Locator("#build .a2b form[action*='tt=2']");
            if (await form.CountAsync() != 1)
                return Result.Fail("Send troops form not found in Rally Point.");
            var snapshot = await page.EvaluateAsync<JsonElement>(@"() => {
                const form = document.querySelector('#build .a2b form[action*=""tt=2""]');
                if (!form) return null;
                const steppeCell = Array.from(form.querySelectorAll('table#troops td'))
                    .find(td => td.querySelector('img.unit.u64'));
                const marksmanCell = Array.from(form.querySelectorAll('table#troops td'))
                    .find(td => td.querySelector('img.unit.u65'));
                const marauderCell = Array.from(form.querySelectorAll('table#troops td'))
                    .find(td => td.querySelector('img.unit.u66'));
                if (!steppeCell || !marksmanCell || !marauderCell) return null;
                const steppeInput = steppeCell.querySelector('input[name=""troop[t4]""]');
                const steppeAvailable = steppeCell.querySelector('a, span');
                const marksmanInput = marksmanCell.querySelector('input[name=""troop[t5]""]');
                const marksmanAvailable = marksmanCell.querySelector('a, span');
                const marauderInput = marauderCell.querySelector('input[name=""troop[t6]""]');
                const marauderAvailable = marauderCell.querySelector('a, span');
                if (!steppeInput || !steppeAvailable || !marksmanInput || !marksmanAvailable ||
                    !marauderInput || !marauderAvailable) return null;
                const action = form.getAttribute('action') || '';
                return {
                    steppeText: (steppeAvailable.textContent || '').trim(),
                    marksmanText: (marksmanAvailable.textContent || '').trim(),
                    marauderText: (marauderAvailable.textContent || '').trim(),
                    action,
                };
            }");
            if (snapshot.ValueKind != JsonValueKind.Object)
                return Result.Fail("Steppe Rider, Marksman, or Marauder draft row not found.");
            var steppeText = snapshot.GetProperty("steppeText").GetString() ?? "";
            var marksmanText = snapshot.GetProperty("marksmanText").GetString() ?? "";
            var marauderText = snapshot.GetProperty("marauderText").GetString() ?? "";
            if (!TryParseTroopCount(steppeText, out var steppe))
                return Result.Fail("Steppe Rider available amount is invalid.");
            if (!TryParseTroopCount(marksmanText, out var marksman))
                return Result.Fail("Marksman available amount is invalid.");
            if (!TryParseTroopCount(marauderText, out var marauder))
                return Result.Fail("Marauder available amount is invalid.");
            var action = snapshot.GetProperty("action").GetString() ?? "";
            var sendTroopsUrl = string.IsNullOrWhiteSpace(action)
                ? page.Url
                : new Uri(new Uri(page.Url), action).AbsoluteUri;
            return Result.Ok(new RallyPointDraft(steppe, marksman, marauder, sendTroopsUrl));
        }

        private static bool TryParseTroopCount(string text, out long count)
        {
            var cleaned = new string(text.Where(c =>
                !char.IsWhiteSpace(c) && c is not '\u200e' and not '\u200f' &&
                (c < '\u202a' || c > '\u202e') && (c < '\u2066' || c > '\u2069')).ToArray());
            return long.TryParse(cleaned, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out count);
        }

        public static async Task<Result<bool>> HasTroops(IPage page, string targetHref)
        {
            var troops = await ReadOasisAnimalTroops(page, targetHref);
            if (troops.IsFailed) return Result.Fail(troops.Errors);
            return Result.Ok(troops.Value.Any(x => x.Count > 0));
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
