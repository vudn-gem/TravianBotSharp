using FluentResults;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MainCore.Entities;
using MainCore.Services;
using MainCore.Test.Parsers;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class RallyPointSendTroopsTabTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData("shortcut")]
        [InlineData("fallback")]
        [InlineData("failed-click")]
        [InlineData("action-fails")]
        [InlineData("action-throws")]
        [InlineData("cancel")]
        [InlineData("close-fails")]
        public async Task RunsInSeparateTabAndRestoresOriginal(string mode)
        {
            await using var context = await CreateContextAsync();
            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var origin = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";
            using var stopping = new CancellationTokenSource();
            var serving = ServePages(server, stopping.Token);
            try
            {
                var mainPage = await context.NewPageAsync();
                await mainPage.GotoAsync($"{origin}/resources");
                if (mode == "fallback") await mainPage.Locator("a").EvaluateAsync("node => node.remove()");
                var currentPage = mainPage;
                var browser = Substitute.For<IChromeBrowser>();
                browser.CurrentPage.Returns(_ => currentPage);
                browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
                browser.Click(Arg.Any<ILocator>(), true, Arg.Any<string>()).Returns(async call =>
                {
                    if (mode == "failed-click") return Result.Fail("Shortcut click failed.");
                    currentPage = await context.RunAndWaitForPageAsync(() =>
                        call.Arg<ILocator>().ClickAsync(new() { Button = MouseButton.Middle }));
                    return Result.Ok();
                });
                browser.OpenNewTab().Returns(async _ =>
                {
                    currentPage = await context.NewPageAsync();
                    return Result.Ok();
                });
                browser.Navigate(Arg.Any<string>()).Returns(async call =>
                {
                    await currentPage.GotoAsync(call.Arg<string>());
                    return Result.Ok();
                });
                browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
                {
                    await call.Arg<ILocator>().WaitForAsync();
                    return Result.Ok();
                });
                browser.CloseNewTab().Returns(async _ =>
                {
                    await currentPage.CloseAsync();
                    currentPage = mainPage;
                    return mode == "close-fails" ? Result.Fail("Tab close failed.") : Result.Ok();
                });
                using var cts = new CancellationTokenSource();
                var inspected = false;
                async Task<Result> Action(IPage page)
                {
                    inspected = true;
                    page.ShouldNotBe(mainPage);
                    context.Pages.Count.ShouldBe(2);
                    (await RallyPointSendTroopsTab.SendForm(page).CountAsync()).ShouldBe(1);
                    if (mode == "action-throws") throw new InvalidOperationException("Action failed.");
                    if (mode == "cancel")
                    {
                        cts.Cancel();
                        cts.Token.ThrowIfCancellationRequested();
                    }
                    await Task.CompletedTask;
                    return mode == "action-fails" ? Result.Fail("Action failed.") : Result.Ok();
                }
                async Task Run()
                {
                    var result = await RallyPointSendTroopsTab.Run(browser, new VillageId(1), Action, cts.Token);
                    result.IsSuccess.ShouldBe(mode is not ("action-fails" or "close-fails"));
                    if (mode == "close-fails") result.Errors.ShouldContain(x => x.Message == "Tab close failed.");
                }
                if (mode == "action-throws") await Should.ThrowAsync<InvalidOperationException>(Run);
                else if (mode == "cancel") await Should.ThrowAsync<OperationCanceledException>(Run);
                else await Run();

                inspected.ShouldBeTrue();
                browser.CurrentPage.ShouldBe(mainPage);
                context.Pages.Count.ShouldBe(1);
                mainPage.Url.ShouldBe($"{origin}/resources");
                (await mainPage.Locator("body").GetAttributeAsync("data-clicked")).ShouldBeNull();
                await browser.Received(1).CloseNewTab();
                if (mode is "fallback" or "failed-click")
                    await browser.Received(1).Navigate($"{origin}/building?id=16&tt=2");
                else
                    await browser.DidNotReceive().Navigate(Arg.Any<string>());
            }
            finally
            {
                await stopping.CancelAsync();
                try { await serving; }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
            }
        }

        private static async Task ServePages(TcpListener server, CancellationToken cancellationToken)
        {
            while (true)
            {
                using var client = await server.AcceptTcpClientAsync(cancellationToken);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                var request = await reader.ReadLineAsync(cancellationToken);
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken))) { }
                var body = request?.StartsWith("GET /building", StringComparison.Ordinal) == true
                    ? """
                        <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="1"></div></div>
                        <div id="build"><div class="a2b"><form action="/building?tt=2"><table id="troops"><tr><td>Troops</td></tr></table></form></div></div>
                        """
                    : """
                        <div id="sidebarBoxVillageList"><div class="quickLinks">
                        <a href="/building?id=16&amp;tt=2" data-load-tooltip="quickLink"
                           onclick="document.body.dataset.clicked='true'; return false">
                        <svg class="RallyPointSendTroops" width="20" height="20"></svg></a></div></div>
                        """;
                var response = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }
}
