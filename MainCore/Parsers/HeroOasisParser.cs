using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public sealed record HeroSimulationResult(int HealthBefore, int HealthAfter, int SubTroopLost, bool HeroSurvives);

    public static class HeroOasisParser
    {
        public static async Task<Result<bool>> IsHeroHome(IPage page)
        {
            var cells = page.Locator("#build .a2b form[action*='tt=2'] #troops td:has(img.unit.uhero)");
            if (await cells.CountAsync() != 1)
                return Result.Fail("Hero availability cell is missing or ambiguous.");
            var input = cells.Locator("input[name='troop[t11]']");
            var available = cells.Locator("a, span.none");
            if (await input.CountAsync() != 1 || await available.CountAsync() != 1 ||
                !TryCount(await available.InnerTextAsync(), out var count) || count > 1)
                return Result.Fail("Hero availability cannot be read.");
            return Result.Ok(count == 1 && await input.IsEnabledAsync());
        }

        public static async Task<Result<string>> SimulatorUrl(IPage page)
        {
            var tile = page.Locator("#dialogContent #tileDetails.oasis");
            var link = tile.Locator("#map_details > table#troop_info:not(.rep) a[href*='screen=combatSimulator']");
            var raid = tile.Locator("a[href*='tt=2'][href*='targetMapId=']");
            if (await link.CountAsync() != 1 || await raid.CountAsync() != 1)
                return Result.Fail("Matching oasis simulator link is missing or ambiguous.");
            var url = new Uri(new Uri(page.Url), (await link.GetAttributeAsync("href"))!);
            var raidUrl = new Uri(new Uri(page.Url), (await raid.GetAttributeAsync("href"))!);
            if (url.Authority != new Uri(page.Url).Authority || url.Scheme != new Uri(page.Url).Scheme ||
                url.AbsolutePath != "/build.php" || Query(url, "tt") != "3" ||
                Query(url, "screen") != "combatSimulator" ||
                !int.TryParse(Query(url, "kid"), out var kid) || kid <= 0 ||
                Query(url, "kid") != Query(raidUrl, "targetMapId"))
                return Result.Fail("Simulator destination does not match the oasis.");
            return Result.Ok(url.AbsoluteUri);
        }

        public static async Task<Result<int>> ReadHealth(IPage page)
        {
            var health = page.Locator("#combatSimulatorForm .role.attacker .heroAttributes input[name='health']");
            if (await health.CountAsync() != 1 || !TryCount(await health.InputValueAsync(), out var value) || value > 100)
                return Result.Fail("Current hero health is missing or invalid.");
            return Result.Ok(value);
        }

        public static async Task<Result> FillSimulator(IPage page, HeroOasisSetting settings)
        {
            var attacker = page.Locator("#combatSimulatorForm .role.attacker");
            var icons = attacker.Locator(".troops.attack tr.troopIcons");
            if (await icons.Locator("i.u64").CountAsync() != 1 ||
                await icons.Locator("i.u65").CountAsync() != 1 ||
                await icons.Locator("i.u66").CountAsync() != 1)
                return Result.Fail("Hero clear oasis requires Hun troops in the simulator.");
            await attacker.Locator("input[name='movement'][value='raid']").CheckAsync();
            var troops = HeroOasisSettings.Troops(settings);
            for (var i = 1; i <= 10; i++)
                await attacker.Locator($".troops.attack tr.troopUnits input[data-t='t{i}']")
                    .FillAsync(troops[i - 1] == 0 ? "" : troops[i - 1].ToString(CultureInfo.InvariantCulture));
            await attacker.Locator(".troops.attack tr.troopUnits input[name='hero']").CheckAsync();
            return Result.Ok();
        }

        public static async Task<Result> ValidateDefenders(IPage page, IReadOnlyList<OasisAnimalTroop> animals)
        {
            var defender = page.Locator("#combatSimulatorForm .role.defender .troops.defence");
            for (var i = 1; i <= 10; i++)
            {
                if (await defender.Locator($"tr.troopIcons td:nth-child({i + 1}) i.u{30 + i}").CountAsync() != 1)
                    return Result.Fail("Simulator defender is not the expected oasis animal tribe.");
                var input = defender.Locator($"tr.troopUnits input[data-t='t{i}']");
                if (await input.CountAsync() != 1) return Result.Fail("Simulator animal input is missing.");
                var text = await input.InputValueAsync();
                if (text.Length == 0) text = "0";
                if (!TryCount(text, out var amount) || amount != animals.Where(x => x.UnitId == 30 + i).Sum(x => x.Count))
                    return Result.Fail("Simulator animals differ from the scanned oasis. Target not sent.");
            }
            return Result.Ok();
        }

        public static async Task<Result<HeroSimulationResult>> ReadSimulation(
            IPage page, HeroOasisSetting settings, int healthBefore, VillageId owner, string targetHref)
        {
            var result = page.Locator("#combatSimulator .results .result.attack");
            if (await result.CountAsync() != 1)
                return Result.Fail("Attacker simulation result is missing or ambiguous.");
            var action = page.Locator("#combatSimulator .results .resultActionWrapper form");
            var expected = HeroOasisSettings.Troops(settings);
            var valid = await ValidateComposition(action, "troop", expected);
            if (valid.IsFailed) return Result.Fail(valid.Errors);
            var destination = await ValidateDestination(action, page.Url, owner, targetHref, false);
            if (destination.IsFailed) return Result.Fail(destination.Errors);
            var actionUrl = new Uri(new Uri(page.Url), (await action.GetAttributeAsync("action"))!);
            var currentUrl = new Uri(page.Url);
            if (actionUrl.Scheme != currentUrl.Scheme || actionUrl.Authority != currentUrl.Authority ||
                actionUrl.AbsolutePath != "/build.php" || Query(actionUrl, "tt") != "2" ||
                Query(actionUrl, "newdid") != owner.Value.ToString(CultureInfo.InvariantCulture))
                return Result.Fail("Simulator send action has an unexpected destination or village.");

            var losses = 0;
            for (var i = 1; i <= 11; i++)
            {
                var initial = result.Locator($"tr.troopsInitial td.unit{i}");
                var loss = result.Locator($"tr.troopsLoss td.unit{i}");
                var remaining = result.Locator($"tr.troopsResult td.unit{i}");
                if (await initial.CountAsync() != 1 || await loss.CountAsync() != 1 || await remaining.CountAsync() != 1)
                    return Result.Fail($"Simulation troop result column t{i} is missing or ambiguous.");
                var initialText = await initial.InnerTextAsync();
                var lossText = await loss.InnerTextAsync();
                var remainingText = await remaining.InnerTextAsync();
                if (!TryCount(initialText, out var count) || count != expected[i - 1] ||
                    !TryLoss(lossText, out var lost) || !TryCount(remainingText, out var left) ||
                    lost > count || left != count - lost)
                    return Result.Fail($"Simulation troop counts at t{i} do not match the configured army: initial '{initialText}', loss '{lossText}', remaining '{remainingText}'.");
                if (i == 11 && left != 1)
                    return Result.Ok(new HeroSimulationResult(healthBefore, 0, losses, false));
                if (i != 11) losses = checked(losses + lost);
            }

            var healthLine = result.Locator("tr.combatReportLine:has(i.hero_health) span");
            if (await healthLine.CountAsync() != 1)
                return Result.Fail("Simulation hero health result is missing.");
            var match = Regex.Match(Clean(await healthLine.InnerTextAsync()),
                @"^Hero's health lowered from (?<before>\d+) to (?<after>\d+)$");
            if (!match.Success || !TryCount(match.Groups["before"].Value, out var before) ||
                !TryCount(match.Groups["after"].Value, out var after) ||
                before != healthBefore || after > before)
                return Result.Fail("Simulation hero health result is invalid or stale.");
            return Result.Ok(new HeroSimulationResult(before, after, losses, after > 0));
        }

        public static async Task<Result<bool>> PrepareSend(IPage page, HeroOasisSetting settings, VillageId owner, string targetHref)
        {
            var form = page.Locator("#build .a2b form[action*='tt=2']:has(#troops)");
            if (await form.CountAsync() != 1) return Result.Fail("Send troops form is missing.");
            if (await VillagePanelParser.GetCurrentVillageId(page) != owner)
                return Result.Fail("Send troops village changed.");
            var target = new Uri(new Uri(page.Url), targetHref);
            foreach (var coordinate in new[] { "x", "y" })
            {
                if (await form.Locator($"input[name='{coordinate}']").InputValueAsync() != Query(target, coordinate))
                    return Result.Fail("Send troops coordinates differ from the oasis.");
            }
            var hero = await IsHeroHome(page);
            if (hero.IsFailed) return Result.Fail(hero.Errors);
            if (!hero.Value) return Result.Fail("Hero is no longer available in the selected village.");
            var draft = await FarmTargetTroopsParser.ReadSteppeRiderDraft(page);
            if (draft.IsFailed) return Result.Fail(draft.Errors);
            if (draft.Value.AvailableSteppeRiders < settings.SteppeRiders ||
                draft.Value.AvailableMarksmen < settings.Marksmen || draft.Value.AvailableMarauders < settings.Marauders)
                return Result.Ok(false);
            var expected = HeroOasisSettings.Troops(settings);
            for (var i = 1; i <= 11; i++)
            {
                var input = form.Locator($"#troops input[name='troop[t{i}]']");
                if (await input.CountAsync() != 1) return Result.Fail("Send troop input is missing.");
                if (await input.IsDisabledAsync())
                {
                    if (expected[i - 1] != 0) return Result.Ok(false);
                    continue;
                }
                await input.FillAsync(expected[i - 1] == 0 ? "" : expected[i - 1].ToString(CultureInfo.InvariantCulture));
            }
            await form.Locator("input[name='eventType'][value='4']").CheckAsync();
            var redeploy = form.Locator("input[name='redeployHero']");
            if (await redeploy.CountAsync() == 1) await redeploy.UncheckAsync();
            return Result.Ok(true);
        }

        public static async Task<Result<DateTime>> ReadConfirmation(
            IPage page, HeroOasisSetting settings, VillageId owner, string targetHref)
        {
            var form = page.Locator("#troopSendForm");
            if (await form.CountAsync() != 1) return Result.Fail("Confirmation form is missing.");
            if (await VillagePanelParser.GetCurrentVillageId(page) != owner)
                return Result.Fail("Active confirmation village differs from the farm list owner.");
            var composition = await ValidateComposition(form, "troops[0]", HeroOasisSettings.Troops(settings));
            if (composition.IsFailed) return Result.Fail(composition.Errors);
            var destination = await ValidateDestination(form, page.Url, owner, targetHref, true);
            if (destination.IsFailed) return Result.Fail(destination.Errors);
            var targetLink = form.Locator("#short_info a[href*='/karte.php']");
            if (await targetLink.CountAsync() != 1)
                return Result.Fail("Confirmation target link is missing or ambiguous.");
            var target = new Uri(new Uri(page.Url), targetHref);
            var displayed = new Uri(new Uri(page.Url), (await targetLink.GetAttributeAsync("href"))!);
            if (displayed.Scheme != target.Scheme || displayed.Authority != target.Authority ||
                displayed.AbsolutePath != "/karte.php" || Query(displayed, "x") != Query(target, "x") ||
                Query(displayed, "y") != Query(target, "y"))
                return Result.Fail("Displayed confirmation target differs from the oasis.");
            if (await form.Locator("input[name='redeployHero']").InputValueAsync() is not ("" or "0"))
                return Result.Fail("Unexpected hero home village change.");
            var arrival = form.Locator("#at.timer");
            var duration = form.Locator("#in.in");
            if (await arrival.CountAsync() != 1 || await duration.CountAsync() != 1)
                return Result.Fail("Hero travel time is missing.");
            return ParseReturnTime(await arrival.GetAttributeAsync("value") ?? "", await duration.InnerTextAsync());
        }

        public static Result<DateTime> ParseReturnTime(string arrivalText, string durationText)
        {
            var match = Regex.Match(Clean(durationText), @"\b(?<h>\d+):(?<m>\d{2}):(?<s>\d{2})\b");
            if (!long.TryParse(arrivalText, NumberStyles.None, CultureInfo.InvariantCulture, out var unix) ||
                unix <= 0 || unix > 253402300799 ||
                !match.Success || !int.TryParse(match.Groups["h"].Value, out var hours) || hours > 10000 ||
                !int.TryParse(match.Groups["m"].Value, out var minutes) || minutes > 59 ||
                !int.TryParse(match.Groups["s"].Value, out var seconds) || seconds > 59)
                return Result.Fail("Hero travel time is invalid.");
            var arrival = DateTimeOffset.FromUnixTimeSeconds(unix);
            var duration = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            if (arrival > DateTimeOffset.MaxValue.Subtract(duration))
                return Result.Fail("Hero return time is out of range.");
            return Result.Ok(arrival.Add(duration).LocalDateTime);
        }

        private static async Task<Result> ValidateComposition(ILocator form, string prefix, int[] expected)
        {
            if (await form.CountAsync() != 1) return Result.Fail("Army form is missing or ambiguous.");
            if (await form.Locator("input[name^='troops['][name$='[t11]']").CountAsync() > 1)
                return Result.Fail("Multiple attack waves are not supported.");
            for (var i = 1; i <= 11; i++)
            {
                var input = form.Locator($"input[name='{prefix}[t{i}]']");
                if (await input.CountAsync() != 1 || !TryCount(await input.InputValueAsync(), out var count) ||
                    count != expected[i - 1])
                    return Result.Fail($"Army differs from simulation at t{i}; raid not confirmed.");
            }
            return Result.Ok();
        }

        private static async Task<Result> ValidateDestination(
            ILocator form, string pageUrl, VillageId owner, string targetHref, bool confirmation)
        {
            var target = new Uri(new Uri(pageUrl), targetHref);
            foreach (var name in new[] { "x", "y", "eventType" })
            {
                var input = form.Locator($"input[name='{name}']");
                var expected = name == "eventType" ? "4" : Query(target, name);
                if (await input.CountAsync() != 1 || await input.InputValueAsync() != expected)
                    return Result.Fail("Raid destination or movement type differs from simulation.");
            }
            if (confirmation)
            {
                var village = form.Locator("input[name='troops[0][villageId]']");
                if (await village.CountAsync() != 1 ||
                    await village.InputValueAsync() != owner.Value.ToString(CultureInfo.InvariantCulture))
                    return Result.Fail("Confirmation belongs to another village.");
            }
            return Result.Ok();
        }

        public static string? Query(Uri uri, string name) =>
            uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split('=', 2)).Where(x => x.Length == 2 && x[0] == name)
                .Select(x => Uri.UnescapeDataString(x[1])).SingleOrDefault();

        private static string Clean(string text) =>
            Regex.Replace(text, @"[\u200e\u200f\u202a-\u202e\u2066-\u2069]", "").Trim();

        private static bool TryCount(string text, out int count) =>
            int.TryParse(Clean(text), NumberStyles.None, CultureInfo.InvariantCulture, out count);

        private static bool TryLoss(string text, out int count) =>
            TryCount(Clean(text).Replace('\u2212', '-').TrimStart('-'), out count);
    }
}
