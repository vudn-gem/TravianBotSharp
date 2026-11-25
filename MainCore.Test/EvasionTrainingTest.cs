using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Commands.Features;
using MainCore.Commands.Navigate;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Errors;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class EvasionTrainingTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private static IChromeBrowser Browser(IPage page)
        {
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
            browser.Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>()).Returns(async call =>
            {
                await call.Arg<ILocator>().FillAsync(call.ArgAt<string>(1));
                return Result.Ok();
            });
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

        private static string TrainingHtml(string maximum) => $"""
            <div class="troop"><img class="unit u61"><div class="cta">
            <input class="text" name="t1"><a>{maximum}</a></div></div>
            <div class="troop"><img class="unit u62"><div class="cta">
            <input class="text" name="t2" value="7"><a>0</a></div></div>
            <div class="troop"><img class="unit u63"><div class="cta">
            <input class="text" name="t3" value="0"><a>0</a></div></div>
            <button id="s1" onclick="window.submitted=(window.submitted||0)+1">Train</button>
            """;

        [Theory]
        [InlineData("1&#8239;234")]
        [InlineData("(1.234)")]
        public async Task MaximumModeTrainsExactAffordableAmountAndClearsOtherTypes(string maximum)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(TrainingHtml(maximum));
            var browser = Browser(page);
            var clock = new Clock();
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, clock));
            var result = await handler.HandleAsync(new(TroopEnums.Mercenary, 0, false,
                TrainMaximum: true, SubmitBefore: clock.GetLocalNow().DateTime.AddMinutes(1)), CancellationToken.None);
            result.IsSuccess.ShouldBeTrue();
            (await page.Locator("[name=t1]").InputValueAsync()).ShouldBe("1234");
            (await page.Locator("[name=t2]").InputValueAsync()).ShouldBe("0");
            (await page.EvaluateAsync<int>("window.submitted")).ShouldBe(1);
            await browser.Received(2).Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>());
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("?")]
        [InlineData("123abc")]
        [InlineData("2147483648")]
        [InlineData("")]
        public async Task InvalidOrZeroMaximumDoesNotFillOrSubmit(string maximum)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(TrainingHtml(maximum));
            var browser = Browser(page);
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, new Clock()));
            (await handler.HandleAsync(new(TroopEnums.Mercenary, 0, false, TrainMaximum: true),
                CancellationToken.None)).IsFailed.ShouldBeTrue();
            await browser.DidNotReceive().Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>());
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Fact]
        public async Task MissingOrAmbiguousSelectedTroopDoesNotSubmit()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            var browser = Browser(page);
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, new Clock()));
            foreach (var html in new[] { "<div>No troop unlocked</div>", TrainingHtml("10") + TrainingHtml("20") })
            {
                await page.SetContentAsync(html);
                (await handler.HandleAsync(new(TroopEnums.Mercenary, 0, false, TrainMaximum: true),
                    CancellationToken.None)).IsFailed.ShouldBeTrue();
            }
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task DeadlinePreventsTrainingBeforeOrAfterInput(bool expiresWhileTyping)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(TrainingHtml("42"));
            var browser = Browser(page);
            var clock = new Clock();
            var deadline = clock.GetLocalNow().DateTime.AddSeconds(expiresWhileTyping ? 10 : 0);
            if (expiresWhileTyping)
                browser.Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>()).Returns(async call =>
                {
                    await call.Arg<ILocator>().FillAsync(call.ArgAt<string>(1));
                    clock.Now = clock.Now.AddSeconds(20);
                    return Result.Ok();
                });
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, clock));
            (await handler.HandleAsync(new(TroopEnums.Mercenary, 0, false, TrainMaximum: true, SubmitBefore: deadline),
                CancellationToken.None)).HasError<Skip>().ShouldBeTrue();
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Fact]
        public async Task NormalTrainingKeepsConfiguredAmountAndExistingResourceGate()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(TrainingHtml("42"));
            var browser = Browser(page);
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, new Clock()));
            (await handler.HandleAsync(new(TroopEnums.Mercenary, 8, false), CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await page.Locator("[name=t1]").InputValueAsync()).ShouldBe("8");
            (await page.Locator("[name=t2]").InputValueAsync()).ShouldBe("7");
            browser.ClearReceivedCalls();
            (await handler.HandleAsync(new(TroopEnums.Mercenary, 43, false), CancellationToken.None))
                .HasError<MissingResource>().ShouldBeTrue();
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Theory]
        [InlineData("input-failure")]
        [InlineData("canceled")]
        [InlineData("settings-changed")]
        public async Task DoesNotSubmitWhenInputCancellationOrFinalSettingsGuardFails(string mode)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync(TrainingHtml("42"));
            var browser = Browser(page);
            using var cts = new CancellationTokenSource();
            if (mode == "input-failure")
                browser.Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>()).Returns(Retry.Error);
            if (mode == "canceled") cts.Cancel();
            var handler = new TrainTroopCommand.HandleBehavior(new TrainTroopCommand(browser.Logger, browser, new Clock()));
            var command = new TrainTroopCommand.Command(TroopEnums.Mercenary, 0, false, TrainMaximum: true,
                ValidateBeforeSubmit: () => Skip.Error.WithError("Settings changed"));
            if (mode == "canceled")
                await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(command, cts.Token));
            else
                (await handler.HandleAsync(command, cts.Token)).IsFailed.ShouldBeTrue();
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }

        [Theory]
        [InlineData(TroopEnums.Mercenary, TribeEnums.Huns, BuildingEnums.Barracks)]
        [InlineData(TroopEnums.SteppeRider, TribeEnums.Huns, BuildingEnums.Stable)]
        [InlineData(TroopEnums.HunCatapult, TribeEnums.Huns, BuildingEnums.Workshop)]
        [InlineData(TroopEnums.SpartanCatapult, TribeEnums.Spartan, BuildingEnums.Workshop)]
        public async Task EvasionOpensCorrectBuildingAndRequestsMaximumOnce(TroopEnums troop, TribeEnums tribe, BuildingEnums building)
        {
            await RunPreparation(troop, tribe, building, "success");
        }

        [Theory]
        [InlineData("none")]
        [InlineData("disabled")]
        [InlineData("expired")]
        [InlineData("canceled-incoming")]
        [InlineData("missing-building")]
        [InlineData("changed-settings")]
        [InlineData("input-failure")]
        [InlineData("submit-exception")]
        [InlineData("manual-test")]
        [InlineData("manual-test-changed")]
        public async Task EvasionSkipsUnsafeTrainingAndDoesNotRetryAnAttempt(string mode) =>
            await RunPreparation(mode == "none" ? TroopEnums.None : TroopEnums.Mercenary,
                TribeEnums.Huns, BuildingEnums.Barracks, mode);

        private async Task RunPreparation(TroopEnums troop, TribeEnums tribe, BuildingEnums building, string mode)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tbs-evasion-train-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(directory, "account.db")};Pooling=False").Options;
                using var database = new AppDbContext(options);
                database.Database.EnsureCreated();
                database.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                database.Villages.Add(new Village { Id = 1, AccountId = 1, Name = "Origin" });
                database.SaveChanges();
                database.FillVillageSettings();
                database.SaveVillageSetting(new(1), new()
                {
                    [VillageSettingEnums.EvasionEnable] = mode == "disabled" ? 0 : 1,
                    [VillageSettingEnums.Tribe] = (int)tribe,
                    [VillageSettingEnums.EvasionTrainTroop] = (int)troop,
                });
                var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
                factory.CreateDbContext().Returns(_ => new AppDbContext(options));
                await using var context = await CreateContextAsync();
                var page = await context.NewPageAsync();
                var movementHtml = mode is "canceled-incoming" or "manual-test" or "manual-test-changed" ? "" : """
                    <table class="troop_details inAttack"><thead><tr><td>
                    <img class="markAttack" id="markSymbol_101"></td></tr></thead>
                    <tbody class="infos"><tr><td><div class="in">
                    <span class="timer" counting="down">00:02:00</span></div></td></tr></tbody></table>
                    """;
                await page.RouteAsync("https://example.com/**", route => route.FulfillAsync(new()
                {
                    ContentType = "text/html",
                    Body = $"""
                        <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="1"></div></div>
                        <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">
                        Incoming troops {movementHtml}</div></div>
                        """,
                }));
                await page.GotoAsync("https://example.com/resources");
                var browser = Browser(page);
                var navigate = Substitute.For<IHandler<ToBuildingByTypeCommand.Command, Result>>();
                navigate.HandleAsync(Arg.Any<ToBuildingByTypeCommand.Command>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    call.Arg<ToBuildingByTypeCommand.Command>().BuildingType.ShouldBe(building);
                    if (mode == "changed-settings")
                        database.SaveVillageSetting(new(1), new() { [VillageSettingEnums.EvasionTrainTroop] = 0 });
                    return mode == "missing-building" ? Result.Fail("Training building is missing") : Result.Ok();
                });
                var train = Substitute.For<IHandler<TrainTroopCommand.Command, Result>>();
                train.HandleAsync(Arg.Any<TrainTroopCommand.Command>(), Arg.Any<CancellationToken>()).Returns(call =>
                {
                    var command = call.Arg<TrainTroopCommand.Command>();
                    command.Troop.ShouldBe(troop);
                    command.TrainMaximum.ShouldBeTrue();
                    command.SubmitBefore.ShouldNotBeNull();
                    if (mode == "submit-exception") throw new PlaywrightException("Unknown submit outcome");
                    return mode == "input-failure" ? Retry.Error.WithError("Input failed") : Result.Ok();
                });
                var clock = new Clock();
                var task = new EvasionReadinessTask.Task(new(1), new(1),
                    new(101, false, clock.GetLocalNow().DateTime.AddSeconds(mode == "expired" ? -1 : 120)),
                    EvasionSettings.Read(database, new(1), new(1)).Value,
                    manualTest: mode is "manual-test" or "manual-test-changed");
                if (mode == "manual-test-changed")
                    database.SaveVillageSetting(new(1), new() { [VillageSettingEnums.EvasionTrainTroop] = 0 });
                var handler = new EvasionReadinessTask.HandleBehavior(new EvasionReadinessTask(browser, clock, factory, navigate, train,
                    Substitute.For<IHandler<EvadeTroopsCommand.Command, Result>>()));
                var result = await handler.HandleAsync(task, CancellationToken.None);
                if (mode is not ("success" or "none" or "manual-test")) result.HasError<Skip>().ShouldBeTrue();
                else result.IsSuccess.ShouldBeTrue();
                var attempted = mode is "success" or "input-failure" or "submit-exception" or "manual-test";
                if (mode == "manual-test")
                {
                    task.Key.ShouldBe("1-1-evasion-test");
                    await browser.DidNotReceive().Navigate(Arg.Any<string>());
                    page.Url.ShouldBe("https://example.com/resources");
                }
                task.TrainingAttempted.ShouldBe(attempted);
                if (attempted)
                {
                    (await handler.HandleAsync(task, CancellationToken.None)).HasError<Skip>().ShouldBeTrue();
                    await train.Received(1).HandleAsync(Arg.Any<TrainTroopCommand.Command>(), Arg.Any<CancellationToken>());
                }
                else await train.DidNotReceive().HandleAsync(Arg.Any<TrainTroopCommand.Command>(), Arg.Any<CancellationToken>());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
