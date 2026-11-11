using System.Globalization;
using System.Text.Json;

namespace MainCore.Parsers
{
    public static class ReportParser
    {
        public static async Task<Result<int>> GetUnreadCount(IPage page)
        {
            // Read the link and badge together so a DOM update cannot split the snapshot.
            var snapshot = await page.EvaluateAsync<JsonElement>(@"() => {
                const links = document.querySelectorAll('a.reports[accesskey=""5""]');
                if (links.length !== 1) return { valid: false };
                const badges = links[0].querySelectorAll('.indicator');
                return {
                    valid: badges.length <= 1,
                    text: badges.length === 0 ? null : badges[0].textContent
                };
            }");

            if (!snapshot.GetProperty("valid").GetBoolean())
            {
                return Result.Fail("Report link or badge is missing or ambiguous.");
            }

            var text = snapshot.GetProperty("text").GetString();
            if (text is null) return Result.Ok(0);

            if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                return Result.Fail("Report badge does not contain a valid non-negative count.");
            }

            return Result.Ok(count);
        }
    }
}
