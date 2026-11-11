using FluentResults;
using MainCore.Parsers;
using MainCore.Entities;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class LostReportTargetScannerTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(false, false, "6706", true, true)]
        [InlineData(true, false, "6706", true, false)]
        [InlineData(false, true, "6706", true, false)]
        [InlineData(false, false, "", true, false)]
        [InlineData(false, false, "6706", false, true)]
        public async Task ProcessesLostReportsSafelyAndRestoresContext(
            bool alreadyChecked, bool disabled, string listId, bool savePersists, bool expectSave)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var village = 1;
            var saved = alreadyChecked;
            var saves = 0;
            var reportOpens = 0;
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (uri.AbsolutePath == "/save")
                {
                    saves++;
                    if (savePersists) saved = true;
                    await route.FulfillAsync(new() { Body = "ok" });
                    return;
                }
                if (uri.AbsolutePath == "/switch") village = int.Parse(uri.Query["?v=".Length..]);
                var sidebar = $"""
                    <div id="sidebarBoxVillageList">
                    <div class="listEntry village {(village == 1 ? "active" : "")}" data-did="1" onclick="location.href='/switch?v=1'"><a href="#"><span class="name">0</span></a></div>
                    <div class="listEntry village {(village == 2 ? "active" : "")}" data-did="2" onclick="location.href='/switch?v=2'"><a href="#"><span class="name">7</span></a></div>
                    </div><a class="reports" accesskey="5" href="/report">Reports</a>
                    <a class="tabItem" href="/report/offensive">Offensive</a>
                    """;
                var body = "";
                if (uri.AbsolutePath == "/report/offensive" && uri.Query.Contains("id="))
                {
                    reportOpens++;
                    var dialog = $"""
                        <div class="contentV2"><h3>Edit target</h3><div id="farmListTargetForm"><select name="listId"><option value="{listId}">list</option></select>
                        <div class="deactivateSlot"><input type="checkbox" name="isActive" {(saved ? "checked" : "")}></div>
                        <div class="actionButtons"><button class="save" type="submit" onclick="fetch('/save').then(()=>document.querySelector('#farmListTargetForm').remove())">Save</button></div></div></div>
                        """;
                    body = $"""
                        <div class="body"><div class="role attacker"><div class="outcome"><i class="attackLost"></i></div>
                        <div class="troopHeadline"><a class="village" href="/karte.php?d=77571">7</a></div></div></div>
                        <button class="{(disabled ? "disabled" : "")}" onclick="document.querySelector('#dialog').hidden=false"><i class="reportButton raidList"></i></button>
                        <div id="dialog" hidden>{dialog}</div>
                        """;
                }
                else if (uri.AbsolutePath == "/report/offensive")
                    body = """
                        <table><tbody>
                        <tr><td><input class="report" name="ids[]" value="10"></td><td class="sub newMessage"><img class="messageStatusUnread"><img class="iReport3"><div><a href="?id=10%7Ctoken">lost</a></div></td></tr>
                        <tr><td><input class="report" name="ids[]" value="11"></td><td class="sub newMessage"><img class="messageStatusUnread"><img class="iReport1"><div><a href="?id=11%7Ctoken">win</a></div></td></tr>
                        </tbody></table>
                        """;
                await route.FulfillAsync(new() { ContentType = "text/html", Body = sidebar + body });
            });
            await page.GotoAsync("https://example.com/farm");
            var browser = Substitute.For<IChromeBrowser>();
            var logger = Substitute.For<Serilog.ILogger>();
            browser.Logger.Returns(logger);
            browser.CurrentPage.Returns(page);
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

            await new LostReportTargetScanner(browser).Scan(CancellationToken.None);

            saves.ShouldBe(expectSave ? 1 : 0, string.Join("\n",
                logger.ReceivedCalls().Select(call => string.Join(" | ", call.GetArguments()))));
            saved.ShouldBe(alreadyChecked || (expectSave && savePersists));
            page.Url.ShouldBe("https://example.com/farm");
            (await VillagePanelParser.GetCurrentVillageId(page)).ShouldBe(new VillageId(1));
            reportOpens.ShouldBeGreaterThanOrEqualTo(2);
            if (expectSave && !savePersists)
                logger.Received().Warning(
                    "Lost report {ReportId} target skipped: {Reason}", "10",
                    "Target deactivation was not confirmed after reopening the dialog.");
        }
    }
}
