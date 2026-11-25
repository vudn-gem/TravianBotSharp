using System.Globalization;

namespace MainCore.Parsers
{
    public static class EvasionTroopsParser
    {
        public static ILocator SendForm(IPage page) =>
            RallyPointSendTroopsTab.SendForm(page);

        public static Task<bool> HasVillage(IPage page, VillageId villageId) =>
            RallyPointSendTroopsTab.HasVillage(page, villageId);

        public static async Task<Result<long[]>> ReadAvailable(IPage page, VillageId villageId, CancellationToken cancellationToken)
        {
            var form = SendForm(page);
            if (await form.CountAsync() != 1 || !await HasVillage(page, villageId))
                return Result.Fail("Evasion send form is missing, ambiguous or belongs to another village.");
            var troops = new long[11];
            for (var i = 1; i <= 11; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = form.Locator($"#troops input[name='troop[t{i}]']");
                var count = await input.CountAsync();
                if (i == 11 && count == 0)
                {
                    if (await form.Locator("#troops img.unit.uhero").CountAsync() != 0)
                        return Result.Fail("Hero availability input is missing.");
                    continue;
                }
                if (count != 1) return Result.Fail($"Evasion troop field t{i} is missing or ambiguous.");
                var cell = form.Locator($"#troops td:has(input[name='troop[t{i}]'])");
                var available = cell.Locator("a, span.none");
                if (await cell.CountAsync() != 1 || await cell.Locator("img.unit").CountAsync() != 1 ||
                    await available.CountAsync() != 1 ||
                    !FarmTargetTroopsParser.TryParseTroopCount(await available.InnerTextAsync(), out var amount) ||
                    amount < 0 || (i == 11 && amount > 1))
                    return Result.Fail($"Evasion available troop count t{i} is invalid.");
                if (amount > 0 && !await input.IsEnabledAsync())
                    return Result.Fail($"Evasion troop field t{i} has available troops but is disabled.");
                troops[i - 1] = amount;
            }
            if (!troops.Any(x => x > 0)) return Result.Fail("No troops or Hero are available for evasion.");
            return Result.Ok(troops);
        }

        public static async Task<Result> Fill(IChromeBrowser browser, long[] troops, int x, int y,
            CancellationToken cancellationToken)
        {
            var form = SendForm(browser.CurrentPage);
            foreach (var (name, coordinate) in new[] { ("x", x), ("y", y) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = form.Locator($"input[name='{name}']");
                if (await input.CountAsync() != 1 || !await input.IsEnabledAsync() ||
                    await input.GetAttributeAsync("type") == "hidden")
                    return Result.Fail($"Evasion coordinate field {name} is unavailable.");
                var filled = await browser.Input(input, coordinate.ToString(CultureInfo.InvariantCulture));
                if (filled.IsFailed) return filled;
            }
            for (var i = 1; i <= 11; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = form.Locator($"#troops input[name='troop[t{i}]']");
                if (i == 11 && troops[i - 1] == 0 && await input.CountAsync() == 0) continue;
                if (await input.IsDisabledAsync())
                {
                    if (troops[i - 1] != 0 || (await input.InputValueAsync()).Trim() is not ("" or "0"))
                        return Result.Fail($"Disabled evasion field t{i} contains troops.");
                    continue;
                }
                var filled = await FarmTargetTroopsParser.FillSendTroopInput(browser, input, troops[i - 1]);
                if (filled.IsFailed) return filled;
            }
            var raid = form.Locator("input[type='radio'][name='eventType'][value='4'], input[type='radio'][name='c'][value='4']");
            if (await raid.CountAsync() != 1 || !await raid.IsEnabledAsync())
                return Result.Fail("Attack: Raid is missing, ambiguous or disabled.");
            await raid.CheckAsync();
            if (!await raid.IsCheckedAsync()) return Result.Fail("Attack: Raid was not selected.");
            var redeploy = form.Locator("input[name='redeployHero']");
            if (await redeploy.CountAsync() > 1) return Result.Fail("Hero redeploy setting is ambiguous.");
            if (await redeploy.CountAsync() == 1)
            {
                await redeploy.UncheckAsync();
                if (await redeploy.IsCheckedAsync()) return Result.Fail("Hero redeployment could not be disabled.");
            }
            return Result.Ok();
        }

        public static async Task<Result> Validate(IPage page, VillageId villageId, long[] troops, int x, int y,
            bool confirmation, CancellationToken cancellationToken)
        {
            var form = confirmation ? page.Locator("#troopSendForm") : SendForm(page);
            if (await form.CountAsync() != 1 || !await HasVillage(page, villageId))
                return Result.Fail("Evasion army form/village changed.");
            var origin = new Uri(page.Url);
            if (!Uri.TryCreate(origin, await form.GetAttributeAsync("action"), out var action) ||
                action.Scheme != origin.Scheme || action.Authority != origin.Authority ||
                action.AbsolutePath != "/building" || HeroOasisParser.Query(action, "tt") != "2")
                return Result.Fail("Evasion army form submits to an unexpected destination.");
            foreach (var (name, expected) in new[] { ("x", x), ("y", y) })
            {
                var input = form.Locator($"input[name='{name}']");
                if (await input.CountAsync() != 1 ||
                    await input.InputValueAsync() != expected.ToString(CultureInfo.InvariantCulture))
                    return Result.Fail("Evasion target coordinates changed.");
            }
            var type = confirmation ? form.Locator("input[name='eventType']") :
                form.Locator("input[type='radio'][name='eventType']:checked, input[type='radio'][name='c']:checked");
            if (await type.CountAsync() != 1 || await type.InputValueAsync() != "4")
                return Result.Fail("Evasion movement is not Attack: Raid.");
            var prefix = confirmation ? "troops[0]" : "troop";
            var fields = await form.Locator($"input[name^='{prefix}[t']").EvaluateAllAsync<string[]>(
                "nodes => nodes.map(node => node.name)");
            var expectedNames = Enumerable.Range(1, 11).Select(i => $"{prefix}[t{i}]").ToHashSet();
            if (fields.Any(name => !expectedNames.Contains(name)))
                return Result.Fail("Evasion form contains unsupported troop fields.");
            for (var i = 1; i <= 11; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = form.Locator($"input[name='{prefix}[t{i}]']");
                if (i == 11 && troops[i - 1] == 0 && await input.CountAsync() == 0) continue;
                if (await input.CountAsync() != 1) return Result.Fail($"Evasion army field t{i} is missing or ambiguous.");
                var value = (await input.InputValueAsync()).Trim();
                if (value.Length == 0 && troops[i - 1] == 0) continue;
                if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var actual) ||
                    actual != troops[i - 1] || (actual > 0 && await input.IsDisabledAsync()))
                    return Result.Fail($"Evasion army count t{i} differs from the scanned army.");
            }
            if (confirmation)
            {
                var village = form.Locator("input[name='troops[0][villageId]']");
                if (await village.CountAsync() != 1 ||
                    await village.InputValueAsync() != villageId.Value.ToString(CultureInfo.InvariantCulture))
                    return Result.Fail("Evasion confirmation belongs to another village.");
                var waves = await form.Locator("input[name^='troops[']").EvaluateAllAsync<string[]>(
                    "nodes => nodes.map(node => node.name)");
                if (waves.Any(name => !name.StartsWith("troops[0][", StringComparison.Ordinal)))
                    return Result.Fail("Additional evasion attack waves are not supported.");
                var redeploy = form.Locator("input[name='redeployHero']");
                if (await redeploy.CountAsync() > 1 || (await redeploy.CountAsync() == 1 &&
                    await redeploy.InputValueAsync() is not ("" or "0")))
                    return Result.Fail("Evasion confirmation would redeploy the Hero.");
                var map = form.Locator("#short_info a[href*='/map']");
                if (await map.CountAsync() != 1 ||
                    !Uri.TryCreate(new Uri(page.Url), await map.GetAttributeAsync("href"), out var target) ||
                    target.Authority != new Uri(page.Url).Authority || target.Scheme != new Uri(page.Url).Scheme ||
                    target.AbsolutePath != "/map" ||
                    HeroOasisParser.Query(target, "x") != x.ToString(CultureInfo.InvariantCulture) ||
                    HeroOasisParser.Query(target, "y") != y.ToString(CultureInfo.InvariantCulture))
                    return Result.Fail("Displayed evasion confirmation target differs from the configured coordinates.");
            }
            if (await page.Locator("#content .errorMessage, #content .errorMessageContainer").CountAsync() > 0)
                return Result.Fail("The game reported an error while preparing evasion.");
            return Result.Ok();
        }
    }
}
