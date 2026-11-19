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
            var first = new MainCore.Tasks.CheckFarmTargetOasisTask.Task(new AccountId(1), new FarmId(6706));
            var same = new MainCore.Tasks.CheckFarmTargetOasisTask.Task(new AccountId(1), new FarmId(6706));
            var other = new MainCore.Tasks.CheckFarmTargetOasisTask.Task(new AccountId(1), new FarmId(56));
            first.Key.ShouldBe(same.Key);
            first.Key.ShouldNotBe(other.Key);
            first.Key.ShouldNotBe(new MainCore.Tasks.CheckFarmTargetOasisTask.Task(
                new AccountId(1), new FarmId(6706), activate: true).Key);
            first.Description.ShouldContain("6706");
            first.Key.ShouldNotBe(new MainCore.Tasks.CheckFarmTargetOasisTask.Task(
                new AccountId(1), new FarmId(6706), synchronize: true).Key);
        }

        [Theory]
        [InlineData("2", false, true, false, 1)]
        [InlineData("none", false, true, false, 0)]
        [InlineData("?", false, true, false, 0)]
        [InlineData("2", true, true, false, 0)]
        [InlineData("2", false, false, false, 1)]
        [InlineData("1", false, true, false, 0)]
        [InlineData("1", true, true, true, 1)]
        [InlineData("2", true, true, true, 0)]
        [InlineData("none", true, true, true, 1)]
        [InlineData("1", true, true, false, 1, true)]
        [InlineData("1", false, true, false, 0, true)]
        [InlineData("2", false, true, false, 1, true)]
        [InlineData("2", true, true, false, 0, true)]
        [InlineData("none", true, true, false, 1, true)]
        [InlineData("none", false, true, false, 0, true)]
        [InlineData("0", true, true, false, 1, true)]
        [InlineData("?", true, true, false, 0, true)]
        [InlineData("?", false, true, false, 0, true)]
        public async Task ChangesOnlyTargetsMatchingSelectedOasisAction(
            string troopCount, bool initiallyDisabled, bool persist, bool activate, int expectedSaves, bool synchronize = false)
        {
            if (synchronize) activate = initiallyDisabled;
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var disabled = initiallyDisabled;
            var saves = 0;
            var targetNavigations = 0;
            var newTabFarmNavigations = 0;
            var events = new List<string>();
            var originNavigations = 0;
            var currentVillage = 1;
            var switches = 0;
            await context.RouteAsync("https://example.com/**", async route =>
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
                    if (persist) disabled = !activate;
                    await route.FulfillAsync(new() { Body = "ok" });
                    return;
                }
                var troops = troopCount == "none" ? "<tr><td>none</td></tr>" :
                    $"<tr><td><img class='unit u31'></td><td class='val'>{troopCount}</td></tr>";
                var map = $"""
                    <div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=25&amp;y=184">Center map</a>
                    <div id="map_details"><table id="troop_info"><tbody>{troops}</tbody></table>
                    <div><table id="troop_info" class="rep"><tr><td>No information</td></tr></table></div></div>
                    </div>
                    """;
                if (url.AbsolutePath == "/karte.php")
                {
                    await route.FulfillAsync(new() { ContentType = "text/html", Body = map });
                    return;
                }
                var body = $$"""
                    <div id="rallyPointFarmList"><div class="farmListWrapper expanded">
                    <div class="farmListHeader"><div data-list="6706"></div></div>
                    <table class="slots"><tbody><tr class="slot {{(disabled ? "disabled" : "")}}">
                    <td><input name="selectOne" data-slot-id="637157"></td>
                    <td class="target"><a href="/karte.php?x=25&amp;y=184"
                    onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#target-map').innerHTML;return false;">Oasis</a></td>
                    <td class="actions"><button class="menuButton" onclick="document.querySelector('#slot-menu-637157').hidden=false">⋯</button>
                    <div id="slot-menu-637157" class="menu" hidden>
                    <button data-action="deactivate" onclick="fetch('/save').then(()=>{document.querySelector('#slot-menu-637157').hidden=true;{{(persist && !activate ? "document.querySelector('input[name=selectOne]')?.closest('tr.slot')?.classList.add('disabled');" : "")}}})">Deactivate target</button>
                    <button data-action="activate" onclick="fetch('/save').then(()=>{document.querySelector('#slot-menu-637157').hidden=true;{{(persist && activate ? "document.querySelector('input[name=selectOne]')?.closest('tr.slot')?.classList.remove('disabled');" : "")}}})">Activate target</button>
                    </div></td></tr></tbody></table></div></div>
                    <div id="dialogContent"></div><template id="target-map">{{map}}</template>
                    """;
                body = $"""
                    <div id="sidebarBoxVillageList">
                    <div class="listEntry village {(currentVillage == 1 ? "active" : "")}" data-did="1"><span class="name">0</span></div>
                    <div class="listEntry village {(currentVillage == 2 ? "active" : "")}" data-did="2"><a href="#" onclick="location.href='/switch'"><span class="name">7</span></a></div>
                    </div>
                    """ + body.Replace(
                        "<div id=\"rallyPointFarmList\">",
                        """<div id="rallyPointFarmList"><div class="villageWrapper"><div class="villageHeader collapsed"><div class="villageName">7</div><a class="expandCollapse" onclick="this.parentElement.classList.replace('collapsed', 'expanded')"><svg width="12" height="8" viewBox="0 0 12 8"><path d="M0 0L6 8L12 0Z"></path></svg></a></div>""") + "</div>";
                if (url.AbsolutePath == "/switch")
                    body = body[..body.IndexOf("<div id=\"rallyPointFarmList\">", StringComparison.Ordinal)];
                await route.FulfillAsync(new() { ContentType = "text/html", Body = body });
            });
            await page.GotoAsync("https://example.com/farm");
            var browser = Substitute.For<IChromeBrowser>();
            var logger = Substitute.For<Serilog.ILogger>();
            IPage currentPage = page;
            browser.CurrentPage.Returns(_ => currentPage);
            browser.Logger.Returns(logger);
            browser.Click(Arg.Any<ILocator>(), true, Arg.Any<string>()).Returns(async call =>
            {
                var href = await ((ILocator)call[0]).GetAttributeAsync("href");
                currentPage = await context.NewPageAsync();
                await currentPage.GotoAsync(new Uri(new Uri(page.Url), href!).AbsoluteUri);
                targetNavigations++;
                events.Add("check-target");
                events.Add("click-buffer");
                return Result.Ok();
            });
            browser.DelayClick().Returns(_ =>
            {
                events.Add("buffer");
                return Task.CompletedTask;
            });
            browser.CloseNewTab().Returns(async _ =>
            {
                events.Add("close-check-tab");
                if (currentPage != page) await currentPage.CloseAsync();
                currentPage = page;
                return Result.Ok();
            });
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = (ILocator)call[0];
                var action = await locator.GetAttributeAsync("data-action");
                if (action is "deactivate" or "activate")
                    events.Add(action);
                await locator.ClickAsync();
                await currentPage.WaitForLoadStateAsync();
                return Result.Ok();
            });
            browser.Navigate(Arg.Any<string>()).Returns(async call =>
            {
                var target = new Uri((string)call[0]);
                if (currentPage == page) originNavigations++;
                else if (target.AbsolutePath == "/farm") newTabFarmNavigations++;
                else if (target.AbsolutePath == "/karte.php")
                {
                    targetNavigations++;
                    events.Add("check-target");
                }
                await currentPage.GotoAsync(target.AbsoluteUri);
                return Result.Ok();
            });
            browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await ((ILocator)call[0]).WaitForAsync();
                return Result.Ok();
            });

            var allowedAnimals = synchronize || activate || troopCount == "1"
                ? new HashSet<int> { 31 }
                : new HashSet<int>();
            (await new FarmTargetTroopsScanner(browser).Scan(
                new FarmId(6706), activate, allowedAnimals,
                1, CancellationToken.None, synchronize)).IsSuccess.ShouldBeTrue();

            saves.ShouldBe(expectedSaves);
            disabled.ShouldBe(expectedSaves == 1 && persist ? !activate : initiallyDisabled);
            new Uri(page.Url).AbsolutePath.ShouldBe("/farm");
            currentVillage.ShouldBe(2);
            switches.ShouldBe(1);
            originNavigations.ShouldBe(1);
            targetNavigations.ShouldBe(synchronize || initiallyDisabled == activate ? 1 : 0);
            newTabFarmNavigations.ShouldBe(0);
            if (initiallyDisabled == activate)
            {
                events.IndexOf("check-target").ShouldBeLessThan(events.IndexOf("close-check-tab"));
                events.Where(x => x is "check-target" or "click-buffer" or "buffer" or "close-check-tab")
                    .ShouldBe(new[] { "check-target", "click-buffer", "buffer", "close-check-tab" });
                (await page.Locator("#dialogContent").InnerHTMLAsync()).ShouldBeEmpty();
            }
            var actionName = activate ? "activate" : "deactivate";
            if (expectedSaves == 1 && !persist)
                logger.Received().Warning("Farm target {SlotId}: saved status change was not confirmed", "637157");
            if (expectedSaves == 1)
            {
                events.IndexOf("check-target").ShouldBeLessThan(events.IndexOf("close-check-tab"));
                events.IndexOf("close-check-tab").ShouldBeLessThan(events.IndexOf(actionName));
            }
        }

    }
}
