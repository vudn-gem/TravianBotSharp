using System.Globalization;
using FluentResults;

namespace MainCore.Services
{
    [RegisterScoped<HeroClearOasisScanner>]
    public sealed class HeroClearOasisScanner(IChromeBrowser browser)
    {
        public async Task<Result> Scan(
            HeroClearFarmOasisTask.Task task, HeroOasisSetting settings, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var status = await HeroOasisParser.ReadHudStatus(page);
            if (status.IsFailed) return Stop.Error.WithErrors(status.Errors);
            if (status.Value.State == HeroHudState.Running)
                return ScheduleByHeroTimer(task, status.Value, "Hero is moving.");
            if (status.Value.State != HeroHudState.Home || status.Value.Village is null)
                return Stop.Error.WithError("Hero status in HUD is unknown.");
            task.HeroVillage = status.Value.Village;
            var hudHealth = await HeroOasisParser.ReadHudHealth(page);
            if (hudHealth.IsFailed) return Stop.Error.WithErrors(hudHealth.Errors);
            if (hudHealth.Value < settings.MinHeroHealth)
                return Stop.Error.WithError($"Hero health {hudHealth.Value} is below minimum {settings.MinHeroHealth}. Hero clear task canceled.");
            var farmUrl = new UriBuilder(page.Url);
            var owner = await FarmTargetTroopsParser.ReadOwnerVillage(page, task.FarmId);
            if (owner.IsFailed) return Stop.Error.WithErrors(owner.Errors);
            if (task.Owner is VillageId previous && previous != owner.Value)
                return Skip.Error.WithError("Farm list owner village changed. Hero clear task canceled.");
            if (task.HeroVillage != owner.Value)
                return Stop.Error.WithError("Hero village differs from farm list owner village.");
            task.Owner = owner.Value;
            var village = VillagePanelParser.GetVillageNode(page, owner.Value);
            if (!await VillagePanelParser.IsActive(village))
            {
                var switched = await browser.Click(village);
                if (switched.IsFailed) return switched;
                switched = await browser.Wait(page.Locator(
                    $"#sidebarBoxVillageList .listEntry.village.active[data-did='{owner.Value.Value}']"));
                if (switched.IsFailed) return switched;
            }
            farmUrl.Query = $"gid=16&tt=99&newdid={owner.Value.Value}";
            var sendUrl = FarmTargetTroopsParser.BuildRallyPointTabUrl(farmUrl.Uri.AbsoluteUri, 2);
            var opened = await browser.Navigate(sendUrl);
            if (opened.IsFailed) return opened;
            var ready = await browser.Wait(page.Locator("#build .a2b form[action*='tt=2'] #troops"));
            if (ready.IsFailed) return ready;
            if (await VillagePanelParser.GetCurrentVillageId(page) != owner.Value)
                return Stop.Error.WithError("Hero availability village switch was not confirmed.");
            var home = await HeroOasisParser.IsHeroHome(page);
            if (home.IsFailed) return Stop.Error.WithErrors(home.Errors);
            if (!home.Value) return Skip.Error.WithError("Hero is not in the farm list village. Hero clear task canceled.");
            task.HeroReturnAt = null;

            opened = await FarmListNavigation.Open(browser, farmUrl.Uri.AbsoluteUri, owner.Value, preferRallyPointTab: true);
            if (opened.IsFailed) return opened;
            var prepared = await FarmListPreparation.Prepare(browser, task.FarmId, owner.Value);
            if (prepared.IsFailed) return Stop.Error.WithErrors(prepared.Errors);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await VillagePanelParser.GetCurrentVillageId(page) != owner.Value)
                    return Stop.Error.WithError("Hero clear oasis village changed.");
                var list = FarmTargetTroopsParser.GetList(page, task.FarmId);
                var targets = await FarmTargetTroopsParser.ReadTargets(page, task.FarmId);
                if (targets.IsFailed) return Stop.Error.WithErrors(targets.Errors);
                var target = targets.Value.FirstOrDefault(x => !x.Disabled && !task.ProcessedSlots.Contains(x.SlotId));
                if (target is null)
                {
                    browser.Logger.Information("Hero clear oasis completed for farm list {FarmId}", task.FarmId.Value);
                    return Result.Ok();
                }
                var uri = new Uri(page.Url);
                if (!Uri.TryCreate(uri, target.Href, out var map) || map.Scheme != uri.Scheme ||
                    map.Authority != uri.Authority || map.AbsolutePath != "/karte.php" ||
                    !int.TryParse(HeroOasisParser.Query(map, "x"), out _) ||
                    !int.TryParse(HeroOasisParser.Query(map, "y"), out _))
                    return Stop.Error.WithError("Hero oasis target map link is invalid.");

                var targetLink = list.Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}']) td.target a");
                if (await targetLink.CountAsync() != 1 || await targetLink.GetAttributeAsync("href") != target.Href)
                    return Stop.Error.WithError("Hero oasis target changed while opening it.");
                var continueScanning = false;
                var result = await FarmTargetOasisTab.Run(browser, target.Href, oasisPage =>
                    ProcessTargetInOasisTab(task, settings, target, owner.Value, oasisPage,
                        cancellationToken, () => continueScanning = true));
                if (result.IsFailed) return result;
                if (continueScanning) continue;
                return result;
            }
        }

        private async Task<Result> ProcessTargetInOasisTab(
            HeroClearFarmOasisTask.Task task,
            HeroOasisSetting settings,
            FarmTargetSlot target,
            VillageId owner,
            IPage page,
            CancellationToken cancellationToken,
            Action continueScanning)
        {
            var animals = await FarmTargetTroopsParser.ReadOasisAnimalTroops(page, target.Href);
            if (animals.IsFailed) return Stop.Error.WithErrors(animals.Errors);
            if (!HeroOasisSettings.AllowsAnimals(settings, animals.Value))
            {
                SkipTarget(task, target, "Animal count or allowed animals do not match.");
                continueScanning();
                return Result.Ok();
            }
            if (HeroOasisSettings.AllowsSafeClear(settings, animals.Value))
            {
                var opened = await OpenRaidUnoccupiedOasisFromDialog(target.Href);
                if (opened.IsFailed) return Stop.Error.WithErrors(opened.Errors);
            }
            else
            {
                var simulator = await HeroOasisParser.SimulatorUrl(page);
                if (simulator.IsFailed) return Stop.Error.WithErrors(simulator.Errors);
                var opened = await browser.Navigate(simulator.Value);
                if (opened.IsFailed) return opened;
                var ready = await browser.Wait(page.Locator("#combatSimulatorForm .role.attacker select[name='village']"));
                if (ready.IsFailed) return ready;
                var attacker = page.Locator("#combatSimulatorForm .role.attacker");
                await attacker.Locator("select[name='village']").SelectOptionAsync(owner.Value.ToString(CultureInfo.InvariantCulture));
                opened = await browser.Click(attacker.Locator(".villageSelection a.reload"));
                if (opened.IsFailed) return opened;
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                var filled = await HeroOasisParser.FillSimulator(page, settings);
                if (filled.IsFailed) return Skip.Error.WithErrors(filled.Errors);
                var health = await HeroOasisParser.ReadHealth(page);
                if (health.IsFailed) return Stop.Error.WithErrors(health.Errors);
                if (health.Value < settings.MinHeroHealth)
                    return Stop.Error.WithError($"Hero health {health.Value} is below minimum {settings.MinHeroHealth}. Hero clear task canceled.");
                var defenders = await HeroOasisParser.ValidateDefenders(page, animals.Value);
                if (defenders.IsFailed)
                {
                    SkipTarget(task, target, defenders.Errors[0].Message);
                    continueScanning();
                    return Result.Ok();
                }

                await page.EvaluateAsync("""
                    () => {
                        const results = document.querySelector('#combatSimulator .results');
                        if (!results) throw new Error('Simulator results container missing');
                        window.heroOasisResultReady = false;
                        window.heroOasisObserver?.disconnect();
                        window.heroOasisObserver = new MutationObserver(() => {
                            window.heroOasisResultReady = true;
                        });
                        window.heroOasisObserver.observe(results, {childList:true, subtree:true, characterData:true});
                    }
                    """);
                cancellationToken.ThrowIfCancellationRequested();
                opened = await browser.Click(page.Locator("#combatSimulator #simulate"));
                if (opened.IsFailed) return opened;
                await page.WaitForFunctionAsync("() => window.heroOasisResultReady && !!document.querySelector('#combatSimulator .results .result.attack')");
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await page.EvaluateAsync("() => window.heroOasisObserver?.disconnect()");
                var simulation = await HeroOasisParser.ReadSimulation(page, settings, health.Value, owner, target.Href);
                if (simulation.IsFailed)
                {
                    SkipTarget(task, target, simulation.Errors[0].Message);
                    continueScanning();
                    return Result.Ok();
                }
                if (!HeroOasisSettings.AllowsResult(settings, simulation.Value))
                {
                    SkipTarget(task, target,
                        $"Simulation rejected: hero health {simulation.Value.HealthBefore} -> {simulation.Value.HealthAfter}, sub troop lost {simulation.Value.SubTroopLost}.");
                    continueScanning();
                    return Result.Ok();
                }

                opened = await browser.Click(page.Locator("#combatSimulator .resultActionWrapper #sendTroops"));
                if (opened.IsFailed) return opened;
                ready = await browser.Wait(page.Locator("#build .a2b form[action*='tt=2'] #troops"));
                if (ready.IsFailed) return ready;
            }
            var availableHero = await HeroOasisParser.IsHeroHome(page);
            if (availableHero.IsFailed) return Stop.Error.WithErrors(availableHero.Errors);
            if (!availableHero.Value)
                return await HandleHeroUnavailable(task, target, "Hero is no longer in the village.");
            var send = await HeroOasisParser.PrepareSend(page, settings, owner, target.Href);
            if (send.IsFailed)
            {
                if (IsHeroUnavailable(send.Errors))
                    return await HandleHeroUnavailable(task, target, send.Errors[0].Message);
                return Stop.Error.WithErrors(send.Errors);
            }
            if (!send.Value)
                return RetryTargetOrStop(task, target, "Insufficient sub troops.");
            cancellationToken.ThrowIfCancellationRequested();
            var sent = await browser.Click(page.Locator("#build .a2b form[action*='tt=2'] button#ok"));
            if (sent.IsFailed) return sent;
            var confirmationReady = await browser.Wait(page.Locator("#troopSendForm #confirmSendTroops"));
            if (confirmationReady.IsFailed) return confirmationReady;
            var confirmation = await HeroOasisParser.ReadConfirmation(page, settings, owner, target.Href);
            if (confirmation.IsFailed) return Stop.Error.WithErrors(confirmation.Errors);
            if (confirmation.Value <= DateTime.Now)
                return Stop.Error.WithError("Hero return time is not in the future. Raid not confirmed.");
            cancellationToken.ThrowIfCancellationRequested();
            task.ConfirmationAttempted = true;
            var confirmed = await browser.Click(page.Locator("#troopSendForm #confirmSendTroops"));
            if (confirmed.IsFailed) return Stop.Error.WithErrors(confirmed.Errors);
            await page.WaitForFunctionAsync("""
                () => new URL(location.href).searchParams.get('tt') === '1' &&
                    !!document.querySelector('.subNavi a.tabItem.active[href*="tt=1"]')
                """);
            task.ProcessedSlots.Add(target.SlotId);
            task.HeroReturnAt = confirmation.Value;
            task.ExecuteAt = confirmation.Value.AddMilliseconds(task.TaskBufferMilliseconds <= 0 ? 1000 : task.TaskBufferMilliseconds);
            task.ConfirmationAttempted = false;
            task.SentRaidThisRun = true;
            browser.Logger.Information(
                "Hero raid sent to slot {SlotId}; next target scheduled at {ExecuteAt}",
                target.SlotId, task.ExecuteAt);
            return Result.Ok();
        }

        private void SkipTarget(HeroClearFarmOasisTask.Task task, FarmTargetSlot target, string reason)
        {
            task.ProcessedSlots.Add(target.SlotId);
            browser.Logger.Information("Hero oasis target {SlotId} skipped: {Reason}", target.SlotId, reason);
        }

        private static bool IsHeroUnavailable(IReadOnlyList<IError> errors) =>
            errors.Any(x => x.Message.Contains("Hero is no longer available", StringComparison.Ordinal));

        private Result RetryTargetOrStop(HeroClearFarmOasisTask.Task task, FarmTargetSlot target, string reason)
        {
            if (task.RetryAttemptedSlots.Add(target.SlotId))
            {
                task.ExecuteAt = DateTime.Now.AddMilliseconds(task.RetryDelayMilliseconds <= 0 ? 1000 : task.RetryDelayMilliseconds);
                return Skip.Error.WithError(
                    $"{reason} Slot {target.SlotId} scheduled once at {task.ExecuteAt:yyyy-MM-dd HH:mm:ss}.");
            }
            return Stop.Error.WithError($"{reason} Slot {target.SlotId} already retried once.");
        }

        private Result ScheduleByHeroTimer(HeroClearFarmOasisTask.Task task, HeroHudStatus status, string reason)
        {
            if (!status.SecondsUntilReady.HasValue)
                return Stop.Error.WithError("Hero status timer is missing.");
            var returnAt = DateTime.Now.AddSeconds(Math.Max(0, status.SecondsUntilReady.Value));
            task.HeroReturnAt = returnAt;
            task.ExecuteAt = returnAt.AddMilliseconds(task.TaskBufferMilliseconds <= 0 ? 1000 : task.TaskBufferMilliseconds);
            return Skip.Error.WithError($"{reason} Next run at {task.ExecuteAt:yyyy-MM-dd HH:mm:ss}.");
        }

        private async Task<Result> HandleHeroUnavailable(
            HeroClearFarmOasisTask.Task task, FarmTargetSlot target, string reason)
        {
            var status = await HeroOasisParser.ReadHudStatus(browser.CurrentPage);
            if (status.IsFailed || status.Value.State == HeroHudState.Unknown)
                return RetryTargetOrStop(task, target, $"Unable to read hero status. {reason}");
            if (status.Value.State == HeroHudState.Running)
                return ScheduleByHeroTimer(task, status.Value, reason);
            return RetryTargetOrStop(task, target, reason);
        }

        private async Task<Result> OpenRaidUnoccupiedOasisFromDialog(string targetHref)
        {
            var page = browser.CurrentPage;
            var opened = await page.EvaluateAsync<bool>(@"href => {
                const tile = document.querySelector('#tileDetails.oasis');
                if (!tile) return false;
                const center = tile.querySelector('a[data-centermap]');
                if (!center) return false;
                const expected = new URL(href, location.href);
                const actual = new URL(center.getAttribute('href'), location.href);
                if (actual.searchParams.get('x') !== expected.searchParams.get('x') ||
                    actual.searchParams.get('y') !== expected.searchParams.get('y')) return false;
                let raid = tile.querySelector(
                    '.detailImage .options .option a[href*=""/build.php""][href*=""tt=2""][href*=""eventType=4""]');
                if (!raid) {
                    const clean = text => (text || '')
                        .replace(/[\u200e\u200f\u202a-\u202e\u2066-\u2069]/g, '')
                        .trim()
                        .toLowerCase();
                    raid = Array.from(tile.querySelectorAll('.detailImage .options .option a'))
                        .find(node => clean(node.textContent) === 'raid unoccupied oasis') || null;
                }
                if (!raid) return false;
                raid.click();
                return true;
            }", targetHref);
            if (!opened) return Result.Fail("Unable to find 'Raid unoccupied oasis' action in oasis dialog.");
            return await browser.Wait(page.Locator("#build .a2b form[action*='tt=2'] #troops"));
        }
    }
}
