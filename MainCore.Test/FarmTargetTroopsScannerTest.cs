using FluentResults;
using MainCore.Entities;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class FarmTargetTroopsScannerTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Fact]
        public void QueueIdentitySeparatesListsWithinAnAccount()
        {
            var first = new MainCore.Tasks.CheckFarmTargetTroopsTask.Task(new AccountId(1), new FarmId(6706));
            var same = new MainCore.Tasks.CheckFarmTargetTroopsTask.Task(new AccountId(1), new FarmId(6706));
            var other = new MainCore.Tasks.CheckFarmTargetTroopsTask.Task(new AccountId(1), new FarmId(56));
            first.Key.ShouldBe(same.Key);
            first.Key.ShouldNotBe(other.Key);
            first.Description.ShouldContain("6706");
        }

        [Theory]
        [InlineData("2", false, true, 1)]
        [InlineData("none", false, true, 0)]
        [InlineData("?", false, true, 0)]
        [InlineData("2", true, true, 0)]
        [InlineData("2", false, false, 1)]
        public async Task DisablesOnlyConfirmedTroopTargetsInSelectedList(
            string troopCount, bool initiallyDisabled, bool persist, int expectedSaves)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var disabled = initiallyDisabled;
            var saves = 0;
            var mapOpens = 0;
            var currentVillage = 1;
            var switches = 0;
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var url = new Uri(route.Request.Url);
                if (url.AbsolutePath == "/switch")
                {
                    currentVillage = 2;
                    switches++;
                }
                if (route.Request.Url.Contains("/save", StringComparison.Ordinal))
                {
                    saves++;
                    if (persist) disabled = true;
                    await route.FulfillAsync(new() { Body = "ok" });
                    return;
                }
                var troops = troopCount == "none" ? "<tr><td>none</td></tr>" :
                    $"<tr><td><img class='unit u31'></td><td class='val'>{troopCount}</td></tr>";
                var map = $"""
                    <div id="dialogContent"><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=25&amp;y=184">Center map</a>
                    <div id="map_details"><table id="troop_info"><tbody>{troops}</tbody></table>
                    <div><table id="troop_info" class="rep"><tr><td>No information</td></tr></table></div></div>
                    </div></div>
                    """;
                var body = $"""
                    <div id="rallyPointFarmList"><div class="farmListWrapper expanded">
                    <div class="farmListHeader"><div data-list="6706"></div></div>
                    <table class="slots"><tbody><tr class="slot {(disabled ? "disabled" : "")}">
                    <td><input name="selectOne" data-slot-id="637157"></td>
                    <td class="target"><a href="/karte.php?x=25&amp;y=184">Oasis</a></td>
                    <td class="actions">
                        <button class="menuButton" onclick="document.querySelector('#slot-menu-637157').hidden=false">⋯</button>
                        <div id="slot-menu-637157" class="menu" hidden>
                            <button data-action="deactivate" onclick="fetch('/save').then(()=>{{document.querySelector('#slot-menu-637157').hidden=true;{(persist ? "document.querySelector(\"input[data-slot-id='637157']\")?.closest(\"tr.slot\")?.classList.add(\"disabled\");" : "")}}})">Deactivate target</button>
                        </div>
                    </td>
                    </tr></tbody></table></div></div>
                    """;
                if (route.Request.Url.Contains("/karte.php", StringComparison.Ordinal))
                {
                    currentVillage.ShouldBe(2);
                    mapOpens++;
                    body = map;
                }
                else
                {
                    body = $"""
                        <div id="sidebarBoxVillageList">
                        <div class="listEntry village {(currentVillage == 1 ? "active" : "")}" data-did="1"><span class="name">0</span></div>
                        <div class="listEntry village {(currentVillage == 2 ? "active" : "")}" data-did="2" onclick="location.href='/switch'"><span class="name">7</span></div>
                        </div>
                        """ + body.Replace(
                            "<div id=\"rallyPointFarmList\">",
                            """<div id="rallyPointFarmList"><div class="villageWrapper"><div class="villageHeader collapsed"><div class="villageName">7</div><a class="expandCollapse" onclick="this.parentElement.classList.replace('collapsed', 'expanded')"><svg width="12" height="8" viewBox="0 0 12 8"><path d="M0 0L6 8L12 0Z"></path></svg></a></div>""") + "</div>";
                }
                await route.FulfillAsync(new() { ContentType = "text/html", Body = body });
            });
            await page.GotoAsync("https://example.com/farm");
            var browser = Substitute.For<IChromeBrowser>();
            var logger = Substitute.For<Serilog.ILogger>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(logger);
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await ((ILocator)call[0]).ClickAsync();
                await page.WaitForLoadStateAsync();
                return Result.Ok();
            });
            browser.Navigate(Arg.Any<string>()).Returns(async call =>
            {
                await page.GotoAsync((string)call[0]);
                return Result.Ok();
            });
            browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await ((ILocator)call[0]).WaitForAsync();
                return Result.Ok();
            });

            (await new FarmTargetTroopsScanner(browser).Scan(new FarmId(6706), CancellationToken.None)).IsSuccess.ShouldBeTrue();

            saves.ShouldBe(expectedSaves);
            disabled.ShouldBe(initiallyDisabled || (expectedSaves == 1 && persist));
            new Uri(page.Url).AbsolutePath.ShouldBe("/farm");
            currentVillage.ShouldBe(2);
            switches.ShouldBe(1);
            if (initiallyDisabled) mapOpens.ShouldBe(0);
            if (expectedSaves == 1 && !persist)
                logger.Received().Warning("Farm target {SlotId}: saved deactivation was not confirmed", "637157");
        }

    }
}
