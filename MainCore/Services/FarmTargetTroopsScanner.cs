namespace MainCore.Services
{
    [RegisterScoped<FarmTargetTroopsScanner>]
    public sealed class FarmTargetTroopsScanner(IChromeBrowser browser)
    {
        public async Task<Result> Scan(FarmId farmId, CancellationToken cancellationToken)
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
                if (switched.IsFailed) return switched;
                switched = await browser.Wait(page.Locator(
                    $"#sidebarBoxVillageList .listEntry.village.active[data-did='{owner.Value}']"));
                if (switched.IsFailed) return switched;
                var reopened = await browser.Navigate(farmUrl);
                if (reopened.IsFailed) return reopened;
            }
            var prepared = await PrepareList(farmId, owner.Value);
            if (prepared.IsFailed) return prepared;
            var list = FarmTargetTroopsParser.GetList(page, farmId);
            if (await list.CountAsync() != 1) return Result.Fail("Selected farm list not found.");
            if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
            {
                var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                if (expanded.IsFailed) return expanded;
            }
            var ready = await browser.Wait(list.Locator("table.slots"));
            if (ready.IsFailed) return ready;
            var targets = await FarmTargetTroopsParser.ReadTargets(page, farmId);
            if (targets.IsFailed) return Result.Fail(targets.Errors);
            var disabled = 0;
            foreach (var target in targets.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (target.Disabled) continue;
                var saved = false;
                Result restored = Result.Ok();
                try
                {
                    var result = await ProcessTarget(farmId, target, cancellationToken);
                    if (result.IsFailed)
                        browser.Logger.Warning("Farm target {SlotId} skipped: {Reason}", target.SlotId, result.Errors[0].Message);
                    else saved = result.Value;
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    browser.Logger.Warning("Farm target {SlotId} check failed ({ErrorType}); not confirmed disabled",
                        target.SlotId, ex.GetType().Name);
                }
                finally
                {
                    restored = await browser.Navigate(farmUrl);
                    if (restored.IsFailed)
                        browser.Logger.Warning("Unable to restore farm list after target {SlotId}: {Reason}",
                            target.SlotId, restored.Errors[0].Message);
                }
                if (restored.IsFailed) return restored;
                prepared = await PrepareList(farmId, owner.Value);
                if (prepared.IsFailed) return prepared;
                // Reacquire rows after dialogs/navigation; never click a stale target.
                if (!await list.EvaluateAsync<bool>("node => node.classList.contains('expanded')"))
                {
                    var expanded = await browser.Click(list.Locator(".farmListHeader a.expandCollapse"));
                    if (expanded.IsFailed) return expanded;
                }
                ready = await browser.Wait(list.Locator("table.slots"));
                if (ready.IsFailed) return ready;
                if (saved)
                {
                    var row = list.Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
                    if (await row.CountAsync() == 1 &&
                        await row.Locator("td.target a").GetAttributeAsync("href") == target.Href &&
                        await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')"))
                    {
                        disabled++;
                        browser.Logger.Information("Farm target {SlotId} disabled in list {FarmListId}", target.SlotId, farmId.Value);
                    }
                    else browser.Logger.Warning("Farm target {SlotId}: saved deactivation was not confirmed", target.SlotId);
                }
            }
            browser.Logger.Information("Farm target troops checked: list {FarmListId}, {TargetCount} targets, {DisabledCount} disabled",
                farmId.Value, targets.Value.Count, disabled);
            return Result.Ok();
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

        private async Task<Result<bool>> ProcessTarget(FarmId farmId, FarmTargetSlot target, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var current = new Uri(page.Url);
            if (!Uri.TryCreate(current, target.Href, out var targetUrl) ||
                targetUrl.Authority != current.Authority || targetUrl.Scheme != current.Scheme ||
                targetUrl.AbsolutePath != "/karte.php" ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)x=-?\d+(?:&|$)") ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)y=-?\d+(?:&|$)"))
                return Result.Fail("Invalid target map link.");
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1) return Result.Fail("Target slot is missing or ambiguous.");
            if (await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')")) return Result.Ok(false);
            var link = row.Locator("td.target a");
            if (await link.GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed during scan.");
            var result = await browser.Click(link);
            if (result.IsFailed) return Result.Fail(result.Errors);
            result = await browser.Wait(page.Locator("#dialogContent #tileDetails"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            var troops = await FarmTargetTroopsParser.HasTroops(page, target.Href);
            if (troops.IsFailed) return Result.Fail(troops.Errors);
            browser.Logger.Information("Farm target {SlotId} ({TargetName}): has troops {HasTroops}",
                target.SlotId, target.Name, troops.Value);
            if (!troops.Value) return Result.Ok(false);
            var edit = await FarmTargetTroopsParser.FindEditLink(page, farmId, target.SlotId);
            if (edit.IsFailed) return Result.Fail(edit.Errors);
            result = await browser.Click(edit.Value);
            if (result.IsFailed) return Result.Fail(result.Errors);
            result = await browser.Wait(page.Locator("#farmListTargetForm"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            var selected = await OffensiveReportParser.ReadSelectedFarmList(page);
            if (selected.IsFailed) return Result.Fail(selected.Errors);
            if (selected.Value != farmId.Value.ToString())
                return Result.Fail("Edit dialog selected a different farm list.");
            var matches = await page.Locator("#farmListTargetForm").EvaluateAsync<bool>(@"(form, href) => {
                const url = new URL(href, location.href);
                return form.querySelector('input[name=""x""]')?.value === url.searchParams.get('x') &&
                    form.querySelector('input[name=""y""]')?.value === url.searchParams.get('y');
            }", target.Href);
            if (!matches) return Result.Fail("Edit dialog target coordinates do not match.");
            cancellationToken.ThrowIfCancellationRequested();
            var checkbox = page.Locator("#farmListTargetForm .deactivateSlot input[name='isActive']");
            if (await checkbox.IsCheckedAsync()) return Result.Ok(false);
            await checkbox.CheckAsync(new LocatorCheckOptions { Timeout = 3000 });
            result = await browser.Click(page.Locator("#farmListTargetForm .actionButtons button.save[type='submit']"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            await page.Locator("#farmListTargetForm").WaitForAsync(new LocatorWaitForOptions
                { State = WaitForSelectorState.Hidden, Timeout = 5000 });
            return Result.Ok(true);
        }
    }
}
