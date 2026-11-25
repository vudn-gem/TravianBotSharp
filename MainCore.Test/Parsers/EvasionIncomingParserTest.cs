using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public sealed class EvasionIncomingParserTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData("11:01:19", 39679)]
        [InlineData("11:03:44", 39824)]
        [InlineData("25:00:00", 90000)]
        [InlineData("00:00:00", 0)]
        public void ParsesLiveCountdown(string text, int seconds) =>
            EvasionIncomingParser.ParseRemaining(text).Value.TotalSeconds.ShouldBe(seconds);

        [Theory]
        [InlineData("")]
        [InlineData("at 22:19:14")]
        [InlineData("-1:00:00")]
        [InlineData("11:60:00")]
        [InlineData("11:00:60")]
        [InlineData("2147483648:00:00")]
        [InlineData("2147483647:00:00")]
        public void RejectsInvalidCountdown(string text) =>
            EvasionIncomingParser.ParseRemaining(text).IsFailed.ShouldBeTrue();

        [Fact]
        public async Task ReadsBothAttackAndRaidAndIgnoresStaleDataValueAndReinforcements()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">
                <table class="troop_details inAttack"><thead><tr><td>
                <img class="markAttack" id="markSymbol_20289404"></td></tr></thead>
                <tbody class="infos"><tr><th>Arrival</th><td>
                <div class="in"><span class="timer" counting="down" data-value="99999">11:03:44</span></div>
                <div class="at"><span>at 22:21:39</span></div></td></tr></tbody></table>
                <table class="troop_details inRaid"><thead><tr><td>
                <img class="markAttack" id="markSymbol_20284933"></td></tr></thead>
                <tbody class="infos"><tr><th>Arrival</th><td>
                <div class="in"><span class="timer" counting="down" value="99999" data-value="99999">11:01:19</span></div>
                <div class="at"><span>at 22:19:14</span></div></td></tr></tbody></table>
                <table class="troop_details inReinforcement"></table>
                <div class="paginator"><img class="next disabled"></div>
                </div></div>
                """);
            var now = new DateTime(2026, 11, 25, 23, 30, 0);
            var result = await EvasionIncomingParser.Read(page, now);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Select(x => x.MovementId).ShouldBe([20284933L, 20289404L]);
            result.Value[0].Raid.ShouldBeTrue();
            result.Value[0].Arrival.ShouldBe(now.AddSeconds(39679));
            result.Value[1].Raid.ShouldBeFalse();
            result.Value[1].Arrival.ShouldBe(now.AddSeconds(39824));
            result.Value[0].Arrival.Date.ShouldBe(now.Date.AddDays(1));
        }

        [Fact]
        public async Task ReadsIncomingOverviewWithNestedMarkAndSingleDigitHour()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <span id="servertime">15:42:00</span>
                <div id="content" class="buildRallyPoint"><div class="data rallyPointOverviewContainer">
                <h4 class="spacer">Incoming troops (2)</h4>
                <div class="paginatorTop"><div class="paginator"><img class="next disabled"></div></div>
                <a name="at"></a><table class="troop_details inRaid">
                <thead><tr><td class="role"></td><td class="troopHeadline">
                <a class="markAttack"><img id="markSymbol_101" class="markAttack markAttack0"></a>
                </td></tr></thead><tbody class="units"><tr><td class="unit">?</td></tr></tbody>
                <tbody class="infos"><tr><th>Arrival</th><td>
                <div class="in">in&nbsp;<span class="timer" counting="down" data-value="23834">6:37:14</span>&nbsp;hrs.</div>
                <div class="at"><span>at&nbsp;22:19:14</span></div></td></tr></tbody></table>
                <a name="at"></a><table class="troop_details inAttack">
                <thead><tr><td class="role"></td><td class="troopHeadline">
                <a class="markAttack"><img id="markSymbol_102" class="markAttack markAttack0"></a>
                </td></tr></thead><tbody class="units"><tr><td class="unit">?</td></tr></tbody>
                <tbody class="infos"><tr><th>Arrival</th><td>
                <div class="in">in&nbsp;<span class="timer" counting="down" data-value="23979">6:39:39</span>&nbsp;hrs.</div>
                <div class="at"><span>at&nbsp;22:21:39</span><span> </span></div>
                </td></tr></tbody></table>
                <div class="paginator"><img class="next disabled"></div></div></div>
                """);
            var now = new DateTime(2026, 11, 25, 15, 42, 0);
            var result = await EvasionIncomingParser.Read(page, now);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Select(x => x.MovementId).ShouldBe([101L, 102L]);
            result.Value[0].Arrival.ShouldBe(now.AddSeconds(23834));
            result.Value[0].Raid.ShouldBeTrue();
            result.Value[0].DisplayedArrival.ShouldBe(new TimeOnly(22, 19, 14));
            result.Value[0].ArrivalDifferenceSeconds.ShouldBe(0);
            result.Value[1].Arrival.ShouldBe(now.AddSeconds(23979));
            result.Value[1].Raid.ShouldBeFalse();
            result.Value[1].DisplayedArrival.ShouldBe(new TimeOnly(22, 21, 39));
            result.Value[1].ArrivalDifferenceSeconds.ShouldBe(0);
        }

        [Theory]
        [InlineData("23:59:50", "00:00:20", "at 00:00:10", 0)]
        [InlineData("23:59:50", "25:00:20", "at 01:00:10", 0)]
        [InlineData("23:59:50", "00:00:20", "at 00:00:16", 6)]
        [InlineData("00:00:10", "00:00:00", "at 23:59:59", -11)]
        [InlineData("23:59:50", "00:00:20", "at invalid", null)]
        public async Task ComparesPlayerArrivalWithSystemTimeWithoutChangingCountdownSchedule(
            string localTime, string countdown, string displayedArrival, int? difference)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync($"""
                <span id="servertime">12:34:56</span>
                <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">
                <table class="troop_details inAttack"><thead><tr><td>
                <img class="markAttack" id="markSymbol_101"></td></tr></thead>
                <tbody class="infos"><tr><td><div class="in">
                <span class="timer" counting="down">{countdown}</span></div>
                <div class="at"><span>{displayedArrival}</span></div></td></tr></tbody></table>
                </div></div>
                """);
            var now = new DateTime(2026, 11, 25).Add(TimeSpan.Parse(localTime));
            var result = await EvasionIncomingParser.Read(page, now);
            result.IsSuccess.ShouldBeTrue();
            var movement = result.Value.Single();
            movement.Arrival.ShouldBe(now.Add(EvasionIncomingParser.ParseRemaining(countdown).Value));
            movement.ArrivalDifferenceSeconds.ShouldBe(difference);
        }

        [Theory]
        [InlineData("<div>Login</div>")]
        [InlineData("<div id='content' class='buildRallyPoint'><div class='rallyPointOverviewContainer'><img class='next'></div></div>")]
        [InlineData("<table class='troop_details inRaid'></table>")]
        public async Task RejectsMissingOrIncompleteOverview(string html)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            if (html.StartsWith("<table", StringComparison.Ordinal))
                html = $"<div id='content' class='buildRallyPoint'><div class='rallyPointOverviewContainer'>{html}</div></div>";
            if (html.Contains("class='next'", StringComparison.Ordinal))
                html = html.Replace("<img class='next'>", "<div class='paginator'><img class='next'></div>");
            await page.SetContentAsync(html);
            (await EvasionIncomingParser.Read(page, DateTime.Now)).IsFailed.ShouldBeTrue();
        }
    }
}
