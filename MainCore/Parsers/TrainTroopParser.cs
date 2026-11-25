using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MainCore.Parsers
{
    public static class TrainTroopParser
    {
        public static ILocator GetInputBox(IPage page, TroopEnums troop)
        {
            var node = page.Locator($"div.troop:not(.empty):has(img.unit.u{(int)troop}) div.cta input.text");
            return node;
        }

        public static async Task<int> GetMaxAmount(IPage page, TroopEnums troop)
        {
            var node = page.Locator($"div.troop:not(.empty):has(img.unit.u{(int)troop}) div.cta a");
            var valueStrFixed = WebUtility.HtmlDecode(await node.InnerTextAsync());
            return valueStrFixed.ParseInt();
        }

        public static async Task<Result<int>> ReadMaximum(IPage page, TroopEnums troop)
        {
            var node = page.Locator($"div.troop:not(.empty):has(img.unit.u{(int)troop}) div.cta a");
            if (await node.CountAsync() != 1)
                return Result.Fail("Evasion troop is unavailable or its maximum training amount is ambiguous.");
            var text = WebUtility.HtmlDecode(await node.InnerTextAsync()).Trim();
            if (text.StartsWith('(') && text.EndsWith(')')) text = text[1..^1];
            // Travian formats thousands with separators and may include directional marks.
            var normalized = Regex.Replace(text, @"[\s.,\u200e\u200f\u202a-\u202e\u2066-\u2069]", "");
            if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
                return Result.Fail("Evasion maximum training amount is invalid or outside the supported range.");
            return Result.Ok(amount);
        }

        public static ILocator GetTrainButton(IPage page)
        {
            var node = page.Locator("#s1");
            return node;
        }
    }
}