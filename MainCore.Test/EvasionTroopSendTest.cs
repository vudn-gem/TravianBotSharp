using FluentResults;
using Immediate.Handlers.Shared;
using MainCore.Commands.Features;
using MainCore.Commands.Navigate;
using MainCore.Entities;
using MainCore.Enums;
using MainCore.Errors;
using MainCore.Infrasturecture.Extensions;
using MainCore.Infrasturecture.Persistence;
using MainCore.Parsers;
using MainCore.Services;
using MainCore.Tasks;
using MainCore.Test.Parsers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace MainCore.Test
{
    public sealed class EvasionTroopSendTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        private sealed class Clock : TimeProvider
        {
            public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);
            public override DateTimeOffset GetUtcNow() => Now;
        }

        private static string Sidebar(int village = 1) => $"""
            <div id="sidebarBoxVillageList"><div class="listEntry village active" data-did="{village}"></div>
            <div class="quickLinks"><a data-load-tooltip="quickLink" href="/building?id=16&amp;tt=2">
            <svg class="RallyPointSendTroops" width="20" height="20"></svg></a></div></div>
            """;

        private static string Overview(string mode) => Sidebar() + """
            <div id="content" class="buildRallyPoint"><div class="rallyPointOverviewContainer">Incoming
            """ + (mode is "incoming-canceled" or "manual-no-incoming" ? "" : """
            <table class="troop_details inRaid"><thead><tr><td>
            <img class="markAttack" id="markSymbol_101"></td></tr></thead><tbody class="infos"><tr><td>
            <div class="in"><span class="timer" counting="down">00:02:00</span></div>
            </td></tr></tbody></table>
            """) + "</div></div>";

        private static string SendHtml(string mode)
        {
            var fields = string.Join("", Enumerable.Range(1, mode == "hero-absent" ? 10 : 11).Select(i =>
            {
                var amount = i == 1 ? "1&#8239;234" : i == 4 ? "42" : i == 11 ? "1" : "0";
                if (mode == "empty") amount = "0";
                if (mode == "hero-zero" && i == 11) amount = "0";
                if (mode == "hero-only" && i != 11) amount = "0";
                if (mode == "invalid-count" && i == 3) amount = "?";
                if (mode == "disabled-positive" && i == 4) amount = "42";
                var disabled = i == 2 || (mode == "disabled-positive" && i == 4) ? "disabled" : "";
                if (mode == "missing-field" && i == 3) return "";
                return $"<td><img class='unit u{(i == 11 ? "hero" : (60 + i).ToString())}'>" +
                    $"<input name='troop[t{i}]' value='{(i == 5 ? "9" : "")}' {disabled}><a>{amount}</a></td>";
            }));
            var raid = mode == "no-raid" ? "" : $"<input type='radio' name='eventType' value='4' {(mode == "raid-disabled" ? "disabled" : "")}>";
            return Sidebar(mode == "wrong-shortcut-village" ? 2 : 1) + $$"""
                <div id="content"><div id="build"><div class="a2b">
                <form action="/building?id=16&amp;tt=2" onsubmit="event.preventDefault(); const data=new FormData(this);
                sessionStorage.setItem('army',JSON.stringify(Array.from({length:11},(_,i)=>data.get('troop[t'+(i+1)+']')||'0')));
                sessionStorage.setItem('x',data.get('x')); sessionStorage.setItem('y',data.get('y'));
                sessionStorage.setItem('raid',data.get('eventType')); location.href='/building?id=16&amp;tt=2&amp;stage=confirm';">
                <table id="troops"><tr>{{fields}}</tr></table>
                <input name="x"><input name="y"><input type="radio" name="eventType" value="3" checked>{{raid}}
                <input name="redeployHero" type="checkbox" checked><button type="submit">Send</button>
                </form></div></div></div>
                """;
        }

        private static string ConfirmHtml(string mode) => Sidebar() + $$"""
            <div id="content"><form id="troopSendForm" action="/building?id=16&amp;tt=2"></form></div>
            <script>
            const form=document.querySelector('#troopSendForm');
            const mode='{{mode}}';
            let army=JSON.parse(sessionStorage.getItem('army'));
            if(mode==='wrong-army') army[0]='1';
            const field=(name,value)=>`<input type="hidden" name="${name}" value="${value}">`;
            const x=mode==='wrong-target'?'10':sessionStorage.getItem('x'),y=sessionStorage.getItem('y');
            form.innerHTML=army.map((value,i)=>field(`troops[0][t${i+1}]`,value)).join('')+
                field('troops[0][villageId]',mode==='wrong-owner'?'2':'1')+field('x',x)+field('y',y)+
                field('eventType',mode==='normal-attack'?'3':sessionStorage.getItem('raid'))+
                field('redeployHero',mode==='redeploy'?'1':'0')+
                (mode==='extra-wave'?field('troops[1][t1]','1'):'')+
                `<div id="short_info"><a href="/map?x=${mode==='wrong-map'?'2':x}&amp;y=${y}">Target</a></div>`+
                '<button id="confirmSendTroops" type="button">Confirm</button>';
            form.querySelector('#confirmSendTroops').onclick=()=>{
                sessionStorage.setItem('confirmed','1');
                location.href='/building?id=16&tt=1';
            };
            </script>
            """;

        [Theory]
        [InlineData("success", true)]
        [InlineData("hero-absent", true)]
        [InlineData("missing-shortcut", true)]
        [InlineData("disabled-shortcut", true)]
        [InlineData("duplicate-shortcut", true)]
        [InlineData("external-shortcut", true)]
        [InlineData("wrong-shortcut-village", true)]
        [InlineData("incoming-canceled", false)]
        [InlineData("empty", false)]
        [InlineData("invalid-count", false)]
        [InlineData("missing-field", false)]
        [InlineData("disabled-positive", false)]
        [InlineData("no-raid", false)]
        [InlineData("raid-disabled", false)]
        [InlineData("wrong-army", false)]
        [InlineData("wrong-target", false)]
        [InlineData("wrong-owner", false)]
        [InlineData("normal-attack", false)]
        [InlineData("wrong-map", false)]
        [InlineData("extra-wave", false)]
        [InlineData("redeploy", false)]
        [InlineData("settings-changed", false)]
        [InlineData("deadline", false)]
        [InlineData("save-fails", false)]
        [InlineData("confirm-fails", false)]
        [InlineData("fill-fails", false)]
        [InlineData("hero-zero", true)]
        [InlineData("hero-only", true)]
        [InlineData("cancel-input", false)]
        [InlineData("disabled-send", false)]
        [InlineData("duplicate-send", false)]
        [InlineData("manual-no-incoming", true)]
        public async Task SendsWholeArmyOnlyAfterValidatedRaidAndDurableGuard(string mode, bool expectedSend)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tbs-evade-{Guid.NewGuid():N}");
            Directory.CreateDirectory(directory);
            try
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={Path.Combine(directory, "account.db")};Pooling=False").Options;
                using var database = new AppDbContext(options);
                database.Database.EnsureCreated();
                database.InitializeEvasionTroopAttempts();
                database.InitializeEvasionTroopAttempts();
                database.Accounts.Add(new Account { Id = 1, Username = "test", Server = "https://example.com" });
                database.Villages.Add(new Village { Id = 1, AccountId = 1, Name = "Origin" });
                database.SaveChanges();
                database.FillVillageSettings();
                database.SaveVillageSetting(new(1), new()
                {
                    [VillageSettingEnums.EvasionEnable] = 1, [VillageSettingEnums.EvasionTroopEnable] = 1,
                    [VillageSettingEnums.EvasionX] = -20, [VillageSettingEnums.EvasionY] = 30,
                });
                var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
                factory.CreateDbContext().Returns(_ => new AppDbContext(options));
                var clock = new Clock();
                using var cts = new CancellationTokenSource();
                await using var context = await CreateContextAsync();
                var page = await context.NewPageAsync();
                page.SetDefaultTimeout(1500);
                var sendPages = 0;
                await context.RouteAsync("https://example.com/**", route =>
                {
                    var uri = new Uri(route.Request.Url);
                    if (HeroOasisParser.Query(uri, "tt") == "2" &&
                        HeroOasisParser.Query(uri, "stage") != "confirm") sendPages++;
                    var html = HeroOasisParser.Query(uri, "stage") == "confirm" ? ConfirmHtml(mode) :
                        HeroOasisParser.Query(uri, "tt") == "2"
                            ? SendHtml(mode == "wrong-shortcut-village" && sendPages > 1 ? "success" : mode)
                            : Overview(mode);
                    if (mode == "missing-shortcut") html = html.Replace("class=\"RallyPointSendTroops\"", "class=\"None\"");
                    if (mode == "disabled-shortcut") html = html.Replace("data-load-tooltip=", "class='disabled' data-load-tooltip=");
                    if (mode == "external-shortcut") html = html.Replace("href=\"/building?id=16&amp;tt=2\"", "href=\"https://other.example/building?id=16&amp;tt=2\"");
                    if (mode == "duplicate-shortcut")
                        html = html.Replace("<div class=\"quickLinks\">", """
                            <div class="quickLinks"><a data-load-tooltip="quickLink" href="/building?id=16&amp;tt=2">
                            <svg class="RallyPointSendTroops" width="20" height="20"></svg></a>
                            """);
                    if (mode == "disabled-send") html = html.Replace("<button type=\"submit\">", "<button type=\"submit\" disabled>");
                    if (mode == "duplicate-send") html = html.Replace("<button type=\"submit\">", "<button type=\"submit\">Extra</button><button type=\"submit\">");
                    return route.FulfillAsync(new() { ContentType = "text/html", Body = html });
                });
                await page.GotoAsync("https://example.com/resources");
                var browser = Substitute.For<IChromeBrowser>();
                var currentPage = page;
                browser.CurrentPage.Returns(_ => currentPage);
                browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
                browser.OpenNewTab().Returns(async _ =>
                {
                    currentPage = await context.NewPageAsync();
                    return Result.Ok();
                });
                browser.CloseNewTab().Returns(async _ =>
                {
                    await currentPage.CloseAsync();
                    currentPage = page;
                    return Result.Ok();
                });
                browser.Click(Arg.Any<ILocator>(), true, Arg.Any<string>()).Returns(async call =>
                {
                    var href = await call.Arg<ILocator>().GetAttributeAsync("href");
                    currentPage = await context.NewPageAsync();
                    await currentPage.GotoAsync(new Uri(new Uri(page.Url), href!).AbsoluteUri);
                    return Result.Ok();
                });
                var writes = new List<(string? Name, string Value)>();
                browser.Input(Arg.Any<ILocator>(), Arg.Any<string>(), Arg.Any<string>()).Returns(async call =>
                {
                    var input = call.Arg<ILocator>();
                    writes.Add((await input.GetAttributeAsync("name"), call.ArgAt<string>(1)));
                    if (mode == "fill-fails") return Retry.Error.WithError("Input failed");
                    await input.FillAsync(call.ArgAt<string>(1));
                    if (mode == "cancel-input") cts.Cancel();
                    return Result.Ok();
                });
                var confirms = 0;
                browser.Click(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(async call =>
                {
                    var input = call.Arg<ILocator>();
                    if (await input.GetAttributeAsync("id") == "confirmSendTroops")
                    {
                        confirms++;
                        using var saved = new AppDbContext(options);
                        saved.EvasionTroopAttempts.Single().Status.ShouldBe("Check in game");
                        if (mode == "confirm-fails") return Retry.Error.WithError("Unknown confirm outcome");
                    }
                    await input.ClickAsync();
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
                    if (HeroOasisParser.Query(new Uri(currentPage.Url), "stage") == "confirm")
                    {
                        if (mode == "settings-changed")
                            database.SaveVillageSetting(new(1), new() { [VillageSettingEnums.EvasionTroopEnable] = 0 });
                        if (mode == "deadline") clock.Now = clock.Now.AddMinutes(3);
                        if (mode == "save-fails") database.Database.ExecuteSqlRaw("DROP TABLE EvasionTroopAttempts");
                    }
                    return Result.Ok();
                });
                var incoming = new EvasionIncoming(mode == "manual-no-incoming" ? -101 : 101, true,
                    clock.GetLocalNow().DateTime.AddMinutes(2));
                var command = new EvadeTroopsCommand.Command(new(1), new(1), incoming, -20, 30,
                    ManualTest: mode == "manual-no-incoming");
                var handler = new EvadeTroopsCommand.HandleBehavior(new EvadeTroopsCommand(browser, factory, clock));
                if (mode == "cancel-input")
                {
                    await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(command, cts.Token));
                    confirms.ShouldBe(0);
                    database.EvasionTroopAttempts.Any().ShouldBeFalse();
                    browser.CurrentPage.ShouldBe(page);
                    context.Pages.Count.ShouldBe(1);
                    await browser.Received(1).CloseNewTab();
                    return;
                }
                var result = await handler.HandleAsync(command, cts.Token);
                result.IsSuccess.ShouldBe(expectedSend, string.Join("; ", result.Errors.Select(x => x.Message)));
                confirms.ShouldBe(expectedSend || mode == "confirm-fails" ? 1 : 0);
                browser.CurrentPage.ShouldBe(page);
                context.Pages.Count.ShouldBe(1);
                if (mode == "manual-no-incoming")
                {
                    page.Url.ShouldBe("https://example.com/resources");
                    await browser.DidNotReceive().Navigate(Arg.Any<string>());
                }
                else HeroOasisParser.Query(new Uri(page.Url), "tt").ShouldBe("1");
                if (mode != "incoming-canceled") await browser.Received(1).CloseNewTab();
                if (mode is "wrong-army" or "wrong-target" or "wrong-owner" or "normal-attack" or
                    "wrong-map" or "extra-wave" or "redeploy" or "settings-changed" or "deadline" or "save-fails" or "confirm-fails")
                    writes.ShouldContain(("troop[t1]", "1234"));
                if (mode is "missing-shortcut" or "disabled-shortcut" or "duplicate-shortcut" or
                    "external-shortcut" or "wrong-shortcut-village")
                    await browser.Received(1).Navigate("https://example.com/building?id=16&tt=2");
                if (expectedSend)
                {
                    writes[0].ShouldBe(("x", "-20"));
                    writes[1].ShouldBe(("y", "30"));
                    writes.ShouldContain(("x", "-20"));
                    writes.ShouldContain(("y", "30"));
                    if (mode != "hero-only")
                    {
                        writes.ShouldContain(("troop[t1]", "1234"));
                        writes.ShouldContain(("troop[t4]", "42"));
                    }
                    writes.ShouldContain(("troop[t5]", ""));
                    if (mode is not ("hero-absent" or "hero-zero")) writes.ShouldContain(("troop[t11]", "1"));
                    writes.ShouldNotContain(x => x.Name == "troop[t2]");
                    using var saved = new AppDbContext(options);
                    saved.EvasionTroopAttempts.Single().Status.ShouldBe("Sent");
                    var restarted = new EvadeTroopsCommand.HandleBehavior(new EvadeTroopsCommand(browser, factory, clock));
                    (await restarted.HandleAsync(command, CancellationToken.None)).IsFailed.ShouldBeTrue();
                    confirms.ShouldBe(1);
                }
                if (mode == "confirm-fails")
                {
                    (await handler.HandleAsync(command, CancellationToken.None)).IsFailed.ShouldBeTrue();
                    confirms.ShouldBe(1);
                    using var saved = new AppDbContext(options);
                    saved.EvasionTroopAttempts.Single().Status.ShouldBe("Check in game");
                }
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public async Task PreparationRunsTroopEvasionEvenWhenTrainingFails(bool trainFailure, bool manualTest)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"tbs-evade-phase-{Guid.NewGuid():N}");
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
                    [VillageSettingEnums.EvasionEnable] = 1, [VillageSettingEnums.EvasionTroopEnable] = 1,
                    [VillageSettingEnums.EvasionX] = -20, [VillageSettingEnums.EvasionY] = 30,
                    [VillageSettingEnums.Tribe] = (int)TribeEnums.Huns,
                    [VillageSettingEnums.EvasionTrainTroop] = trainFailure ? (int)TroopEnums.Mercenary : 0,
                });
                var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
                factory.CreateDbContext().Returns(_ => new AppDbContext(options));
                await using var context = await CreateContextAsync();
                var page = await context.NewPageAsync();
                await page.RouteAsync("https://example.com/**", route => route.FulfillAsync(new()
                {
                    ContentType = "text/html", Body = Overview("success"),
                }));
                await page.GotoAsync("https://example.com/resources");
                var browser = Substitute.For<IChromeBrowser>();
                browser.CurrentPage.Returns(page);
                browser.Logger.Returns(Substitute.For<Serilog.ILogger>());
                browser.Navigate(Arg.Any<string>()).Returns(async call =>
                {
                    await page.GotoAsync(call.Arg<string>());
                    return Result.Ok();
                });
                browser.Wait(Arg.Any<ILocator>(), Arg.Any<string>()).Returns(Result.Ok());
                var navigate = Substitute.For<IHandler<ToBuildingByTypeCommand.Command, Result>>();
                navigate.HandleAsync(Arg.Any<ToBuildingByTypeCommand.Command>(), Arg.Any<CancellationToken>())
                    .Returns(MissingBuilding.Error(BuildingEnums.Barracks));
                var train = Substitute.For<IHandler<TrainTroopCommand.Command, Result>>();
                var evade = Substitute.For<IHandler<EvadeTroopsCommand.Command, Result>>();
                evade.HandleAsync(Arg.Any<EvadeTroopsCommand.Command>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());
                var clock = new Clock();
                var incoming = new EvasionIncoming(101, true, clock.GetLocalNow().DateTime.AddMinutes(2));
                var task = new EvasionReadinessTask.Task(new(1), new(1), incoming,
                    EvasionSettings.Read(database, new(1), new(1)).Value, manualTest);
                var handler = new EvasionReadinessTask.HandleBehavior(new EvasionReadinessTask(browser, clock, factory, navigate, train, evade));
                var result = await handler.HandleAsync(task, CancellationToken.None);
                result.IsSuccess.ShouldBe(!trainFailure);
                await evade.Received(1).HandleAsync(Arg.Is<EvadeTroopsCommand.Command>(x => x.X == -20 && x.Y == 30 &&
                    x.ManualTest == manualTest),
                    CancellationToken.None);
                (await handler.HandleAsync(task, CancellationToken.None)).HasError<Skip>().ShouldBeTrue();
                await evade.Received(1).HandleAsync(Arg.Any<EvadeTroopsCommand.Command>(), Arg.Any<CancellationToken>());
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void MigratesMissingAttemptTableAndPreservesRecordedAttempts()
        {
            using var database = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite("Data Source=:memory:").Options);
            database.Database.OpenConnection();
            database.Database.EnsureCreated();
            database.Database.ExecuteSqlRaw("DROP TABLE EvasionTroopAttempts");
            database.InitializeEvasionTroopAttempts();
            database.EvasionTroopAttempts.Add(new()
            {
                AccountId = 1, VillageId = 1, MovementId = 101, TargetX = -20, TargetY = 30,
            });
            database.SaveChanges();
            database.InitializeEvasionTroopAttempts();
            database.EvasionTroopAttempts.AsNoTracking().Single().Status.ShouldBe("Check in game");
        }

        [Fact]
        public async Task CancelingBeforePreparationDoesNotReadOrSend()
        {
            var page = Substitute.For<IPage>();
            var browser = Substitute.For<IChromeBrowser>();
            browser.CurrentPage.Returns(page);
            var factory = Substitute.For<IDbContextFactory<AppDbContext>>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var clock = new Clock();
            var command = new EvadeTroopsCommand.Command(new(1), new(1),
                new(101, true, clock.GetLocalNow().DateTime.AddMinutes(2)), -20, 30);
            var handler = new EvadeTroopsCommand.HandleBehavior(new EvadeTroopsCommand(browser, factory, clock));
            await Should.ThrowAsync<OperationCanceledException>(async () => await handler.HandleAsync(command, cts.Token));
            factory.DidNotReceive().CreateDbContext();
            await browser.DidNotReceive().Click(Arg.Any<ILocator>(), Arg.Any<string>());
        }
    }
}
