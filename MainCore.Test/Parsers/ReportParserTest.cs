using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public sealed class ReportParserTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData("<a class='reports' href='/report' accesskey='5'></a>", 0)]
        [InlineData("<a class='reports' href='/report' accesskey='5'><div class='indicator'>1</div></a>", 1)]
        [InlineData("<a class='reports' href='/report?x=1' accesskey='5'><div class='indicator'> 3 </div></a>", 3)]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'>0</div></a>", 0)]
        public async Task ReadsBadgeWithoutNavigating(string html, int expected)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html);
            var result = await ReportParser.GetUnreadCount(page);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(expected);
            page.Url.ShouldBe("about:blank");
            (await page.ContentAsync()).ShouldContain(html.Contains("indicator") ? "indicator" : "reports");
        }

        [Theory]
        [InlineData("<div>Login page</div>")]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'></div></a>")]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'>99+</div></a>")]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'>-1</div></a>")]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'>2147483648</div></a>")]
        [InlineData("<a class='reports' accesskey='5'></a><a class='reports' accesskey='5'></a>")]
        [InlineData("<a class='reports' accesskey='5'><div class='indicator'>1</div><div class='indicator'>2</div></a>")]
        public async Task RejectsMissingOrInvalidBadges(string html)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html);
            var result = await ReportParser.GetUnreadCount(page);
            result.IsFailed.ShouldBeTrue();
        }
    }
}
