using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public sealed record IncomingVillageBadge(int Id, string Name, string Coordinates, bool Attack);
    public sealed record IncomingVillage(int Id, string Name, string Coordinates, int Attacks, int Raids);

    public static class IncomingTroopsParser
    {
        public static async Task<Result<List<IncomingVillageBadge>>> Read(IPage page)
        {
            var snapshot = await page.EvaluateAsync<JsonElement>(@"() => {
                const lists = document.querySelectorAll('.villageList');
                if (lists.length !== 1) return null;
                const clean = text => (text || '').replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '').trim();
                return Array.from(lists[0].querySelectorAll('.listEntry.village[data-did]')).map(row => ({
                    Id: Number(row.getAttribute('data-did')),
                    Name: clean(row.querySelector('.name')?.textContent),
                    Coordinates: clean(row.querySelector('.coordinatesGrid')?.textContent),
                    Attack: row.classList.contains('attack')
                }));
            }");
            if (snapshot.ValueKind != JsonValueKind.Array)
                return Result.Fail("Village list is missing or ambiguous.");

            var villages = snapshot.Deserialize<List<IncomingVillageBadge>>();
            if (villages is null || villages.Count == 0 ||
                villages.Any(x => x.Id <= 0 || string.IsNullOrWhiteSpace(x.Name)) ||
                villages.Select(x => x.Id).Distinct().Count() != villages.Count)
                return Result.Fail("Village list is empty or contains invalid or duplicate villages.");

            return Result.Ok(villages);
        }

        public static async Task<Result<IncomingVillage>> ReadCounts(
            IPage page, IncomingVillageBadge village, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!village.Attack)
                return Result.Ok(new IncomingVillage(village.Id, village.Name, village.Coordinates, 0, 0));

            var badge = page.Locator(
                $".villageList .listEntry.village[data-did='{village.Id}'] .incomingTroops[data-id='{village.Id}']");
            try
            {
                await page.Mouse.MoveAsync(0, 0);
                await badge.HoverAsync(new LocatorHoverOptions { Timeout = 3000 });
                cancellationToken.ThrowIfCancellationRequested();
                // Resolve only the tooltip belonging to this badge, never a global visible tooltip.
                await using var handle = await page.WaitForFunctionAsync(@"id => {
                    const badge = document.querySelector(
                        `.villageList .listEntry.village[data-did='${id}'] .incomingTroops[data-id='${id}']`);
                    if (!badge) return false;
                    const ids = (badge.getAttribute('aria-describedby') || '').split(/\s+/);
                    const texts = ids.map(id => document.getElementById(id))
                        .filter(root => root && root.querySelector('.tippy-box[data-state=""visible""]'))
                        .map(root => root.querySelector('.tippy-content .text'))
                        .filter(text => text && text.getClientRects().length > 0 && text.innerText.trim());
                    return texts.length === 1 ? texts[0].innerText : false;
                }", village.Id, new PageWaitForFunctionOptions { Timeout = 3000 });
                cancellationToken.ThrowIfCancellationRequested();
                var text = await handle.JsonValueAsync<string>();
                var counts = ParseCounts(text);
                if (counts.IsFailed) return Result.Fail(counts.Errors);
                return Result.Ok(new IncomingVillage(village.Id, village.Name, village.Coordinates,
                    counts.Value.Attacks, counts.Value.Raids));
            }
            finally
            {
                if (!page.IsClosed) await page.Mouse.MoveAsync(0, 0);
            }
        }

        public static Result<(int Attacks, int Raids)> ParseCounts(string text)
        {
            int attacks = 0, raids = 0;
            var found = new HashSet<string>();
            foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("Incoming attacks", StringComparison.Ordinal) &&
                    !trimmed.StartsWith("Incoming raids", StringComparison.Ordinal)) continue;
                var match = Regex.Match(trimmed, @"\AIncoming (attacks|raids):\s*([0-9]+)\z",
                    RegexOptions.CultureInvariant);
                if (!match.Success ||
                    !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                    !found.Add(match.Groups[1].Value))
                    return Result.Fail("Incoming tooltip contains invalid or duplicate attack/raid counts.");
                if (match.Groups[1].Value == "attacks") attacks = count;
                else raids = count;
            }
            if (found.Count == 0)
                return Result.Fail("Incoming tooltip has no supported attack/raid counts (English labels required).");
            return Result.Ok((attacks, raids));
        }
    }
}
