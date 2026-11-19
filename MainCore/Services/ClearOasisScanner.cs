namespace MainCore.Services
{
    [RegisterScoped<ClearOasisScanner>]
    public sealed class ClearOasisScanner(IChromeBrowser browser)
    {
        public sealed record ClearOasisScanResult(
            int NextTargetIndex,
            DateTime? RetryAt,
            int RaidCount,
            long RemainingSteppeRiders,
            bool PhaseLimited);

        private sealed record ProcessTargetResult(bool SentRaid, bool NeedRetry, DateTime? RetryAt, bool CheckDeactivation = false);

        private sealed class RaidDraft
        {
            public long SteppeRidersRemaining { get; set; }
            public long MarksmenRemaining { get; set; }
            public long MaraudersRemaining { get; set; }
        }

        public async Task<Result<ClearOasisScanResult>> Scan(
            FarmId farmId,
            OasisRaidSettings settings,
            int startTargetIndex,
            int maxTargetsPerPhase,
            CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var farmUrl = page.Url;
            var owner = await FarmTargetTroopsParser.ReadOwnerVillage(page, farmId);
            if (owner.IsFailed) return Result.Fail(owner.Errors);

            var uri = new UriBuilder(farmUrl);
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.StartsWith("newdid=", StringComparison.Ordinal)).ToList();
            query.Add($"newdid={owner.Value.Value}");
            uri.Query = string.Join("&", query);
            farmUrl = uri.Uri.AbsoluteUri;

            var village = VillagePanelParser.GetVillageNode(page, owner.Value);
            if (!await VillagePanelParser.IsActive(village))
            {
                var switched = await browser.Click(village);
                if (switched.IsFailed) return Result.Fail(switched.Errors);
                switched = await browser.Wait(page.Locator(
                    $"#sidebarBoxVillageList .listEntry.village.active[data-did='{owner.Value}']"));
                if (switched.IsFailed) return Result.Fail(switched.Errors);
            }
            if (!await FarmListNavigation.IsOpenForFarmList(page, farmId, owner.Value))
            {
                var reopened = await FarmListNavigation.Open(browser, farmUrl, owner.Value, preferRallyPointTab: true);
                if (reopened.IsFailed) return Result.Fail(reopened.Errors);
            }

            var prepared = await FarmListPreparation.Prepare(browser, farmId, owner.Value);
            if (prepared.IsFailed) return Result.Fail(prepared.Errors);

            var sendTroopsUrl = FarmTargetTroopsParser.BuildRallyPointTabUrl(farmUrl, 2);
            var draftResult = await ReadSteppeRiderDraft(sendTroopsUrl, farmUrl, farmId, owner.Value);
            if (draftResult.IsFailed) return Result.Fail(draftResult.Errors);
            var draft = draftResult.Value;

            var targets = await FarmTargetTroopsParser.ReadTargets(page, farmId);
            if (targets.IsFailed) return Result.Fail(targets.Errors);
            if (targets.Value.Count == 0)
                return Result.Ok(new ClearOasisScanResult(0, null, 0, draft.SteppeRidersRemaining, false));
            if (startTargetIndex >= targets.Value.Count)
                return Result.Ok(new ClearOasisScanResult(0, null, 0, draft.SteppeRidersRemaining, false));

            var index = Math.Max(0, startTargetIndex);
            var pendingReturns = new List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)>();

            var raids = 0;
            for (var i = index; i < targets.Value.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = targets.Value[i];
                if (target.Disabled) continue;
                var checkDeactivation = false;
                try
                {
                    var result = await ProcessTarget(
                        farmId,
                        target,
                        settings,
                        draft,
                        pendingReturns,
                        cancellationToken);
                    if (result.IsFailed)
                        browser.Logger.Warning("Oasis ({Coordinates}) skipped. {Reason}", target.Coordinates, result.Errors[0].Message);
                    else
                    {
                        checkDeactivation = result.Value.CheckDeactivation;
                        if (result.Value.SentRaid) raids++;
                        if (result.Value.NeedRetry)
                        {
                            var retryAt = result.Value.RetryAt;
                            if (retryAt is null)
                            {
                                retryAt = DateTime.Now.AddMinutes(30);
                                browser.Logger.Warning(
                                    "Oasis ({Coordinates}): troop return time unavailable, fallback retry at {RetryAt:yyyy-MM-dd HH:mm:ss}",
                                    target.Coordinates, retryAt.Value);
                            }
                            return Result.Ok(new ClearOasisScanResult(
                                i,
                                retryAt,
                                raids,
                                draft.SteppeRidersRemaining,
                                false));
                        }
                    }
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    browser.Logger.Warning(ex, "Oasis ({Coordinates}) failed ({ErrorType})",
                        target.Coordinates, ex.GetType().Name);
                }
                prepared = await FarmListPreparation.Prepare(browser, farmId, owner.Value);
                if (prepared.IsFailed) return Result.Fail(prepared.Errors);
                if (checkDeactivation)
                {
                    if (await FarmTargetDeactivator.IsConfirmed(page, farmId, target))
                        browser.Logger.Information("Oasis ({Coordinates}): deactivation confirmed", target.Coordinates);
                    else
                        browser.Logger.Warning("Oasis ({Coordinates}): deactivation was not confirmed", target.Coordinates);
                }
                if (maxTargetsPerPhase > 0 && raids >= maxTargetsPerPhase && i + 1 < targets.Value.Count)
                    return Result.Ok(new ClearOasisScanResult(
                        i + 1,
                        null,
                        raids,
                        draft.SteppeRidersRemaining,
                        true));
            }

            browser.Logger.Information(
                "Clear oasis completed: list {FarmListId}, {TargetCount} targets, {RaidCount} raids, {RemainingSteppe} Steppe Riders, {RemainingMarksmen} Marksmen, {RemainingMarauders} Marauders",
                farmId.Value, targets.Value.Count, raids,
                draft.SteppeRidersRemaining, draft.MarksmenRemaining, draft.MaraudersRemaining);
            return Result.Ok(new ClearOasisScanResult(0, null, raids, draft.SteppeRidersRemaining, false));
        }

        private async Task<Result<RaidDraft>> ReadSteppeRiderDraft(
            string sendTroopsUrl,
            string farmUrl,
            FarmId farmId,
            VillageId owner)
        {
            var opened = await browser.Navigate(sendTroopsUrl);
            if (opened.IsFailed) return Result.Fail(opened.Errors);
            var ready = await browser.Wait(browser.CurrentPage.Locator("#build .a2b form[action*='tt=2']"));
            if (ready.IsFailed) return Result.Fail(ready.Errors);
            var parsed = await FarmTargetTroopsParser.ReadSteppeRiderDraft(browser.CurrentPage);
            if (parsed.IsFailed) return Result.Fail(parsed.Errors);
            browser.Logger.Information(
                "Troop draft saved for clear oasis list {FarmListId}: Steppe Riders {Steppe}, Marksmen {Marksmen}, Marauders {Marauders}",
                farmId.Value, parsed.Value.AvailableSteppeRiders, parsed.Value.AvailableMarksmen, parsed.Value.AvailableMarauders);
            var restored = await FarmListNavigation.Open(browser, farmUrl, owner, preferRallyPointTab: true);
            if (restored.IsFailed) return Result.Fail(restored.Errors);
            var prepared = await FarmListPreparation.Prepare(browser, farmId, owner);
            if (prepared.IsFailed) return Result.Fail(prepared.Errors);
            return Result.Ok(new RaidDraft
            {
                SteppeRidersRemaining = parsed.Value.AvailableSteppeRiders,
                MarksmenRemaining = parsed.Value.AvailableMarksmen,
                MaraudersRemaining = parsed.Value.AvailableMarauders,
            });
        }

        private async Task<Result<ProcessTargetResult>> ProcessTarget(
            FarmId farmId,
            FarmTargetSlot target,
            OasisRaidSettings settings,
            RaidDraft draft,
            List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)> pendingReturns,
            CancellationToken cancellationToken)
        {
            var farmPage = browser.CurrentPage;
            var targetRow = FarmTargetTroopsParser.GetList(farmPage, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await targetRow.CountAsync() != 1) return Result.Fail("Target slot is missing or ambiguous.");
            if (await targetRow.EvaluateAsync<bool>("node => node.classList.contains('disabled')"))
                return Result.Ok(new ProcessTargetResult(false, false, null));
            if (await targetRow.Locator("td.target a").GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed during scan.");

            var result = await FarmTargetOasisTab.Run(browser, target.Href, targetRow.Locator("td.target a"), page =>
                ProcessTargetInOasisTab(page, target, settings, draft, pendingReturns, cancellationToken));
            if (result.IsFailed) return Result.Fail(result.Errors);
            if (!result.Value.CheckDeactivation) return result;

            var deactivated = await FarmTargetDeactivator.DeactivateFromFarmList(
                browser, farmId, target, cancellationToken);
            if (deactivated.IsFailed) return Result.Fail(deactivated.Errors);
            return result;
        }

        private async Task<Result<ProcessTargetResult>> ProcessTargetInOasisTab(
            IPage page,
            FarmTargetSlot target,
            OasisRaidSettings settings,
            RaidDraft draft,
            List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)> pendingReturns,
            CancellationToken cancellationToken)
        {
            var troops = await FarmTargetTroopsParser.ReadOasisAnimalTroops(page, target.Href);
            if (troops.IsFailed) return Result.Fail(troops.Errors);
            var animalCount = troops.Value.Sum(x => x.Count);
            if (settings.LimitAnimal > 0 && animalCount > settings.LimitAnimal)
            {
                browser.Logger.Information(
                    "Oasis ({Coordinates}): {AnimalCount} animals exceed limit {AnimalLimit}; checking deactivation",
                    target.Coordinates, animalCount, settings.LimitAnimal);
                return Result.Ok(new ProcessTargetResult(false, false, null, true));
            }
            var blocked = troops.Value.Where(x => x.Count > 0 && !settings.AllowedAnimals.Contains(x.UnitId))
                .Select(x => x.Name).Distinct().OrderBy(x => x).ToList();
            if (blocked.Count > 0)
            {
                browser.Logger.Information(
                    "Oasis ({Coordinates}): blocked animals [{Blocked}]; checking deactivation",
                    target.Coordinates, string.Join(", ", blocked));
                return Result.Ok(new ProcessTargetResult(false, false, null, true));
            }

            var clearable = troops.Value.Where(x => x.Count > 0 && settings.AllowedAnimals.Contains(x.UnitId)).ToList();
            var clearableCount = clearable.Sum(x => x.Count);
            var required = settings.RequiredTroops(clearableCount);
            if (required == (0L, 0L, 0L))
            {
                browser.Logger.Information(
                    "Oasis ({Coordinates}) skipped. Empty oasis raid disabled.", target.Coordinates);
                return Result.Ok(new ProcessTargetResult(false, false, null));
            }

            var openRaid = await OpenRaidUnoccupiedOasisFromDialog(target.Href);
            if (openRaid.IsFailed) return Result.Fail(openRaid.Errors);
            var raid = await SendRaidFromCurrentPage(required.Steppe, required.Marksmen, required.Marauders, settings, cancellationToken);
            if (raid.IsFailed) return Result.Fail(raid.Errors);
            draft.SteppeRidersRemaining = raid.Value.RemainingSteppeRiders;
            draft.MarksmenRemaining = raid.Value.RemainingMarksmen;
            draft.MaraudersRemaining = raid.Value.RemainingMarauders;
            if (!raid.Value.Sent)
            {
                var canRaidEmptyOasis = draft.SteppeRidersRemaining >= settings.RaidSteppeRiders &&
                    draft.MarksmenRemaining >= settings.RaidMarksmen &&
                    draft.MaraudersRemaining >= settings.RaidMarauders;
                browser.Logger.Information(
                    "Oasis ({Coordinates}): troop availability, required Steppe {RequiredSteppe}, required Marksmen {RequiredMarksmen}, required Marauders {RequiredMarauders}, real-time Steppe {RemainingSteppe}, real-time Marksmen {RemainingMarksmen}, real-time Marauders {RemainingMarauders}",
                    target.Coordinates,
                    raid.Value.RequiredSteppeRiders, raid.Value.RequiredMarksmen, raid.Value.RequiredMarauders,
                    draft.SteppeRidersRemaining, draft.MarksmenRemaining, draft.MaraudersRemaining);
                if (canRaidEmptyOasis)
                {
                    browser.Logger.Information(
                        "Oasis ({Coordinates}) skipped. Not enough attack troops; continuing to the next target.",
                        target.Coordinates);
                    return Result.Ok(new ProcessTargetResult(false, false, null));
                }
                var retryAt = ResolveRetryAt(
                    settings.RaidSteppeRiders,
                    settings.RaidMarksmen,
                    settings.RaidMarauders,
                    draft.SteppeRidersRemaining,
                    draft.MarksmenRemaining,
                    draft.MaraudersRemaining,
                    pendingReturns);
                browser.Logger.Information(
                    "Oasis ({Coordinates}): insufficient troops for the configured empty oasis raid; scheduling a retry",
                    target.Coordinates);
                return Result.Ok(new ProcessTargetResult(false, true, retryAt));
            }
            if (raid.Value.ReturnAt is DateTime returnAt)
                pendingReturns.Add((raid.Value.RequiredSteppeRiders, raid.Value.RequiredMarksmen, raid.Value.RequiredMarauders, returnAt));
            browser.Logger.Information(
                "Oasis ({Coordinates}): sent Steppe {SentSteppe}, Marksmen {SentMarksmen}, Marauders {SentMarauders}, remaining Steppe {RemainingSteppe}, remaining Marksmen {RemainingMarksmen}, remaining Marauders {RemainingMarauders}",
                target.Coordinates,
                raid.Value.RequiredSteppeRiders, raid.Value.RequiredMarksmen, raid.Value.RequiredMarauders,
                draft.SteppeRidersRemaining, draft.MarksmenRemaining, draft.MaraudersRemaining);
            return Result.Ok(new ProcessTargetResult(true, false, null));
        }

        private static DateTime? ResolveRetryAt(
            long requiredSteppe,
            long requiredMarksmen,
            long requiredMarauders,
            long availableSteppeNow,
            long availableMarksmenNow,
            long availableMaraudersNow,
            List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)> pendingReturns)
        {
            var steppeDeficit = requiredSteppe - availableSteppeNow;
            var marksmenDeficit = requiredMarksmen - availableMarksmenNow;
            var maraudersDeficit = requiredMarauders - availableMaraudersNow;
            if (steppeDeficit <= 0 && marksmenDeficit <= 0 && maraudersDeficit <= 0)
                return DateTime.Now;

            long steppeCumulative = 0;
            long marksmenCumulative = 0;
            long maraudersCumulative = 0;
            foreach (var (steppe, marksmen, marauders, returnAt) in pendingReturns.OrderBy(x => x.ReturnAt))
            {
                if (returnAt <= DateTime.Now) continue;
                steppeCumulative += steppe;
                marksmenCumulative += marksmen;
                maraudersCumulative += marauders;
                if (steppeCumulative >= Math.Max(0, steppeDeficit) &&
                    marksmenCumulative >= Math.Max(0, marksmenDeficit) &&
                    maraudersCumulative >= Math.Max(0, maraudersDeficit))
                    return returnAt;
            }
            return pendingReturns
                .Where(x => x.ReturnAt > DateTime.Now)
                .OrderBy(x => x.ReturnAt)
                .Select(x => (DateTime?)x.ReturnAt)
                .FirstOrDefault();
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
                // Prefer the exact oasis raid action to avoid matching simulate raid.
                let raid = tile.querySelector(
                    '.detailImage .options .option a[href*=""/build.php""][href*=""gid=16""][href*=""tt=2""][href*=""eventType=4""]');
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
            var ready = await browser.Wait(page.Locator("#build .a2b form[action*='tt=2']"));
            if (ready.IsFailed) return ready;
            return Result.Ok();
        }

        private async Task<Result<(bool Sent,
            long RemainingSteppeRiders,
            long RemainingMarksmen,
            long RemainingMarauders,
            DateTime? ReturnAt,
            long RequiredSteppeRiders,
            long RequiredMarksmen,
            long RequiredMarauders)>> SendRaidFromCurrentPage(
            long requiredSteppe,
            long requiredMarksmen,
            long requiredMarauders,
            OasisRaidSettings settings,
            CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var ready = await browser.Wait(page.Locator("#build .a2b form[action*='tt=2']"));
            if (ready.IsFailed) return Result.Fail(ready.Errors);
            var draft = await FarmTargetTroopsParser.ReadSteppeRiderDraft(page);
            if (draft.IsFailed) return Result.Fail(draft.Errors);
            var availableSteppe = draft.Value.AvailableSteppeRiders;
            var availableMarksmen = draft.Value.AvailableMarksmen;
            var availableMarauders = draft.Value.AvailableMarauders;

            if (availableSteppe < settings.RaidSteppeRiders ||
                availableMarksmen < settings.RaidMarksmen ||
                availableMarauders < settings.RaidMarauders ||
                availableSteppe < requiredSteppe ||
                availableMarksmen < requiredMarksmen ||
                availableMarauders < requiredMarauders)
                return Result.Ok((
                    false,
                    availableSteppe, availableMarksmen, availableMarauders,
                    (DateTime?)null,
                    requiredSteppe, requiredMarksmen, requiredMarauders));

            cancellationToken.ThrowIfCancellationRequested();
            var form = page.Locator("#build .a2b form[action*='tt=2']");
            foreach (var (troop, amount) in new[]
                { (4, requiredSteppe), (5, requiredMarksmen), (6, requiredMarauders) })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var input = form.Locator($"table#troops input[name='troop[t{troop}]']");
                if (await input.CountAsync() != 1)
                    return Result.Fail($"Oasis raid troop field t{troop} is missing or ambiguous.");
                if (await input.IsDisabledAsync())
                {
                    if (amount > 0) return Result.Fail($"Oasis raid troop field t{troop} is disabled.");
                    continue;
                }
                var filled = await FarmTargetTroopsParser.FillSendTroopInput(browser, input, amount);
                if (filled.IsFailed) return Result.Fail(filled.Errors);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var sent = await browser.Click(page.Locator("#build .a2b form[action*='tt=2'] button#ok[type='submit']"));
            if (sent.IsFailed) return Result.Fail(sent.Errors);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var returnAt = await TryReadReturnTime(page);
            var confirm = page.Locator("#troopSendForm button#confirmSendTroops, #troopSendForm button[name='s1']");
            if (await confirm.CountAsync() > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var done = await browser.Click(confirm.First);
                if (done.IsFailed) return Result.Fail(done.Errors);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
            return Result.Ok((
                true,
                availableSteppe - requiredSteppe,
                availableMarksmen - requiredMarksmen,
                availableMarauders - requiredMarauders,
                returnAt,
                requiredSteppe,
                requiredMarksmen,
                requiredMarauders));
        }

        private static async Task<DateTime?> TryReadReturnTime(IPage page)
        {
            var snapshot = await page.EvaluateAsync<System.Text.Json.JsonElement>(@"() => {
                const at = document.querySelector('#troopSendForm #at.timer');
                const inText = document.querySelector('#troopSendForm #in.in');
                if (!at || !inText) return null;
                return {
                    arrival: at.getAttribute('value') || '',
                    inText: (inText.textContent || '').trim()
                };
            }");
            if (snapshot.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            var arrivalText = snapshot.GetProperty("arrival").GetString() ?? "";
            if (!long.TryParse(arrivalText, out var arrivalUnix) || arrivalUnix <= 0) return null;
            var inText = snapshot.GetProperty("inText").GetString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(inText, @"(?<dur>\d{1,2}:\d{2}:\d{2})");
            if (!match.Success) return null;
            if (!TimeSpan.TryParse(match.Groups["dur"].Value, System.Globalization.CultureInfo.InvariantCulture, out var duration))
                return null;
            var arrival = DateTimeOffset.FromUnixTimeSeconds(arrivalUnix).LocalDateTime;
            return arrival.Add(duration);
        }
    }
}
