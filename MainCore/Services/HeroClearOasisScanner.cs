using System.Globalization;

namespace MainCore.Services
{
    [RegisterScoped<HeroClearOasisScanner>]
    public sealed class HeroClearOasisScanner(IChromeBrowser browser)
    {
        public async Task<Result> Scan(
            HeroClearFarmOasisTask.Task task, HeroOasisSetting settings, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var farmUrl = new UriBuilder(page.Url);
            var owner = await FarmTargetTroopsParser.ReadOwnerVillage(page, task.FarmId);
            if (owner.IsFailed) return Stop.Error.WithErrors(owner.Errors);
            if (task.Owner is VillageId previous && previous != owner.Value)
                return Skip.Error.WithError("Farm list owner village changed. Hero clear task canceled.");
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

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                opened = await browser.Navigate(farmUrl.Uri.AbsoluteUri);
                if (opened.IsFailed) return opened;
                if (await VillagePanelParser.GetCurrentVillageId(page) != owner.Value)
                    return Stop.Error.WithError("Hero clear oasis village changed.");
                var listOwner = await FarmTargetTroopsParser.ReadOwnerVillage(page, task.FarmId);
                if (listOwner.IsFailed || listOwner.Value != owner.Value)
                    return Stop.Error.WithError("Hero clear oasis farm list owner changed.");
                var list = FarmTargetTroopsParser.GetList(page, task.FarmId);
                if (await list.CountAsync() != 1) return Stop.Error.WithError("Farm list not found.");
                var header = list.Locator("xpath=..").Locator(":scope > .villageHeader");
                if (await header.CountAsync() == 1 &&
                    await header.EvaluateAsync<bool>("node => node.classList.contains('collapsed')"))
                {
                    var expanded = await browser.Click(header.Locator("a.expandCollapse"));
                    if (expanded.IsFailed) return expanded;
                }
                if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
                {
                    var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                    if (expanded.IsFailed) return expanded;
                }
                ready = await browser.Wait(list.Locator("table.slots"));
                if (ready.IsFailed) return ready;
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
                opened = await browser.Click(targetLink);
                if (opened.IsFailed) return opened;
                ready = await browser.Wait(page.Locator("#dialogContent #tileDetails.oasis"));
                if (ready.IsFailed) return ready;
                var animals = await FarmTargetTroopsParser.ReadOasisAnimalTroops(page, target.Href);
                if (animals.IsFailed) return Stop.Error.WithErrors(animals.Errors);
                if (!HeroOasisSettings.AllowsAnimals(settings, animals.Value))
                {
                    SkipTarget(task, target, "Animal count or allowed animals do not match.");
                    continue;
                }
                var simulator = await HeroOasisParser.SimulatorUrl(page);
                if (simulator.IsFailed) return Stop.Error.WithErrors(simulator.Errors);
                opened = await browser.Navigate(simulator.Value);
                if (opened.IsFailed) return opened;
                ready = await browser.Wait(page.Locator("#combatSimulatorForm .role.attacker select[name='village']"));
                if (ready.IsFailed) return ready;
                var attacker = page.Locator("#combatSimulatorForm .role.attacker");
                await attacker.Locator("select[name='village']").SelectOptionAsync(owner.Value.Value.ToString(CultureInfo.InvariantCulture));
                opened = await browser.Click(attacker.Locator(".villageSelection a.reload"));
                if (opened.IsFailed) return opened;
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                var filled = await HeroOasisParser.FillSimulator(page, settings);
                if (filled.IsFailed) return Skip.Error.WithErrors(filled.Errors);
                var health = await HeroOasisParser.ReadHealth(page);
                if (health.IsFailed) return Stop.Error.WithErrors(health.Errors);
                if (health.Value < settings.MinHeroHealth)
                    return Skip.Error.WithError($"Hero health {health.Value} is below minimum {settings.MinHeroHealth}. Hero clear task canceled.");
                var defenders = await HeroOasisParser.ValidateDefenders(page, animals.Value);
                if (defenders.IsFailed)
                {
                    SkipTarget(task, target, defenders.Errors[0].Message);
                    continue;
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
                var simulation = await HeroOasisParser.ReadSimulation(page, settings, health.Value, owner.Value, target.Href);
                if (simulation.IsFailed)
                {
                    SkipTarget(task, target, simulation.Errors[0].Message);
                    continue;
                }
                if (!HeroOasisSettings.AllowsResult(settings, simulation.Value))
                {
                    SkipTarget(task, target,
                        $"Simulation rejected: hero health {simulation.Value.HealthBefore} -> {simulation.Value.HealthAfter}, sub troop lost {simulation.Value.SubTroopLost}.");
                    continue;
                }

                opened = await browser.Click(page.Locator("#combatSimulator .resultActionWrapper #sendTroops"));
                if (opened.IsFailed) return opened;
                ready = await browser.Wait(page.Locator("#build .a2b form[action*='tt=2'] #troops"));
                if (ready.IsFailed) return ready;
                var availableHero = await HeroOasisParser.IsHeroHome(page);
                if (availableHero.IsFailed) return Stop.Error.WithErrors(availableHero.Errors);
                if (!availableHero.Value) return Skip.Error.WithError("Hero is no longer in the village. Hero clear task canceled.");
                var send = await HeroOasisParser.PrepareSend(page, settings, owner.Value, target.Href);
                if (send.IsFailed) return Stop.Error.WithErrors(send.Errors);
                if (!send.Value)
                {
                    SkipTarget(task, target, "Insufficient sub troops.");
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                opened = await browser.Click(page.Locator("#build .a2b form[action*='tt=2'] button#ok"));
                if (opened.IsFailed) return opened;
                ready = await browser.Wait(page.Locator("#troopSendForm #confirmSendTroops"));
                if (ready.IsFailed) return ready;
                var confirmation = await HeroOasisParser.ReadConfirmation(page, settings, owner.Value, target.Href);
                if (confirmation.IsFailed) return Stop.Error.WithErrors(confirmation.Errors);
                if (confirmation.Value <= DateTime.Now)
                    return Stop.Error.WithError("Hero return time is not in the future. Raid not confirmed.");
                cancellationToken.ThrowIfCancellationRequested();
                task.ConfirmationAttempted = true;
                // Keep this guard set on timeout/cancellation so scheduler retries cannot send twice.
                opened = await browser.Click(page.Locator("#troopSendForm #confirmSendTroops"));
                if (opened.IsFailed) return Stop.Error.WithErrors(opened.Errors);
                await page.WaitForFunctionAsync("""
                    () => new URL(location.href).searchParams.get('tt') === '1' &&
                        !!document.querySelector('.subNavi a.tabItem.active[href*="tt=1"]')
                    """);
                task.ProcessedSlots.Add(target.SlotId);
                task.HeroReturnAt = confirmation.Value;
                task.ExecuteAt = confirmation.Value.AddSeconds(5);
                task.ConfirmationAttempted = false;
                browser.Logger.Information(
                    "Hero raid sent to slot {SlotId}; next target scheduled at {ExecuteAt}",
                    target.SlotId, task.ExecuteAt);
                return Result.Ok();
            }
        }

        private void SkipTarget(HeroClearFarmOasisTask.Task task, FarmTargetSlot target, string reason)
        {
            task.ProcessedSlots.Add(target.SlotId);
            browser.Logger.Information("Hero oasis target {SlotId} skipped: {Reason}", target.SlotId, reason);
        }
    }
}
