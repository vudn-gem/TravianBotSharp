using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Entities;
using MainCore.Commands.Features;
using MainCore.Commands.Navigate;
using MainCore.Enums;
using MainCore.Errors;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using MainCore.UI.ViewModels.Tabs.Villages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class EvasionWorkflowTest(PlaywrightFixture fixture) : ParserTestBase(fixture), IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"tbs-evasion-{Guid.NewGuid():N}");
        private sealed class Clock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => new(2026, 11, 25, 23, 50, 0, TimeSpan.Zero);
        }

        private static string Movement(long id, string countdown) => $"""
            <table class="troop_details inRaid"><thead><tr><td>
            <img class="markAttack" id="markSymbol_{id}"></td></tr></thead>
            <tbody class="infos"><tr><th>Arrival</th><td><div class="in">
            <span class="timer" counting="down" data-value="99999">{countdown}</span>
            </div></td></tr></tbody></table>
            """;

        private static IChromeBrowser Browser(IPage page)
        {
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
            {
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
            return browser;
        }

        [Theory]
        [InlineData("valid", false)]
        [InlineData("missing", true)]
        [InlineData("duplicate", true)]
        [InlineData("disabled", true)]
        [InlineData("wrong-village", true)]
        [InlineData("external", true)]
        [InlineData("wrong-filter", true)]
        public async Task OpensIncomingLinkOrSameServerVillageFallback(string mode, bool fallback)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            const string href = "/building?id=16&amp;tt=1&amp;filter=1&amp;subfilters=1";
            var link = $"<a href='{href}'>Incoming Troops</a>";
            if (mode == "external") link = link.Replace("href='/", "href='https://other.example/");
            if (mode == "wrong-filter") link = link.Replace("subfilters=1", "subfilters=2");
            if (mode == "disabled") link = $"<div class='disabled'>{link}</div>";
            if (mode == "duplicate") link += link;
            if (mode == "missing") link = "";
            await page.RouteAsync("https://example.com/**", route =>
            {
                var wrongVillage = mode == "wrong-village" && route.Request.Url.Contains("/building", StringComparison.Ordinal) &&
                    !route.Request.Url.Contains("newdid=1", StringComparison.Ordinal);
                return route.FulfillAsync(new()
                {
                    ContentType = "text/html",
                    Body = $"""
                        <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="{(wrongVillage ? 2 : 1)}"></div></div>
                        <div id="sidebarBoxLinklist">{link}</div>
                        <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">Incoming troops (0)</div></div>
                        """,
                });
            });
            await page.GotoAsync("https://example.com/resources");
            var browser = Browser(page);
            (await EvasionIncomingNavigation.Open(browser, new(1), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            if (fallback)
                await browser.Received(1).Navigate("https://example.com/building?id=16&tt=1&filter=1&subfilters=1&newdid=1");
            else await browser.DidNotReceive().Navigate(Arg.Any<string>());
            page.Url.ShouldStartWith("https://example.com/building?");
        }

        [Fact]
        public async Task InspectionSchedules120SecondsBeforeArrivalDeduplicatesAndCancelsRemovedMovements()
        {
            Directory.CreateDirectory(_directory);
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={Path.Combine(_directory, "account.db")};Pooling=False").Options;
            using var database = new AppDbContext(options);
            database.Database.EnsureCreated();
            database.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
            database.Villages.Add(new Village { Id = 1, AccountId = 1, Name = "Origin" });
            database.SaveChanges();
            database.FillVillageSettings();
            database.SaveVillageSetting(new(1), new() { [VillageSettingEnums.EvasionEnable] = 1 });
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            factory.CreateDbContext().Returns(_ => new AppDbContext(options));
            var clock = new Clock();
            var outbox = new ReportNotificationOutbox(Path.Combine(_directory, "outbox.db"));
            var logger = Substitute.For<Serilog.ILogger>();
            var taskManager = new TaskManager(new RxQueue(factory), factory, logger,
                new ReportMonitor(outbox, logger), new IncomingTroopsMonitor(outbox, clock));
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var movements = Movement(101, "00:20:00") + Movement(102, "00:01:00");
            await page.RouteAsync("https://example.com/**", route => route.FulfillAsync(new()
            {
                ContentType = "text/html",
                Body = $"""
                    <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="1"></div></div>
                    <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">
                    {movements}<div class="paginator"><img class="next disabled"></div></div></div>
                    """,
            }));
            await page.GotoAsync("https://example.com/resources");
            var browser = Browser(page);
            var inspection = new InspectEvasionTask.Task(new(1), new(1));
            taskManager.Add(inspection);
            var handler = new InspectEvasionTask.HandleBehavior(new InspectEvasionTask(factory, browser, taskManager, clock));
            (await handler.HandleAsync(inspection, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            var plans = taskManager.GetTaskList(new(1)).OfType<EvasionReadinessTask.Task>().ToList();
            plans.Count.ShouldBe(2);
            plans.Single(x => x.Incoming.MovementId == 101).ExecuteAt.ShouldBe(clock.GetLocalNow().DateTime.AddSeconds(1080));
            var immediate = plans.Single(x => x.Incoming.MovementId == 102);
            immediate.ExecuteAt.ShouldBe(clock.GetLocalNow().DateTime);
            immediate.Settings.LeadSeconds.ShouldBe(120);
            (await handler.HandleAsync(inspection, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            taskManager.GetTaskList(new(1)).OfType<EvasionReadinessTask.Task>().Count().ShouldBe(2);

            browser.ClearReceivedCalls();
            var readiness = new EvasionReadinessTask.HandleBehavior(new EvasionReadinessTask(browser, clock, factory,
                Substitute.For<IHandler<ToBuildingByTypeCommand.Command, Result>>(),
                Substitute.For<IHandler<TrainTroopCommand.Command, Result>>(),
                Substitute.For<IHandler<EvadeTroopsCommand.Command, Result>>()));
            (await readiness.HandleAsync(immediate, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
            await browser.DidNotReceive().Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>());

            movements = Movement(101, "00:20:00");
            (await handler.HandleAsync(inspection, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            taskManager.GetTaskList(new(1)).OfType<EvasionReadinessTask.Task>().Single().Incoming.MovementId.ShouldBe(101);
            var liveTest = new EvasionReadinessTask.Task(new(1), new(1), new(-101, false, DateTime.MaxValue),
                EvasionSettings.Read(database, new(1), new(1)).Value, manualTest: true);
            taskManager.TryAdd(liveTest).ShouldBeTrue();
            taskManager.TryAdd(new EvasionReadinessTask.Task(new(1), new(1), new(-102, false, DateTime.MaxValue),
                liveTest.Settings, manualTest: true)).ShouldBeFalse();
            movements = "No incoming troops";
            (await handler.HandleAsync(inspection, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            taskManager.GetTaskList(new(1)).OfType<EvasionReadinessTask.Task>().Single().ShouldBe(liveTest);
            taskManager.RemoveEvasion(new(1), new(1), includeManualTests: false);
            taskManager.GetTaskList(new(1)).OfType<EvasionReadinessTask.Task>().Single().ShouldBe(liveTest);
            database.SaveVillageSetting(new(1), new() { [VillageSettingEnums.EvasionEnable] = 0 });
            database.TriggerTask(taskManager, new(1), new(1), new() { [VillageSettingEnums.EvasionEnable] = 0 });
            taskManager.GetTaskList(new(1)).ShouldBeEmpty();
        }

        [Fact]
        public async Task TaskDispatcherRoutesBothEvasionTasks()
        {
            var inspectHandler = Substitute.For<IHandler<InspectEvasionTask.Task, Result>>();
            var readinessHandler = Substitute.For<IHandler<EvasionReadinessTask.Task, Result>>();
            inspectHandler.HandleAsync(Arg.Any<InspectEvasionTask.Task>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
            readinessHandler.HandleAsync(Arg.Any<EvasionReadinessTask.Task>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
            using var provider = new ServiceCollection().AddSingleton(inspectHandler).AddSingleton(readinessHandler).BuildServiceProvider();
            using var scope = provider.CreateScope();
            var inspection = new InspectEvasionTask.Task(new(1), new(1));
            var readiness = new EvasionReadinessTask.Task(new(1), new(1), new(101, false, DateTime.Now.AddHours(1)),
                new(true, TroopEnums.None, null, false, 0, 0, 120));
            (await scope.Execute(inspection, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await scope.Execute(readiness, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            await inspectHandler.Received(1).HandleAsync(inspection, CancellationToken.None);
            await readinessHandler.Received(1).HandleAsync(readiness, CancellationToken.None);
        }

        public void Dispose()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }
}
