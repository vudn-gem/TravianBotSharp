using FluentResults;
using MainCore.Entities;
using MainCore.Parsers;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class FarmListPreparationTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(true, true, 2)]
        [InlineData(true, false, 2)]
        [InlineData(false, true, 2)]
        [InlineData(false, false, 2)]
        [InlineData(true, true, 0)]
        public async Task OpensOnlySelectedListAndWaitsForAllTargets(bool villageCollapsed, bool listCollapsed, int count)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(3000);
            await page.SetContentAsync($$"""
                <style>
                .villageHeader.collapsed + .dropContainer { display:none; }
                .villageHeader a.expandCollapse { pointer-events:none; }
                .villageHeader a.expandCollapse svg { pointer-events:auto; }
                </style>
                <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="2"><span class="name">Home</span></div></div>
                <div id="rallyPointFarmList"><div class="villageWrapper">
                <div class="villageHeader {{(villageCollapsed ? "collapsed" : "expanded")}}"><div class="villageName">Home</div>
                <a class="expandCollapse" onclick="setTimeout(() => this.parentElement.className='villageHeader expanded', 30)">
                <svg width="12" height="8" viewBox="0 0 12 8"><path d="M0 0L6 8L12 0Z"></path></svg></a></div>
                <div class="dropContainer">
                <div class="farmListWrapper collapsed" id="other"><div class="farmListHeader"><div data-list="20"></div>
                <a class="expandCollapse" onclick="throw new Error('Wrong list expanded')">Other list</a></div></div>
                <div class="farmListWrapper {{(listCollapsed ? "collapsed" : "expanded")}}" id="selected">
                <div class="farmListHeader"><div data-list="10"></div>
                <a class="expandCollapse" onclick="setTimeout(() => { document.querySelector('#selected').className='farmListWrapper expanded'; loadTargets(); }, 30)">Selected list</a></div>
                <div class="slotsWrapper"><table class="slots"><tbody><tr><td class="addTarget">Add target {{count}}/100</td></tr></tbody></table></div>
                </div></div></div></div>
                <script>
                function loadTargets() {
                    setTimeout(() => {
                        const body = document.querySelector('#selected table tbody');
                        for (let i=0; i<{{count}}; i++) {
                            setTimeout(() => {
                                const row = document.createElement('tr');
                                row.className='slot';
                                row.innerHTML=`<td><input name="selectOne" data-slot-id="${i+1}"></td><td class="target"><a href="/karte.php?x=${i+1}&y=2">Oasis</a></td>`;
                                body.prepend(row);
                            }, i * 100);
                        }
                    }, 200);
                }
                </script>
                """);
            if (villageCollapsed)
            {
                await page.Locator(".villageHeader .villageName").ClickAsync();
                (await page.Locator(".villageHeader").GetAttributeAsync("class")).ShouldBe("villageHeader collapsed");
            }
            if (!listCollapsed) await page.EvaluateAsync("loadTargets()");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await ((ILocator)call[0]).ClickAsync();
                return Result.Ok();
            });

            var prepared = await FarmListPreparation.Prepare(browser, new FarmId(10), new VillageId(2));
            prepared.IsSuccess.ShouldBeTrue();
            var targets = await FarmTargetTroopsParser.ReadTargets(page, new FarmId(10));
            targets.IsSuccess.ShouldBeTrue();
            targets.Value.Count.ShouldBe(count);
            (await page.Locator("#other").GetAttributeAsync("class")).ShouldBe("farmListWrapper collapsed");
            (await page.Locator(".villageHeader").GetAttributeAsync("class")).ShouldBe("villageHeader expanded");
            (await page.Locator("#selected").GetAttributeAsync("class")).ShouldBe("farmListWrapper expanded");
        }

        [Fact]
        public async Task DoesNotInterpretCollapsedListAsEmpty()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div id="rallyPointFarmList"><div class="farmListWrapper collapsed">
                <div class="farmListHeader"><div data-list="10"></div></div>
                <table class="slots"><tbody><tr><td class="addTarget">Add target 6/100</td></tr></tbody></table>
                </div></div>
                """);
            var targets = await FarmTargetTroopsParser.ReadTargets(page, new FarmId(10));
            targets.IsFailed.ShouldBeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task DoesNotTreatUnloadedTargetsAsAnEmptyList(bool hasCounter)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(300);
            await page.SetContentAsync("""
                <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="2"><span class="name">Home</span></div></div>
                <div id="rallyPointFarmList"><div class="villageWrapper">
                <div class="villageHeader expanded"><div class="villageName">Home</div></div>
                <div class="dropContainer"><div class="farmListWrapper expanded">
                <div class="farmListHeader"><div data-list="10"></div></div>
                <table class="slots"><tbody><tr><td class="addTarget">Add target 6/100</td></tr></tbody></table>
                </div></div></div></div>
                """);
            if (!hasCounter) await page.Locator("td.addTarget").EvaluateAsync("node => node.remove()");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);

            await Should.ThrowAsync<TimeoutException>(() =>
                FarmListPreparation.Prepare(browser, new FarmId(10), new VillageId(2)));
        }
    }
}
