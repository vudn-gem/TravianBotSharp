using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public sealed record EvasionIncoming(long MovementId, bool Raid, DateTime Arrival);

    public static class EvasionIncomingParser
    {
        private sealed record Row(string Id, bool Attack, bool Raid, string Remaining);
        private sealed record Snapshot(bool Paginated, List<Row> Rows);

        public static async Task<Result<List<EvasionIncoming>>> Read(IPage page, DateTime observedAt)
        {
            var json = await page.EvaluateAsync<JsonElement>(@"() => {
                const roots = document.querySelectorAll('#content.buildRallyPoint .rallyPointOverviewContainer');
                if (roots.length !== 1) return null;
                const root = roots[0];
                return {
                    Paginated: Array.from(root.querySelectorAll('.paginator .next'))
                        .some(node => !node.classList.contains('disabled')),
                    Rows: Array.from(root.querySelectorAll('table.troop_details.inAttack, table.troop_details.inRaid'))
                        .map(table => {
                            const ids = table.querySelectorAll('thead img.markAttack[id^=""markSymbol_""]');
                            const timers = table.querySelectorAll('tbody.infos .in span.timer[counting=""down""]');
                            return {
                                Id: ids.length === 1 ? ids[0].id.replace('markSymbol_', '') : '',
                                Attack: table.classList.contains('inAttack'),
                                Raid: table.classList.contains('inRaid'),
                                Remaining: timers.length === 1 ? timers[0].textContent.trim() : ''
                            };
                        })
                };
            }");
            if (json.ValueKind != JsonValueKind.Object)
                return Result.Fail("Incoming Rally Point overview is missing or ambiguous.");
            var snapshot = json.Deserialize<Snapshot>();
            if (snapshot?.Rows is null) return Result.Fail("Incoming overview could not be read.");
            if (snapshot.Paginated)
                return Result.Fail("Incoming overview has additional pages; evasion planning requires all incoming movements.");

            var movements = new List<EvasionIncoming>();
            var ids = new HashSet<long>();
            foreach (var row in snapshot.Rows)
            {
                var duration = ParseRemaining(row.Remaining);
                if (!long.TryParse(row.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                    id <= 0 || !ids.Add(id) || row.Attack == row.Raid || duration.IsFailed)
                    return Result.Fail("Incoming movement has an invalid/duplicate ID, type or arrival countdown.");
                if (duration.Value.TotalSeconds > (DateTime.MaxValue - observedAt).TotalSeconds)
                    return Result.Fail("Incoming arrival is outside the supported date range.");
                movements.Add(new(id, row.Raid, observedAt.Add(duration.Value)));
            }
            return Result.Ok(movements.OrderBy(x => x.Arrival).ToList());
        }

        public static Result<TimeSpan> ParseRemaining(string text)
        {
            var match = Regex.Match(text, @"\A([0-9]+):([0-5][0-9]):([0-5][0-9])\z", RegexOptions.CultureInvariant);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours))
                return Result.Fail("Arrival countdown must be hours:minutes:seconds.");
            var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
            var totalSeconds = (long)hours * 3600 + minutes * 60 + seconds;
            if (totalSeconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
                return Result.Fail("Arrival countdown is outside the supported duration range.");
            return Result.Ok(TimeSpan.FromSeconds(totalSeconds));
        }
    }
}
