using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentResults;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class ChromeBrowserTabTest
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public async Task BuffersTargetTabLifecycleAndRestoresMainTab(bool generic, bool failInspection, bool background)
        {
            const int delay = 150;
            using var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            using (var context = new AppDbContext(options))
            {
                await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
                context.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.test" });
                context.AccountsSetting.AddRange(
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.ClickDelayMin, Value = delay },
                    new AccountSetting { AccountId = 1, Setting = AccountSettingEnums.ClickDelayMax, Value = delay + 1 });
                await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var browser = new ChromeBrowser(factory) { Logger = Serilog.Log.Logger };
            browser.SetAccountId(new AccountId(1));
            var profilePath = $"tab-test-{Guid.NewGuid():N}";
            var cachePath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache", profilePath);
            using var server = new TcpListener(IPAddress.Loopback, 0);
            server.Start();
            var origin = $"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}";
            using var stopping = new CancellationTokenSource();
            var serving = ServePages(server, stopping.Token);
            try
            {
                await browser.Setup(new ChromeSetting { ProfilePath = profilePath, IsHeadless = true });
                var mainPage = browser.CurrentPage;
                await mainPage.GotoAsync($"{origin}/farm");
                var link = mainPage.Locator("a");
                var timer = Stopwatch.StartNew();
                (await browser.Click(link, openInNewTab: true)).IsSuccess.ShouldBeTrue();
                timer.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(delay);
                browser.CurrentPage.ShouldNotBe(mainPage);
                timer.Restart();
                (await browser.CloseNewTab()).IsSuccess.ShouldBeTrue();
                timer.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(delay);
                browser.CurrentPage.ShouldBe(mainPage);

                IPage? foregroundPage = null;
                if (background)
                {
                    foregroundPage = await mainPage.Context.NewPageAsync();
                    await foregroundPage.GotoAsync($"{origin}/other");
                    await foregroundPage.BringToFrontAsync();
                    (await foregroundPage.EvaluateAsync<bool>("document.hasFocus()")).ShouldBeTrue();
                }

                timer.Restart();
                var inspected = false;
                async Task Inspect()
                {
                    inspected = true;
                    browser.CurrentPage.ShouldNotBe(mainPage);
                    timer.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(2 * delay);
                    if (foregroundPage is not null)
                        (await foregroundPage.EvaluateAsync<bool>("document.hasFocus()")).ShouldBeTrue();
                    timer.Restart();
                    if (failInspection) throw new InvalidOperationException("Inspection failed.");
                }
                async Task Run()
                {
                    if (generic)
                    {
                        var result = await FarmTargetOasisTab.Run(browser, "/map?x=1&y=2", link, async _ =>
                        {
                            await Inspect();
                            return Result.Ok(true);
                        }, background);
                        result.IsSuccess.ShouldBeTrue();
                        result.Value.ShouldBeTrue();
                    }
                    else
                    {
                        var result = await FarmTargetOasisTab.Run(browser, "/map?x=1&y=2", link, async _ =>
                        {
                            await Inspect();
                            return Result.Ok();
                        }, background);
                        result.IsSuccess.ShouldBeTrue();
                    }
                }
                if (failInspection)
                    await Should.ThrowAsync<InvalidOperationException>(Run);
                else
                    await Run();

                inspected.ShouldBeTrue();
                timer.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(delay);
                browser.CurrentPage.ShouldBe(mainPage);
                mainPage.Context.Pages.Count.ShouldBe(background ? 2 : 1);
                mainPage.Url.ShouldBe($"{origin}/farm");
                (await mainPage.Locator("body").GetAttributeAsync("data-dialog")).ShouldBeNull();
                (await (foregroundPage ?? mainPage).EvaluateAsync<bool>("document.hasFocus()")).ShouldBeTrue();
            }
            finally
            {
                await browser.Shutdown();
                if (Directory.Exists(cachePath)) Directory.Delete(cachePath, recursive: true);
                await stopping.CancelAsync();
                try
                {
                    await serving;
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                }
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
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(cancellationToken)))
                {
                }
                var body = request?.StartsWith("GET /map", StringComparison.Ordinal) == true
                    ? """<div id="tileDetails" class="oasis">Troops</div>"""
                    : """
                      <a href="/map?x=1&y=2" onclick="document.body.dataset.dialog='opened';return false">Oasis</a>
                      """;
                var response = Encoding.UTF8.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
                await stream.WriteAsync(response, cancellationToken);
            }
        }
    }
}
