namespace MainCore.Services
{
    [RegisterScoped<ClearOasisScanner>]
    public sealed class ClearOasisScanner(IChromeBrowser browser)
    {
        public sealed record ClearOasisScanResult(
            int NextTargetIndex,
            DateTime? RetryAt,
            int RaidCount,
            long RemainingSteppeRiders);

        private sealed record ProcessTargetResult(bool SentRaid, bool NeedRetry, DateTime? RetryAt);

        private sealed class RaidDraft
        {
            public long SteppeRidersRemaining { get; set; }
            public long MarksmenRemaining { get; set; }
            public long MaraudersRemaining { get; set; }
            public double SafetyFactor { get; set; }
        }

        public async Task<Result<ClearOasisScanResult>> Scan(
            FarmId farmId,
            IReadOnlySet<int> allowedOasisAnimals,
            int safetyFactor,
            int startTargetIndex,
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
                var reopened = await browser.Navigate(farmUrl);
                if (reopened.IsFailed) return Result.Fail(reopened.Errors);
            }

            var prepared = await PrepareList(farmId, owner.Value);
            if (prepared.IsFailed) return Result.Fail(prepared.Errors);
            var list = FarmTargetTroopsParser.GetList(page, farmId);
            if (await list.CountAsync() != 1) return Result.Fail("Selected farm list not found.");

            var sendTroopsUrl = FarmTargetTroopsParser.BuildRallyPointTabUrl(farmUrl, 2);
            var draftResult = await ReadSteppeRiderDraft(sendTroopsUrl, farmUrl, farmId, owner.Value, safetyFactor);
            if (draftResult.IsFailed) return Result.Fail(draftResult.Errors);
            var draft = draftResult.Value;

            if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
            {
                var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                if (expanded.IsFailed) return Result.Fail(expanded.Errors);
            }

            var ready = await browser.Wait(list.Locator("table.slots"));
            if (ready.IsFailed) return Result.Fail(ready.Errors);

            var targets = await FarmTargetTroopsParser.ReadTargets(page, farmId);
            if (targets.IsFailed) return Result.Fail(targets.Errors);
            if (targets.Value.Count == 0)
                return Result.Ok(new ClearOasisScanResult(0, null, 0, draft.SteppeRidersRemaining));
            if (startTargetIndex >= targets.Value.Count)
                return Result.Ok(new ClearOasisScanResult(0, null, 0, draft.SteppeRidersRemaining));

            var index = Math.Max(0, startTargetIndex);
            var pendingReturns = new List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)>();

            var raids = 0;
            for (var i = index; i < targets.Value.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = targets.Value[i];
                if (target.Disabled) continue;
                Result restored = Result.Ok();
                try
                {
                    var result = await ProcessTarget(
                        farmId,
                        target,
                        allowedOasisAnimals,
                        draft,
                        pendingReturns,
                        cancellationToken);
                    if (result.IsFailed)
                        browser.Logger.Warning("Clear oasis target {SlotId} skipped: {Reason}", target.SlotId, result.Errors[0].Message);
                    else
                    {
                        if (result.Value.SentRaid) raids++;
                        if (result.Value.NeedRetry)
                        {
                            var retryAt = result.Value.RetryAt;
                            if (retryAt is null)
                            {
                                retryAt = DateTime.Now.AddMinutes(30);
                                browser.Logger.Warning(
                                    "Clear oasis target {SlotId}: troop return time unavailable, fallback retry at {RetryAt}",
                                    target.SlotId, retryAt.Value);
                            }
                            return Result.Ok(new ClearOasisScanResult(
                                i,
                                retryAt,
                                raids,
                                draft.SteppeRidersRemaining));
                        }
                    }
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    browser.Logger.Warning("Clear oasis target {SlotId} failed ({ErrorType})",
                        target.SlotId, ex.GetType().Name);
                }
                finally
                {
                    restored = await browser.Navigate(farmUrl);
                    if (restored.IsFailed)
                        browser.Logger.Warning("Unable to restore farm list after clear target {SlotId}: {Reason}",
                            target.SlotId, restored.Errors[0].Message);
                }

                if (restored.IsFailed) return Result.Fail(restored.Errors);
                prepared = await PrepareList(farmId, owner.Value);
                if (prepared.IsFailed) return Result.Fail(prepared.Errors);

                if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
                {
                    var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                    if (expanded.IsFailed) return Result.Fail(expanded.Errors);
                }
                ready = await browser.Wait(list.Locator("table.slots"));
                if (ready.IsFailed) return Result.Fail(ready.Errors);
            }

            browser.Logger.Information(
                "Clear oasis completed: list {FarmListId}, {TargetCount} targets, {RaidCount} raids, {RemainingSteppe} Steppe Riders, {RemainingMarksmen} Marksmen, {RemainingMarauders} Marauders",
                farmId.Value, targets.Value.Count, raids,
                draft.SteppeRidersRemaining, draft.MarksmenRemaining, draft.MaraudersRemaining);
            return Result.Ok(new ClearOasisScanResult(0, null, raids, draft.SteppeRidersRemaining));
        }

        private async Task<Result<RaidDraft>> ReadSteppeRiderDraft(
            string sendTroopsUrl,
            string farmUrl,
            FarmId farmId,
            VillageId owner,
            int safetyFactor)
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
            var restored = await browser.Navigate(farmUrl);
            if (restored.IsFailed) return Result.Fail(restored.Errors);
            var prepared = await PrepareList(farmId, owner);
            if (prepared.IsFailed) return Result.Fail(prepared.Errors);
            return Result.Ok(new RaidDraft
            {
                SteppeRidersRemaining = parsed.Value.AvailableSteppeRiders,
                MarksmenRemaining = parsed.Value.AvailableMarksmen,
                MaraudersRemaining = parsed.Value.AvailableMarauders,
                SafetyFactor = Math.Max(1, safetyFactor),
            });
        }

        private async Task<Result> PrepareList(FarmId farmId, VillageId owner)
        {
            var page = browser.CurrentPage;
            if (await VillagePanelParser.GetCurrentVillageId(page) != owner)
                return Result.Fail("Farm list owner village switch was not confirmed.");
            var resolved = await FarmTargetTroopsParser.ReadOwnerVillage(page, farmId);
            if (resolved.IsFailed) return Result.Fail(resolved.Errors);
            if (resolved.Value != owner) return Result.Fail("Farm list owner changed during scan.");
            var header = page.Locator(
                $"#rallyPointFarmList .villageWrapper:has(.farmListHeader [data-list='{farmId.Value}']) > .villageHeader");
            if (await header.CountAsync() != 1) return Result.Fail("Farm list village header is ambiguous.");
            if (await header.EvaluateAsync<bool>("node => node.classList.contains('collapsed')"))
            {
                var expanded = await browser.Click(header.Locator("a.expandCollapse"));
                if (expanded.IsFailed) return expanded;
            }
            return Result.Ok();
        }

        private async Task<Result<ProcessTargetResult>> ProcessTarget(
            FarmId farmId,
            FarmTargetSlot target,
            IReadOnlySet<int> allowedOasisAnimals,
            RaidDraft draft,
            List<(long Steppe, long Marksmen, long Marauders, DateTime ReturnAt)> pendingReturns,
            CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var current = new Uri(page.Url);
            if (!Uri.TryCreate(current, target.Href, out var targetUrl) ||
                targetUrl.Authority != current.Authority || targetUrl.Scheme != current.Scheme ||
                targetUrl.AbsolutePath != "/karte.php" ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)x=-?\d+(?:&|$)") ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)y=-?\d+(?:&|$)"))
                return Result.Fail("Invalid target map link.");

            var targetRow = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await targetRow.CountAsync() != 1) return Result.Fail("Target slot is missing or ambiguous.");
            if (await targetRow.EvaluateAsync<bool>("node => node.classList.contains('disabled')"))
                return Result.Ok(new ProcessTargetResult(false, false, null));

            var link = targetRow.Locator("td.target a");
            if (await link.GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed during scan.");

            var result = await browser.Click(link);
            if (result.IsFailed) return Result.Fail(result.Errors);
            result = await browser.Wait(page.Locator("#dialogContent #tileDetails"));
            if (result.IsFailed) return Result.Fail(result.Errors);

            var troops = await FarmTargetTroopsParser.ReadOasisAnimalTroops(page, target.Href);
            if (troops.IsFailed) return Result.Fail(troops.Errors);
            var blocked = troops.Value.Where(x => x.Count > 0 && !allowedOasisAnimals.Contains(x.UnitId))
                .Select(x => x.Name).Distinct().OrderBy(x => x).ToList();
            if (blocked.Count > 0)
            {
                browser.Logger.Information(
                    "Clear oasis target {SlotId}: blocked animals [{Blocked}], skipped",
                    target.SlotId, string.Join(", ", blocked));
                return Result.Ok(new ProcessTargetResult(false, false, null));
            }

            var clearable = troops.Value.Where(x => x.Count > 0 && allowedOasisAnimals.Contains(x.UnitId)).ToList();
            var clearableCount = clearable.Sum(x => x.Count);
            var isEmptyOasis = clearableCount <= 0;

            var required = isEmptyOasis ? 0 : (long)Math.Ceiling(clearableCount * draft.SafetyFactor);
            if (!isEmptyOasis && required <= 0) return Result.Ok(new ProcessTargetResult(false, false, null));

            var openRaid = await OpenRaidUnoccupiedOasisFromDialog(target.Href);
            if (openRaid.IsFailed) return Result.Fail(openRaid.Errors);
            var raid = await SendSteppeRiderRaidFromCurrentPage(required, isEmptyOasis, cancellationToken);
            if (raid.IsFailed) return Result.Fail(raid.Errors);
            draft.SteppeRidersRemaining = raid.Value.RemainingSteppeRiders;
            draft.MarksmenRemaining = raid.Value.RemainingMarksmen;
            draft.MaraudersRemaining = raid.Value.RemainingMarauders;
            if (!raid.Value.Sent)
            {
                var retryAt = ResolveRetryAt(
                    raid.Value.RequiredSteppeRiders,
                    raid.Value.RequiredMarksmen,
                    raid.Value.RequiredMarauders,
                    raid.Value.RemainingSteppeRiders,
                    raid.Value.RemainingMarksmen,
                    raid.Value.RemainingMarauders,
                    pendingReturns);
                browser.Logger.Information(
                    "Clear oasis target {SlotId}: insufficient troops, required Steppe {RequiredSteppe}, required Marksmen {RequiredMarksmen}, required Marauders {RequiredMarauders}, real-time Steppe {RemainingSteppe}, real-time Marksmen {RemainingMarksmen}, real-time Marauders {RemainingMarauders}",
                    target.SlotId,
                    raid.Value.RequiredSteppeRiders, raid.Value.RequiredMarksmen, raid.Value.RequiredMarauders,
                    draft.SteppeRidersRemaining, draft.MarksmenRemaining, draft.MaraudersRemaining);
                return Result.Ok(new ProcessTargetResult(false, true, retryAt));
            }
            if (raid.Value.ReturnAt is DateTime returnAt)
                pendingReturns.Add((raid.Value.RequiredSteppeRiders, raid.Value.RequiredMarksmen, raid.Value.RequiredMarauders, returnAt));
            browser.Logger.Information(
                "Clear oasis target {SlotId}: sent Steppe {SentSteppe}, Marksmen {SentMarksmen}, Marauders {SentMarauders}, remaining Steppe {RemainingSteppe}, remaining Marksmen {RemainingMarksmen}, remaining Marauders {RemainingMarauders}",
                target.SlotId,
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
                return DateTime.Now.AddSeconds(5);

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
                const tile = document.querySelector('#dialogContent #tileDetails.oasis');
                if (!tile) return false;
                const center = tile.querySelector('a[data-centermap]');
                if (!center) return false;
                const expected = new URL(href, location.href);
                const actual = new URL(center.getAttribute('href'), location.href);
                if (actual.searchParams.get('x') !== expected.searchParams.get('x') ||
                    actual.searchParams.get('y') !== expected.searchParams.get('y')) return false;
                // Prefer the exact oasis raid action to avoid matching "simulate raid".
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
            long RequiredMarauders)>> SendSteppeRiderRaidFromCurrentPage(
            long amount,
            bool isEmptyOasis,
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

            var requiredSteppe = amount;
            var requiredMarksmen = 0L;
            var requiredMarauders = 0L;
            if (isEmptyOasis)
            {
                if (availableSteppe >= 1 && availableMarksmen >= 1 && availableMarauders >= 1)
                {
                    requiredSteppe = 1;
                    requiredMarksmen = 1;
                    requiredMarauders = 1;
                }
                else if (availableSteppe >= 1 && availableMarksmen >= 1)
                {
                    requiredSteppe = 1;
                    requiredMarksmen = 1;
                    requiredMarauders = 0;
                }
                else
                {
                    requiredSteppe = 2;
                    requiredMarksmen = 0;
                    requiredMarauders = 0;
                }
            }

            if (availableSteppe < requiredSteppe ||
                availableMarksmen < requiredMarksmen ||
                availableMarauders < requiredMarauders)
                return Result.Ok((
                    false,
                    availableSteppe, availableMarksmen, availableMarauders,
                    null,
                    requiredSteppe, requiredMarksmen, requiredMarauders));

            cancellationToken.ThrowIfCancellationRequested();
            var filled = await page.EvaluateAsync<bool>(@"draft => {
                const form = document.querySelector('#build .a2b form[action*=""tt=2""]');
                if (!form) return false;
                const steppe = form.querySelector('table#troops input[name=""troop[t4]""]');
                const marksman = form.querySelector('table#troops input[name=""troop[t5]""]');
                const marauder = form.querySelector('table#troops input[name=""troop[t6]""]');
                if (!steppe || steppe.disabled) return false;
                if (draft.marksman > 0 && (!marksman || marksman.disabled)) return false;
                if (draft.marauder > 0 && (!marauder || marauder.disabled)) return false;
                steppe.value = String(draft.steppe);
                if (marksman) marksman.value = String(draft.marksman);
                if (marauder) marauder.value = String(draft.marauder);
                return !!form.querySelector('button#ok[type=""submit""]');
            }", new { steppe = requiredSteppe, marksman = requiredMarksmen, marauder = requiredMarauders });
            if (!filled) return Result.Fail("Unable to prepare Steppe Rider raid form.");

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
