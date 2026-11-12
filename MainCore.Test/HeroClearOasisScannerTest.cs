using FluentResults;
using MainCore.Entities;
using MainCore.Errors;
using MainCore.Parsers;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class HeroClearOasisScannerTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(90, 61, 50, 29, 0, 0, true)]
        [InlineData(90, 61, 61, 29, 0, 0, true)]
        [InlineData(90, 61, 62, 29, 0, 0, false)]
        [InlineData(90, 61, 50, 28, 0, 0, false)]
        [InlineData(90, 61, 50, 29, 1, 0, false)]
        [InlineData(90, 61, 50, 29, 1, 1, true)]
        [InlineData(90, 90, 90, 0, 0, 0, true)]
        [InlineData(90, 0, 0, 100, 0, 0, false)]
        [InlineData(49, 30, 50, 40, 0, 0, false)]
        public async Task SimulatesAndSendsOnlyWithinLimits(
            int health, int after, int minimum, int maxHealthLost, int lost, int maxTroopLost, bool sends)
        {
            var outcome = await Run(health, after, minimum, maxHealthLost, lost, maxTroopLost);
            outcome.Confirms.ShouldBe(sends ? 1 : 0, outcome.Details);
            if (health < minimum)
            {
                outcome.Simulates.ShouldBe(0);
                outcome.Result.HasError<Skip>().ShouldBeTrue();
            }
            else outcome.Simulates.ShouldBe(1);
            if (sends)
            {
                outcome.Result.IsSuccess.ShouldBeTrue();
                outcome.Task.ProcessedSlots.ShouldContain("1");
                outcome.Task.HeroReturnAt.ShouldNotBeNull();
                outcome.Task.ExecuteAt.ShouldBe(outcome.Task.HeroReturnAt.Value.AddSeconds(5));
                outcome.Task.ConfirmationAttempted.ShouldBeFalse();
                var previous = outcome.Task.ExecuteAt;
                await outcome.Context.Pages[0].GotoAsync("https://example.com/build.php?gid=16&tt=99");
                var resumed = await outcome.Scanner.Scan(outcome.Task, outcome.Settings, CancellationToken.None);
                resumed.IsSuccess.ShouldBeTrue();
                outcome.Task.ExecuteAt.ShouldBe(previous);
            }
            await outcome.Context.DisposeAsync();
        }

        [Theory]
        [InlineData(false, 1, 0)]
        [InlineData(true, 0, 0)]
        public async Task DoesNotSendWithoutHeroOrEnoughSubTroops(bool hero, int availableMarauders, int expected)
        {
            var outcome = await Run(90, 61, 50, 40, 0, 0, hero, availableMarauders);
            outcome.Confirms.ShouldBe(expected);
            if (!hero) outcome.Result.HasError<Skip>().ShouldBeTrue();
            await outcome.Context.DisposeAsync();
        }

        [Fact]
        public async Task SwitchesVillageAndSkipsRejectedTargetBeforeSendingNext()
        {
            var outcome = await Run(90, 61, 50, 40, 0, 0, switchVillage: true, skipFirst: true);
            outcome.Confirms.ShouldBe(1, outcome.Details);
            outcome.Simulates.ShouldBe(1);
            outcome.Task.ProcessedSlots.ShouldContain("0");
            outcome.Task.ProcessedSlots.ShouldContain("1");
            await outcome.Context.DisposeAsync();
        }

        [Fact]
        public async Task RejectsArmyReducedByGameAtConfirmation()
        {
            var outcome = await Run(90, 61, 50, 40, 0, 0, reducedConfirmation: true);
            outcome.Confirms.ShouldBe(0);
            outcome.Result.HasError<Stop>().ShouldBeTrue();
            outcome.Task.HeroReturnAt.ShouldBeNull();
            await outcome.Context.DisposeAsync();
        }

        [Fact]
        public async Task RetainsUncertainConfirmationGuardWhenOverviewDoesNotOpen()
        {
            var outcome = await Run(90, 61, 50, 40, 0, 0, overview: false);
            outcome.Confirms.ShouldBe(1);
            outcome.Task.ConfirmationAttempted.ShouldBeTrue();
            outcome.Task.ProcessedSlots.ShouldBeEmpty();
            await outcome.Context.DisposeAsync();
        }

        private sealed record Outcome(
            Result Result, HeroClearFarmOasisTask.Task Task, HeroOasisSetting Settings,
            HeroClearOasisScanner Scanner, IBrowserContext Context, int Confirms, int Simulates, string Details);

        private async Task<Outcome> Run(
            int health, int after, int minimum, int maxHealthLost, int lost, int maxTroopLost,
            bool hero = true, int availableMarauders = 10, bool reducedConfirmation = false, bool overview = true,
            bool switchVillage = false, bool skipFirst = false)
        {
            var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(1500);
            var confirms = 0;
            var simulates = 0;
            var settings = new HeroOasisSetting
            {
                FarmId = 10, AllowedAnimals = 1, MinHeroHealth = minimum,
                MaxHeroHealthLost = maxHealthLost, MaxSubTroopLost = maxTroopLost,
            };
            var army = HeroOasisSettings.Troops(settings);
            var simulationInputs = string.Concat(Enumerable.Range(1, 10).Select(i =>
                $"<td><input data-t='t{i}' name='unit{i}' value='9'></td>"));
            var defenderIcons = string.Concat(Enumerable.Range(31, 10).Select(i => $"<td><i class='unit u{i}'></i></td>"));
            var defenderInputs = string.Concat(Enumerable.Range(1, 10).Select(i =>
                $"<td><input data-t='t{i}' value='{(i == 1 ? 8 : 0)}'></td>"));
            var initial = string.Concat(army.Select((count, i) => $"<td class='unit{i + 1}'>{count}</td>"));
            var loss = string.Concat(army.Select((_, i) => $"<td class='unit{i + 1}'>{(i == 3 ? $"\u202d\u2212{lost}\u202c" : "0")}</td>"));
            var remaining = string.Concat(army.Select((count, i) => $"<td class='unit{i + 1}'>{count - (i == 3 ? lost : 0)}</td>"));
            var sendAction = HiddenArmy("troop", army);
            var confirmArmy = army.ToArray();
            if (reducedConfirmation) confirmArmy[10] = 0;
            var returnUnix = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            var switched = !switchVillage;
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (uri.AbsolutePath == "/dorf1.php") switched = true;
                var villages = $"""
                    <div id="sidebarBoxVillageList">
                    <div class="listEntry village {(switched ? "active" : "")}" data-did="2" onclick="location.href='/dorf1.php?newdid=2'"><span class="name">Home</span></div>
                    <div class="listEntry village {(switched ? "" : "active")}" data-did="3"><span class="name">Away</span></div>
                    </div>
                    """;
                string body;
                if (HeroOasisParser.Query(uri, "stage") == "confirm")
                    body = $"""
                        <form id="troopSendForm">
                        {HiddenArmy("troops[0]", confirmArmy)}
                        <input name="troops[0][villageId]" value="2">
                        <input name="eventType" value="4"><input name="x" value="-125"><input name="y" value="-6">
                        <input name="redeployHero" value="">
                        <table id="short_info"><tr><td><a href="/karte.php?x=-125&amp;y=-6">Target</a></td></tr></table>
                        <span id="at" class="timer" value="{returnUnix}"></span><div id="in" class="in">In 1:00:00 hours</div>
                        <button id="confirmSendTroops" type="button" onclick="{(overview ? "location.href='/build.php?gid=16&tt=1'" : "this.disabled=true")}">Confirm</button>
                        </form>
                        """;
                else if (HeroOasisParser.Query(uri, "tt") == "1")
                    body = "<div class='subNavi'><a class='tabItem active' href='/build.php?tt=1'>Overview</a></div>";
                else if (HeroOasisParser.Query(uri, "tt") == "2")
                    body = $"""
                        <div id="build"><div class="a2b"><form action="/build.php?gid=16&amp;tt=2">
                        <table id="troops"><tbody><tr>
                        {string.Concat(Enumerable.Range(1, 11).Select(i => $"""
                            <td><img class="unit u{(i == 11 ? "hero" : (60 + i).ToString())}">
                            <input name="troop[t{i}]" value=""><a>{(i == 11 ? (hero ? 1 : 0) : i == 6 ? availableMarauders : 10)}</a></td>
                            """))}
                        </tr></tbody></table>
                        <input name="x" value="-125"><input name="y" value="-6">
                        <input name="eventType" type="radio" value="4">
                        <input name="redeployHero" type="checkbox">
                        <button id="ok" type="button" onclick="location.href='/build.php?tt=2&stage=confirm'">Send</button>
                        </form></div></div>
                        """;
                else if (HeroOasisParser.Query(uri, "screen") == "combatSimulator")
                    body = $"""
                        <div id="combatSimulator"><form id="combatSimulatorForm">
                        <div class="role attacker">
                        <div class="villageSelection"><select name="village"><option value="0">None</option><option value="2">Home</option></select><a class="reload">Reload</a></div>
                        <input type="radio" name="movement" value="raid">
                        <div class="troops attack"><table><tbody><tr class="troopIcons"><th></th><td><i class="u64"></i><i class="u65"></i><i class="u66"></i></td></tr>
                        <tr class="troopUnits"><th></th>{simulationInputs}<td><input type="checkbox" name="hero"></td></tr>
                        <tr class="troopLevels"><td><input name="unit4" value="20"></td></tr></tbody></table></div>
                        <div class="heroAttributes"><input name="health" value="{health}"></div></div>
                        <div class="role defender"><div class="troops defence"><table><tbody>
                        <tr class="troopIcons"><th></th>{defenderIcons}</tr><tr class="troopUnits"><th></th>{defenderInputs}</tr>
                        </tbody></table></div></div></form>
                        <button id="simulate" onclick="document.querySelector('.results').innerHTML=document.querySelector('#resultTemplate').innerHTML">Simulate</button>
                        <div class="results"></div>
                        <template id="resultTemplate"><div class="result attack"><table>
                        <tr class="troopsInitial">{initial}</tr><tr class="troopsLoss">{loss}</tr><tr class="troopsResult">{remaining}</tr>
                        <tr class="combatReportLine"><td><i class="hero_health"></i><span>Hero's health lowered from {health} to {after}</span></td></tr></table></div>
                        <div class="resultActionWrapper"><form method="post" action="/build.php?tt=2&amp;newdid=2">
                        {sendAction}<input name="eventType" value="4"><input name="x" value="-125"><input name="y" value="-6">
                        <button id="sendTroops">Send troops</button></form></div></template></div>
                        """;
                else
                    body = $"""
                        <div id="rallyPointFarmList"><div class="villageWrapper"><div class="villageHeader"><div class="villageName">Home</div></div>
                        <div class="farmListWrapper expanded"><div class="farmListHeader"><div data-list="10"></div></div>
                        <table class="slots"><tbody>
                        {(skipFirst ? """
                            <tr class="slot"><td><input name="selectOne" data-slot-id="0"></td>
                            <td class="target"><a href="/karte.php?x=-126&amp;y=-6" onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#skipTemplate').innerHTML;return false">Too many animals</a></td></tr>
                            """ : "")}
                        <tr class="slot"><td><input name="selectOne" data-slot-id="1"></td>
                        <td class="target"><a href="/karte.php?x=-125&amp;y=-6" onclick="document.querySelector('#dialogContent').innerHTML=document.querySelector('#tileTemplate').innerHTML;return false">Oasis</a></td>
                        </tr></tbody></table></div></div></div><div id="dialogContent"></div>
                        <template id="tileTemplate"><div id="tileDetails" class="oasis">
                        <a data-centermap href="/karte.php?x=-125&amp;y=-6">Center</a>
                        <a href="/build.php?tt=2&amp;targetMapId=82682">Raid</a>
                        <div id="map_details"><table id="troop_info"><tbody>
                        <tr><td><img class="unit u31"></td><td class="val">8</td></tr>
                        <tr><td><a href="/build.php?tt=3&amp;screen=combatSimulator&amp;kid=82682">simulate raid</a></td></tr>
                        </tbody></table></div></div></template>
                        <template id="skipTemplate"><div id="tileDetails" class="oasis">
                        <a data-centermap href="/karte.php?x=-126&amp;y=-6">Center</a>
                        <div id="map_details"><table id="troop_info"><tbody><tr><td><img class="unit u31"></td><td class="val">200</td></tr></tbody></table></div>
                        </div></template>
                        """;
                await route.FulfillAsync(new() { ContentType = "text/html; charset=utf-8", Body = villages + body });
            });
            await page.GotoAsync("https://example.com/build.php?gid=16&tt=99");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            var logger = Substitute.For<Serilog.ILogger>();
            browser.Logger.Returns(logger);
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = (ILocator)call[0];
                var id = await locator.GetAttributeAsync("id");
                if (id == "simulate")
                {
                    simulates++;
                    var attacker = page.Locator("#combatSimulatorForm .role.attacker");
                    (await attacker.Locator("input[name='hero']").IsCheckedAsync()).ShouldBeTrue();
                    (await attacker.Locator("input[name='movement'][value='raid']").IsCheckedAsync()).ShouldBeTrue();
                    (await attacker.Locator(".troopLevels input[name='unit4']").InputValueAsync()).ShouldBe("20");
                    for (var i = 1; i <= 10; i++)
                        (await attacker.Locator($".troopUnits input[data-t='t{i}']").InputValueAsync())
                            .ShouldBe(army[i - 1] == 0 ? "" : army[i - 1].ToString());
                }
                if (id == "ok")
                {
                    for (var i = 1; i <= 11; i++)
                        (await page.Locator($"#troops input[name='troop[t{i}]']").InputValueAsync())
                            .ShouldBe(army[i - 1] == 0 ? "" : army[i - 1].ToString());
                }
                if (id == "confirmSendTroops") confirms++;
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
            var scanner = new HeroClearOasisScanner(browser);
            var task = new HeroClearFarmOasisTask.Task(new AccountId(1), new FarmId(10));
            Result result;
            try
            {
                result = await scanner.Scan(task, settings, CancellationToken.None);
            }
            catch (TimeoutException) when (!overview && task.ConfirmationAttempted)
            {
                result = Stop.Error.WithError("Confirmation outcome unknown.");
            }
            var details = string.Join("; ", result.Errors.Select(x => x.Message)) + " " +
                string.Join("; ", logger.ReceivedCalls().Select(call => string.Join(" ", call.GetArguments())));
            return new(result, task, settings, scanner, context, confirms, simulates, details);
        }

        private static string HiddenArmy(string prefix, int[] army) =>
            string.Concat(army.Select((count, i) => $"<input type='hidden' name='{prefix}[t{i + 1}]' value='{count}'>"));
    }
}
