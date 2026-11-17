using MainCore.Entities;
using MainCore.Enums;
using MainCore.Infrasturecture.Persistence;
using MainCore.Models;
using MainCore.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class ChromeBrowserInputTest
    {
        [Fact]
        public async Task WaitsForConfiguredDelayBetweenTroopFields()
        {
            const int delay = 200;
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
            var profilePath = $"input-test-{Guid.NewGuid():N}";
            var cachePath = Path.Combine(AppContext.BaseDirectory, "Data", "Cache", profilePath);
            try
            {
                await browser.Setup(new ChromeSetting { ProfilePath = profilePath, IsHeadless = true });
                var page = browser.CurrentPage;
                await page.SetContentAsync("""
                    <input name="t4"><input name="t5"><input name="t6">
                    <script>
                    window.events = [];
                    for (const input of document.querySelectorAll('input')) {
                        for (const type of ['focus', 'input']) {
                            input.addEventListener(type, () =>
                                window.events.push({name: input.name, type, time: performance.now()}));
                        }
                    }
                    </script>
                    """);
                foreach (var field in new[] { "t4", "t5", "t6" })
                {
                    var result = await browser.Input(page.Locator($"input[name='{field}']"), "12");
                    result.IsSuccess.ShouldBeTrue();
                    (await page.Locator($"input[name='{field}']").InputValueAsync()).ShouldBe("12");
                }
                var gaps = await page.EvaluateAsync<double[]>("""
                    () => ['t5', 't6'].map((name, i) => {
                        const previous = events.filter(e => e.name === ['t4', 't5'][i] && e.type === 'input').at(-1);
                        return events.find(e => e.name === name && e.type === 'focus').time - previous.time;
                    })
                    """);
                gaps.Length.ShouldBe(2);
                foreach (var gap in gaps)
                    gap.ShouldBeGreaterThanOrEqualTo(delay);
            }
            finally
            {
                await browser.Shutdown();
                if (Directory.Exists(cachePath)) Directory.Delete(cachePath, recursive: true);
            }
        }
    }
}
