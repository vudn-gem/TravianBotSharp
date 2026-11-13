using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public class ChromeBrowserUserAgentTest
    {
        private const string CustomUserAgent = "TBS-User-Agent-Test/1.0";

        [Theory]
        [InlineData(CustomUserAgent)]
        [InlineData("")]
        [InlineData("  ")]
        public async Task SetupAppliesConfiguredUserAgent(string userAgent)
        {
            var profilePath = $"user-agent-test-{Guid.NewGuid():N}";
            var cachePath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache", profilePath);
            var browser = new ChromeBrowser(Substitute.For<IDbContextFactory<AppDbContext>>())
            {
                Logger = Serilog.Log.Logger
            };

            try
            {
                await browser.Setup(new ChromeSetting
                {
                    ProfilePath = profilePath,
                    UserAgent = userAgent,
                    IsHeadless = true
                });

                string? requestUserAgent = null;
                await browser.CurrentPage.RouteAsync("https://example.test/", async route =>
                {
                    var headers = await route.Request.AllHeadersAsync();
                    requestUserAgent = headers["user-agent"];
                    await route.FulfillAsync(new() { Status = 200, Body = "OK" });
                });

                await browser.CurrentPage.GotoAsync("https://example.test/");
                var navigatorUserAgent = await browser.CurrentPage.EvaluateAsync<string>("navigator.userAgent");

                requestUserAgent.ShouldBe(navigatorUserAgent);
                if (string.IsNullOrWhiteSpace(userAgent))
                {
                    navigatorUserAgent.ShouldNotBeNullOrWhiteSpace();
                    navigatorUserAgent.ShouldNotBe(CustomUserAgent);
                }
                else
                {
                    navigatorUserAgent.ShouldBe(userAgent);
                }
            }
            finally
            {
                await browser.Shutdown();
                if (Directory.Exists(cachePath))
                {
                    Directory.Delete(cachePath, recursive: true);
                }
            }
        }
    }
}
