using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public sealed class IncomingTroopsParserTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Fact]
        public async Task UsesVillageRowClassNotSharedSvgOrGroupClass()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div class="villageList">
                  <div class="listEntry village" data-did="23414">
                    <span class="incomingTroops"><svg class="attack"></svg></span>
                    <span class="name">0</span>
                  </div>
                  <div class="listEntry village attack" data-did="34403">
                    <span class="incomingTroops"><svg class="attack"></svg></span>
                    <span class="name">20</span>
                    <span class="coordinatesGrid">(&#8237;−41&#8236;|96)</span>
                  </div>
                  <div class="listEntry group attack" data-groupid="1"><span class="name">Def</span></div>
                  <div class="groupWrapper">
                    <div class="listEntry village active" data-did="28860">
                      <span class="incomingTroops"><svg class="attack"></svg></span>
                      <span class="name">7</span>
                    </div>
                  </div>
                </div>
                """);
            var result = await IncomingTroopsParser.Read(page);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Count.ShouldBe(3);
            result.Value.Where(x => x.Attack).Select(x => x.Id).ShouldBe([34403]);
            result.Value.Single(x => x.Id == 34403).Coordinates.ShouldBe("(−41|96)");
        }

        [Theory]
        [InlineData("<div>Login</div>")]
        [InlineData("<div class='villageList'></div>")]
        [InlineData("<div class='villageList'><div class='listEntry village' data-did='0'><span class='name'>v</span></div></div>")]
        [InlineData("<div class='villageList'><div class='listEntry village' data-did='1'></div></div>")]
        [InlineData("<div class='villageList'><div class='listEntry village' data-did='1'><span class='name'>v</span></div><div class='listEntry village' data-did='1'><span class='name'>v</span></div></div>")]
        public async Task RejectsInvalidSnapshots(string html)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(html);
            (await IncomingTroopsParser.Read(page)).IsFailed.ShouldBeTrue();
        }

        [Theory]
        [InlineData("Incoming attacks: 2", 2, 0)]
        [InlineData("Incoming raids: 1", 0, 1)]
        [InlineData("Incoming attacks: 1\nIncoming raids: 2", 1, 2)]
        [InlineData("Incoming reinforcements: 7\nIncoming attacks: 1", 1, 0)]
        [InlineData("Incoming attacks: 0\nIncoming raids: 0", 0, 0)]
        public void ParsesSeparateCounts(string text, int attacks, int raids)
        {
            var counts = IncomingTroopsParser.ParseCounts(text);
            counts.IsSuccess.ShouldBeTrue();
            counts.Value.ShouldBe((attacks, raids));
        }

        [Theory]
        [InlineData("")]
        [InlineData("Loading...")]
        [InlineData("Incoming attacks: -1")]
        [InlineData("Incoming attacks: 2147483648")]
        [InlineData("Incoming attacks: 1\nIncoming attacks: 2")]
        [InlineData("Incoming attacks: 1\nIncoming raids: ?")]
        public void InvalidCountsAreNotTreatedAsZero(string text)
        {
            IncomingTroopsParser.ParseCounts(text).IsFailed.ShouldBeTrue();
        }

        [Fact]
        public async Task HoversAndReadsDynamicallyLinkedTooltipForEachVillage()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div class="villageList">
                  <div class="listEntry village attack" data-did="23414">
                    <span class="incomingTroops" data-id="23414" style="display:inline-block;padding:10px">badge 0</span>
                    <span class="name">0</span>
                  </div>
                  <div class="listEntry village attack" data-did="28860">
                    <span class="incomingTroops" data-id="28860" style="display:inline-block;padding:10px">badge 7</span>
                    <span class="name">7</span>
                  </div>
                  <div class="listEntry village attack" data-did="34403">
                    <span class="incomingTroops" data-id="34403" style="display:inline-block;padding:10px">badge 20</span>
                    <span class="name">20</span>
                  </div>
                  <div class="listEntry village" data-did="4"><span class="name">safe</span></div>
                </div>
                <div id="unrelated"><div class="tippy-box" data-state="visible"><div class="tippy-content"><div class="text">Incoming attacks: 99</div></div></div></div>
                <script>
                  let sequence = 448;
                  const texts = {'23414':'Incoming attacks: 2<br>Incoming raids: 1', '28860':'Incoming attacks: 2<br>', '34403':'Incoming raids: 1'};
                  document.querySelectorAll('.incomingTroops').forEach(badge => {
                    badge.addEventListener('mouseenter', () => {
                      document.querySelectorAll('[data-tippy-root]').forEach(root => root.remove());
                      const root = document.createElement('div');
                      root.id = 'tippy-' + sequence++;
                      root.setAttribute('data-tippy-root', '');
                      root.innerHTML = '<div class="tippy-box" data-state="visible"><div class="tippy-content"><div class="text"></div></div></div>';
                      document.body.append(root);
                      badge.setAttribute('aria-describedby', root.id);
                      setTimeout(() => root.querySelector('.text').innerHTML = texts[badge.dataset.id], 50);
                    });
                  });
                </script>
                """);
            var villages = (await IncomingTroopsParser.Read(page)).Value;
            var expected = new[] { (2, 1), (2, 0), (0, 1), (0, 0) };
            for (var i = 0; i < villages.Count; i++)
            {
                var counts = await IncomingTroopsParser.ReadCounts(page, villages[i], CancellationToken.None);
                counts.IsSuccess.ShouldBeTrue();
                (counts.Value.Attacks, counts.Value.Raids).ShouldBe(expected[i]);
            }
            page.Url.ShouldBe("about:blank");
        }
    }
}
