using FluentResults;
using MainCore.Entities;
using MainCore.Parsers;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class FarmListNavigationTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData("shortcut", false)]
        [InlineData("missing", true)]
        [InlineData("duplicate", true)]
        [InlineData("click-failure", true)]
        [InlineData("wrong-page", true)]
        [InlineData("wrong-village", true)]
        public async Task UsesShortcutOrFallsBackToSavedUrl(string mode, bool fallback)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var village = 2;
            const string shortcut = """
                <a data-load-tooltip="quickLink" href="/build.php?gid=16&amp;tt=99">
                <svg class="RallyPointFarmList" width="20" height="20"></svg></a>
                """;
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (HeroOasisParser.Query(uri, "newdid") == "2") village = 2;
                else if (mode == "wrong-village" && uri.AbsolutePath == "/build.php") village = 3;
                var links = mode == "missing" ? "" : mode == "duplicate" ? shortcut + shortcut :
                    mode == "wrong-page" ? shortcut.Replace("<a ", "<a onclick='return false' ") : shortcut;
                await route.FulfillAsync(new()
                {
                    ContentType = "text/html",
                    Body = $"""
                        <div id="sidebarBoxVillageList">
                        <div class="quickLinks">{links}</div>
                        <div class="listEntry village active" data-did="{village}"></div></div>
                        {(uri.AbsolutePath == "/build.php" ? "<div id='rallyPointFarmList'>Farm List</div>" : "")}
                        """
                });
            });
            await page.GotoAsync("https://example.com/dorf1.php");
            var logger = Substitute.For<Serilog.ILogger>();
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(logger);
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                if (mode == "click-failure") return Result.Fail("click failed");
                await call.Arg<ILocator>().ClickAsync();
                return Result.Ok();
            });
            browser.Navigate(Arg.Any<string>()).Returns(async call =>
            {
                await page.GotoAsync(call.Arg<string>());
                return Result.Ok();
            });
            browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await call.Arg<ILocator>().WaitForAsync();
                return Result.Ok();
            });
            const string fallbackUrl = "https://example.com/build.php?gid=16&tt=99&newdid=2";

            var result = await FarmListNavigation.Open(browser, fallbackUrl, new VillageId(2));

            result.IsSuccess.ShouldBeTrue();
            village.ShouldBe(2);
            if (fallback)
            {
                await browser.Received(1).Navigate(fallbackUrl);
                logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Warning").ShouldBeTrue();
            }
            else
            {
                await browser.DidNotReceive().Navigate(Arg.Any<string>());
                await browser.Received(1).Click(Arg.Any<ILocator>(), Arg.Any<string>());
            }
        }

        [Theory]
        [InlineData("rally", true, "tab", false)]
        [InlineData("rally", false, "shortcut", false)]
        [InlineData("other-building", true, "shortcut", false)]
        [InlineData("missing-tab", true, "shortcut", false)]
        [InlineData("duplicate-tab", true, "shortcut", false)]
        [InlineData("tab-click-failure", true, "tab", true)]
        [InlineData("tab-wrong-village", true, "tab", true)]
        public async Task PrefersRallyPointTabOnlyForRequestedFlows(
            string mode, bool preferTab, string expectedClick, bool fallback)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var village = 2;
            await page.RouteAsync("https://example.com/**", async route =>
            {
                var uri = new Uri(route.Request.Url);
                if (HeroOasisParser.Query(uri, "newdid") == "2") village = 2;
                else if (mode == "tab-wrong-village" && HeroOasisParser.Query(uri, "tt") == "99") village = 3;
                var tab = "<a class='tabItem' data-kind='tab' href='/build.php?id=39&amp;tt=99'>Farm List</a>";
                var tabs = mode == "missing-tab" ? "" : mode == "duplicate-tab" ? tab + tab : tab;
                await route.FulfillAsync(new()
                {
                    ContentType = "text/html",
                    Body = $"""
                        <div id="sidebarBoxVillageList"><div class="quickLinks">
                        <a data-kind="shortcut" data-load-tooltip="quickLink" href="/build.php?gid=16&amp;tt=99">
                        <svg class="RallyPointFarmList" width="20" height="20"></svg></a></div>
                        <div class="listEntry village active" data-did="{village}"></div></div>
                        <div id="content" class="{(mode == "other-building" ? "build" : "buildRallyPoint")}">
                        <div class="contentNavi subNavi">{tabs}</div>
                        {(HeroOasisParser.Query(uri, "tt") == "99" ? "<div id='rallyPointFarmList'>Farm List</div>" : "")}
                        </div>
                        """
                });
            });
            await page.GotoAsync("https://example.com/build.php?gid=16&tt=2");
            var browser = Substitute.For<IChromeBrowser>();
            var logger = Substitute.For<Serilog.ILogger>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(logger);
            var clicks = new List<string>();
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                var locator = call.Arg<ILocator>();
                var kind = await locator.GetAttributeAsync("data-kind");
                clicks.Add(kind!);
                if (mode == "tab-click-failure") return Result.Fail("click failed");
                await locator.ClickAsync();
                return Result.Ok();
            });
            browser.Navigate(Arg.Any<string>()).Returns(async call =>
            {
                await page.GotoAsync(call.Arg<string>());
                return Result.Ok();
            });
            browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
                await call.Arg<ILocator>().WaitForAsync();
                return Result.Ok();
            });
            const string fallbackUrl = "https://example.com/build.php?gid=16&tt=99&newdid=2";

            var result = await FarmListNavigation.Open(browser, fallbackUrl, new VillageId(2), preferTab);

            result.IsSuccess.ShouldBeTrue();
            clicks.ShouldBe([expectedClick]);
            village.ShouldBe(2);
            if (fallback)
            {
                await browser.Received(1).Navigate(fallbackUrl);
                logger.ReceivedCalls().Any(call => call.GetMethodInfo().Name == "Warning").ShouldBeTrue();
            }
            else await browser.DidNotReceive().Navigate(Arg.Any<string>());
        }

        [Fact]
        public async Task RejectsFallbackWhenVillageIsIncorrect()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="3"></div></div>
                <div id="rallyPointFarmList"></div>
                """);
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.Navigate(Arg.Any<string>()).Returns(Result.Ok());
            browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(Result.Ok());

            var result = await FarmListNavigation.Open(browser, "https://example.com/build.php?gid=16&tt=99", new VillageId(2));

            result.IsFailed.ShouldBeTrue();
        }
    }
}
