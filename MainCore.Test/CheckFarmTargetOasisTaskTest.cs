using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Commands.Navigate;
using MainCore.Entities;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MainCore.Test
{
    public sealed class CheckFarmTargetOasisTaskTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData(false, false, true, false, false, false)]
        [InlineData(true, false, true, false, false, true)]
        [InlineData(true, false, true, true, false, true)]
        [InlineData(true, false, true, false, true, true)]
        [InlineData(true, true, true, false, true, true)]
        [InlineData(true, false, false, false, true, false)]
        public async Task ActivatesOnlyAfterSuccessfulScanWhenEnabled(
            bool autoActive, bool initiallyActive, bool scanSucceeds,
            bool activate, bool synchronize, bool expectedActive)
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            using var db = new AppDbContext(options);
            db.Database.EnsureCreated();
            db.Accounts.AddRange(
                new Account { Id = 1, Username = "first", Server = "https://example.com" },
                new Account { Id = 2, Username = "second", Server = "https://example.com" });
            db.FarmLists.AddRange(
                new Farm { Id = 10, AccountId = 1, Name = "selected", IsActive = initiallyActive },
                new Farm { Id = 11, AccountId = 1, Name = "unselected" },
                new Farm { Id = 20, AccountId = 2, Name = "other account" });
            db.FarmOasisSettings.Add(new FarmOasisSetting { FarmId = 10, CheckAutoActive = autoActive });
            db.SaveChanges();
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));

            await using var browserContext = await CreateContextAsync();
            var page = await browserContext.NewPageAsync();
            await page.RouteAsync("https://example.com/**", route => route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = scanSucceeds ? """
                    <div id="sidebarBoxVillageList">
                        <div class="listEntry village active" data-did="1"><span class="name">Home</span></div>
                    </div>
                    <div id="rallyPointFarmList"><div class="villageWrapper">
                        <div class="villageHeader"><div class="villageName">Home</div></div>
                        <div class="farmListWrapper expanded">
                            <div class="farmListHeader"><div data-list="10"></div></div>
                            <table class="slots"><tbody><tr><td class="addTarget">0 / 100</td></tr></tbody></table>
                        </div>
                    </div></div>
                    """ : "<div>Farm list is unavailable</div>",
            }));
            await page.GotoAsync("https://example.com/farm");
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            var navigate = Substitute.For<IHandler<ToFarmListPageCommand.Command, Result>>();
            navigate.HandleAsync(Arg.Any<ToFarmListPageCommand.Command>(), Arg.Any<CancellationToken>())
                .Returns(Result.Ok());
            var rxQueue = new RxQueue(factory);
            var handler = new CheckFarmTargetOasisTask.HandleBehavior(new(
                navigate, new FarmTargetTroopsScanner(browser), factory, rxQueue));

            var result = await handler.HandleAsync(
                new(new AccountId(1), new FarmId(10), activate, synchronize), CancellationToken.None);

            result.IsSuccess.ShouldBe(scanSucceeds);
            db.FarmLists.AsNoTracking().Single(x => x.Id == 10).IsActive.ShouldBe(expectedActive);
            db.FarmLists.AsNoTracking().Single(x => x.Id == 11).IsActive.ShouldBeFalse();
            db.FarmLists.AsNoTracking().Single(x => x.Id == 20).IsActive.ShouldBeFalse();
        }
    }
}
