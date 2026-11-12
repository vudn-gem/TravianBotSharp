using FluentResults;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class ClearOasisScannerTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(4, false, true, true, 1, 1)]
        [InlineData(5, true, true, false, 0, 2)]
        [InlineData(0, true, true, false, 0, 2)]
        [InlineData(4, false, false, false, 1, 1)]
        [InlineData(5, false, true, true, 1, 1)]
        [InlineData(0, false, true, true, 1, 1)]
        [InlineData(5, false, false, false, 1, 1)]
        public async Task DeactivatesTargetsWithExcessOrDisallowedAnimalsAndContinues(
            int limit, bool allowAll, bool persist, bool expectedDisabled, int expectedSaves, int expectedRaids)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var disabled = false;
            var saves = 0;
            var raids = 0;
            var logger = Substitute.For<Serilog.ILogger>();
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var url = new Uri(route.Request.Url);
                if (url.AbsolutePath == "/save")
                {
                    saves++;
                    if (persist) disabled = true;
                    await route.FulfillAsync(new() { Body = "ok" });
                    return;
                }
                var body = url.Query.Contains("tt=2", StringComparison.Ordinal) ? """
                    <div id="build"><div class="a2b"><form action="/build.php?gid=16&amp;tt=2">
                    <table id="troops"><tbody><tr>
                    <td><img class="unit u64"><input name="troop[t4]"><a href="#">20</a></td>
                    <td><img class="unit u65"><input name="troop[t5]"><a href="#">20</a></td>
                    <td><img class="unit u66"><input name="troop[t6]"><a href="#">20</a></td>
                    </tr></tbody></table><button id="ok" type="submit"
                    onclick="this.closest('form').remove();return false;">Send</button>
                    </form></div></div>
                    """ : $"""
                    <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="2">
                    <span class="name">Village</span></div></div>
                    <div id="rallyPointFarmList"><div class="villageWrapper">
                    <div class="villageHeader"><div class="villageName">Village</div></div>
                    <div class="farmListWrapper expanded"><div class="farmListHeader"><div data-list="6706"></div></div>
                    <table class="slots"><tbody>
                    <tr class="slot {(disabled ? "disabled" : "")}"><td><input name="selectOne" data-slot-id="1"></td>
                    <td class="target"><a href="/karte.php?x=25&amp;y=184"
                    onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#map1').innerHTML;return false;">First</a></td></tr>
                    <tr class="slot"><td><input name="selectOne" data-slot-id="2"></td>
                    <td class="target"><a href="/karte.php?x=26&amp;y=184"
                    onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#map2').innerHTML;return false;">Second</a></td></tr>
                    </tbody></table></div></div></div>
                    <div id="dialogContent"></div>
                    <template id="map1"><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=25&amp;y=184">Center</a>
                    <div id="map_details"><table id="troop_info"><tbody>
                    <tr><td><img class="unit u31"></td><td class="val">2</td></tr>
                    <tr><td><img class="unit u32"></td><td class="val">3</td></tr>
                    </tbody></table></div>
                    <div class="detailImage"><div class="options"><div class="option">
                    <a href="/build.php?gid=16&amp;tt=2&amp;eventType=4">Raid</a>
                    </div></div></div>
                    <div class="options"><a onclick="/* SLOT_DIALOG_TYPE_EDIT, listId: 6706, editSlotIds: [1] */ document.querySelector('#edit').hidden=false;document.querySelector('#dialogContent').innerHTML=''">Edit farm list</a></div>
                    </div></template>
                    <template id="map2"><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=26&amp;y=184">Center</a>
                    <div id="map_details"><table id="troop_info"><tbody><tr><td>none</td></tr></tbody></table></div>
                    <div class="detailImage"><div class="options"><div class="option">
                    <a href="/build.php?gid=16&amp;tt=2&amp;eventType=4">Raid</a></div></div></div>
                    </div></template>
                    <div id="edit" hidden><div class="contentV2"><h3>Edit target</h3><div id="farmListTargetForm">
                    <select name="listId"><option value="6706">list</option></select>
                    <input name="x" value="25"><input name="y" value="184">
                    <div class="deactivateSlot"><input name="isActive" type="checkbox"></div>
                    <div class="actionButtons"><button class="save" type="submit"
                    onclick="fetch('/save').then(()=>document.querySelector('#farmListTargetForm').remove())">Save</button></div>
                    </div></div></div>
                    """;
                await route.FulfillAsync(new() { ContentType = "text/html", Body = body });
            });
            await page.GotoAsync("https://example.com/build.php?gid=16&tt=4");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(logger);
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = (ILocator)call[0];
                if (await locator.GetAttributeAsync("id") == "ok") raids++;
                await locator.ClickAsync();
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
            var allowed = allowAll ? new HashSet<int> { 31, 32 } : new HashSet<int> { 31 };
            var settings = new OasisRaidSettings(allowed, 2, OasisAttackTroop.SteppeRider, 2, 0, 0, limit);
            var result = await new ClearOasisScanner(browser).Scan(new FarmId(6706), settings, 0, CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            disabled.ShouldBe(expectedDisabled);
            saves.ShouldBe(expectedSaves);
            raids.ShouldBe(expectedRaids);
            result.Value.RaidCount.ShouldBe(expectedRaids);
            if (expectedSaves == 1 && !persist)
                logger.Received().Warning("Clear oasis target {SlotId}: deactivation was not confirmed", "1");
            if (expectedDisabled)
                logger.Received().Information("Clear oasis target {SlotId}: deactivation confirmed", "1");
        }

        [Theory]
        [InlineData(OasisAttackTroop.SteppeRider, true, 1, 1, 1, 10, true, 6, 0, 0)]
        [InlineData(OasisAttackTroop.Marksman, true, 1, 1, 1, 10, true, 0, 6, 0)]
        [InlineData(OasisAttackTroop.Marauder, true, 1, 1, 1, 10, true, 0, 0, 6)]
        [InlineData(OasisAttackTroop.Marksman, false, 2, 0, 0, 10, true, 2, 0, 0)]
        [InlineData(OasisAttackTroop.Marksman, false, 1, 1, 0, 10, true, 1, 1, 0)]
        [InlineData(OasisAttackTroop.Marksman, false, 1, 1, 1, 10, true, 1, 1, 1)]
        [InlineData(OasisAttackTroop.Marksman, false, 0, 2, 0, 0, true, 0, 2, 0)]
        [InlineData(OasisAttackTroop.Marksman, false, 2, 0, 0, 0, false, 0, 0, 0)]
        public async Task SendsConfiguredTroopsForOasis(
            OasisAttackTroop attackTroop, bool hasAnimals, int raidSteppe, int raidMarksmen, int raidMarauders,
            int availableSteppe, bool sends, int steppe, int marksmen, int marauders)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var submitted = new List<(string? Steppe, string? Marksmen, string? Marauders)>();
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var url = new Uri(route.Request.Url);
                var body = url.Query.Contains("tt=2", StringComparison.Ordinal) ? $"""
                    <div id="build"><div class="a2b"><form action="/build.php?gid=16&amp;tt=2">
                    <table id="troops"><tbody><tr>
                    <td><img class="unit u64"><input name="troop[t4]" {(availableSteppe == 0 ? "disabled" : "")}>
                    {(availableSteppe == 0 ? "<span class='none'>0</span>" : "<a href='#'>10</a>")}</td>
                    <td><img class="unit u65"><input name="troop[t5]"><a href="#">10</a></td>
                    <td><img class="unit u66"><input name="troop[t6]"><a href="#">10</a></td>
                    </tr></tbody></table><button id="ok" type="submit"
                    onclick="this.closest('form').remove(); return false;">Send</button>
                    </form></div></div>
                    """ : $"""
                    <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="2">
                    <span class="name">Village</span></div></div>
                    <div id="rallyPointFarmList"><div class="villageWrapper">
                    <div class="villageHeader"><div class="villageName">Village</div></div>
                    <div class="farmListWrapper expanded"><div class="farmListHeader"><div data-list="6706"></div></div>
                    <table class="slots"><tbody><tr class="slot"><td><input name="selectOne" data-slot-id="1"></td>
                    <td class="target"><a href="/karte.php?x=25&amp;y=184"
                    onclick="document.querySelector('#dialogContent').hidden=false;return false;">Oasis</a></td>
                    </tr></tbody></table></div></div></div>
                    <div id="dialogContent" hidden><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=25&amp;y=184">Center</a>
                    <div id="map_details"><table id="troop_info"><tbody>
                    {(hasAnimals ? "<tr><td><img class='unit u31'></td><td class='val'>3</td></tr>" : "<tr><td>none</td></tr>")}
                    </tbody></table></div>
                    <div class="detailImage"><div class="options"><div class="option">
                    <a href="/build.php?gid=16&amp;tt=2&amp;eventType=4">Raid</a>
                    </div></div></div></div></div>
                    """;
                await route.FulfillAsync(new() { ContentType = "text/html", Body = body });
            });
            await page.GotoAsync("https://example.com/build.php?gid=16&tt=4");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = (ILocator)call[0];
                if (await locator.GetAttributeAsync("id") == "ok")
                {
                    submitted.Add((
                        await page.Locator("input[name='troop[t4]']").InputValueAsync(),
                        await page.Locator("input[name='troop[t5]']").InputValueAsync(),
                        await page.Locator("input[name='troop[t6]']").InputValueAsync()));
                }
                await locator.ClickAsync();
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
            var settings = new OasisRaidSettings(new HashSet<int> { 31 }, 2, attackTroop, raidSteppe, raidMarksmen, raidMarauders);
            var result = await new ClearOasisScanner(browser).Scan(new FarmId(6706), settings, 0, CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            result.Value.RaidCount.ShouldBe(sends ? 1 : 0);
            if (!sends)
            {
                submitted.ShouldBeEmpty();
                result.Value.RetryAt.ShouldNotBeNull();
                return;
            }
            submitted.ShouldHaveSingleItem();
            submitted[0].ShouldBe((steppe.ToString(), marksmen.ToString(), marauders.ToString()));
        }

        [Theory]
        [InlineData(5, 0, 4, 0, 2, 0, OasisAttackTroop.SteppeRider, 1, -1)]
        [InlineData(5, 1, 4, 0, 2, 0, OasisAttackTroop.SteppeRider, 1, -1)]
        [InlineData(1, 0, 4, 0, 2, 0, OasisAttackTroop.SteppeRider, 2, -1)]
        [InlineData(0, 5, 3, 0, 2, 0, OasisAttackTroop.SteppeRider, 1, 1)]
        [InlineData(1, 0, 4, 1, 1, 2, OasisAttackTroop.SteppeRider, 0, 0)]
        [InlineData(5, 0, 2, 0, 2, 0, OasisAttackTroop.Marksman, 1, -1)]
        public async Task SkipsExpensiveTargetsAndRetriesOnlyWhenEmptyRaidCompositionIsUnavailable(
            int firstAnimals, int secondAnimals, int steppe, int marksmen, int raidSteppe, int raidMarksmen,
            OasisAttackTroop attackTroop, int expectedRaids, int retryIndex)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var submitted = new List<(int Steppe, int Marksmen)>();
            var visitedTargets = new List<string>();
            var returnAt = DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var url = new Uri(route.Request.Url);
                var body = url.Query.Contains("tt=2", StringComparison.Ordinal) ? $"""
                    <div id="build"><div class="a2b"><form action="/build.php?gid=16&amp;tt=2">
                    <table id="troops"><tbody><tr>
                    <td><img class="unit u64"><input name="troop[t4]"><a href="#">{steppe}</a></td>
                    <td><img class="unit u65"><input name="troop[t5]"><a href="#">{marksmen}</a></td>
                    <td><img class="unit u66"><input name="troop[t6]"><a href="#">0</a></td>
                    </tr></tbody></table><button id="ok" type="submit"
                    onclick="document.querySelector('#build').innerHTML = document.querySelector('#confirmation').innerHTML; return false;">Send</button>
                    </form></div></div>
                    <template id="confirmation"><div id="troopSendForm"><span id="at" class="timer" value="{returnAt}"></span>
                    <span id="in" class="in">00:01:00</span></div></template>
                    """ : $"""
                    <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="2">
                    <span class="name">Village</span></div></div>
                    <div id="rallyPointFarmList"><div class="villageWrapper">
                    <div class="villageHeader"><div class="villageName">Village</div></div>
                    <div class="farmListWrapper expanded"><div class="farmListHeader"><div data-list="6706"></div></div>
                    <table class="slots"><tbody>
                    <tr class="slot"><td><input name="selectOne" data-slot-id="1"></td>
                    <td class="target"><a href="/karte.php?x=25&amp;y=184"
                    onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#map1').innerHTML;return false;">First</a></td></tr>
                    <tr class="slot"><td><input name="selectOne" data-slot-id="2"></td>
                    <td class="target"><a href="/karte.php?x=26&amp;y=184"
                    onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#map2').innerHTML;return false;">Second</a></td></tr>
                    </tbody></table></div></div></div>
                    <div id="dialogContent"></div>
                    <template id="map1"><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=25&amp;y=184">Center</a>
                    <div id="map_details"><table id="troop_info"><tbody>
                    {(firstAnimals > 0 ? $"<tr><td><img class='unit u31'></td><td class='val'>{firstAnimals}</td></tr>" : "<tr><td>none</td></tr>")}
                    </tbody></table></div>
                    <div class="detailImage"><div class="options"><div class="option">
                    <a href="/build.php?gid=16&amp;tt=2&amp;eventType=4">Raid</a></div></div></div></div></template>
                    <template id="map2"><div id="tileDetails" class="oasis">
                    <a data-centermap href="/karte.php?x=26&amp;y=184">Center</a>
                    <div id="map_details"><table id="troop_info"><tbody>
                    {(secondAnimals > 0 ? $"<tr><td><img class='unit u31'></td><td class='val'>{secondAnimals}</td></tr>" : "<tr><td>none</td></tr>")}
                    </tbody></table></div>
                    <div class="detailImage"><div class="options"><div class="option">
                    <a href="/build.php?gid=16&amp;tt=2&amp;eventType=4">Raid</a></div></div></div></div></template>
                    """;
                await route.FulfillAsync(new() { ContentType = "text/html", Body = body });
            });
            await page.GotoAsync("https://example.com/build.php?gid=16&tt=4");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = (ILocator)call[0];
                var href = await locator.GetAttributeAsync("href");
                if (href?.StartsWith("/karte.php?", StringComparison.Ordinal) == true)
                    visitedTargets.Add(href);
                if (await locator.GetAttributeAsync("id") == "ok")
                {
                    var sentSteppe = int.Parse(await page.Locator("input[name='troop[t4]']").InputValueAsync());
                    var sentMarksmen = int.Parse(await page.Locator("input[name='troop[t5]']").InputValueAsync());
                    submitted.Add((sentSteppe, sentMarksmen));
                    steppe -= sentSteppe;
                    marksmen -= sentMarksmen;
                }
                await locator.ClickAsync();
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
            var settings = new OasisRaidSettings(new HashSet<int> { 31 }, 2, attackTroop, raidSteppe, raidMarksmen, 0, 5);
            var result = await new ClearOasisScanner(browser).Scan(new FarmId(6706), settings, 0, CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            result.Value.RaidCount.ShouldBe(expectedRaids);
            submitted.Count.ShouldBe(expectedRaids);
            visitedTargets.ShouldBe(retryIndex == 0
                ? new[] { "/karte.php?x=25&y=184" }
                : new[] { "/karte.php?x=25&y=184", "/karte.php?x=26&y=184" });
            if (retryIndex < 0)
                result.Value.RetryAt.ShouldBeNull();
            else
            {
                result.Value.NextTargetIndex.ShouldBe(retryIndex);
                result.Value.RetryAt.ShouldNotBeNull();
                if (expectedRaids > 0)
                    result.Value.RetryAt.Value.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(returnAt).LocalDateTime.AddMinutes(1));
            }
        }
    }
}
