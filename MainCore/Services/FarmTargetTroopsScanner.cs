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
            }
            if (!await FarmListNavigation.IsOpenForFarmList(page, farmId, owner.Value))
            {
                var reopened = await FarmListNavigation.Open(browser, farmUrl, owner.Value);
                if (reopened.IsFailed) return reopened;
            }
            var prepared = await FarmListPreparation.Prepare(browser, farmId, owner.Value);
            if (prepared.IsFailed) return prepared;
            var targets = await FarmTargetTroopsParser.ReadTargets(page, farmId);
            if (targets.IsFailed) return Result.Fail(targets.Errors);
            var disabled = 0;
            foreach (var target in targets.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (target.Disabled) continue;
                var saved = false;
                try
                {
                    var result = await ProcessTarget(farmId, target, farmUrl, owner.Value, cancellationToken);
                    if (result.IsFailed)
                        browser.Logger.Warning("Farm target {SlotId} skipped: {Reason}", target.SlotId, result.Errors[0].Message);
                    else saved = result.Value;
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    browser.Logger.Warning("Farm target {SlotId} check failed ({ErrorType}); not confirmed disabled",
                        target.SlotId, ex.GetType().Name);
                }

                prepared = await FarmListPreparation.Prepare(browser, farmId, owner.Value);
                if (prepared.IsFailed) return prepared;
                if (saved)
                {
                    if (await FarmTargetDeactivator.IsConfirmed(page, farmId, target))
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

        private async Task<Result<bool>> ProcessTarget(
            FarmId farmId,
            FarmTargetSlot target,
            string farmUrl,
            VillageId owner,
            CancellationToken cancellationToken)
        {
            var farmPage = browser.CurrentPage;
            var current = new Uri(farmPage.Url);
            if (!Uri.TryCreate(current, target.Href, out var targetUrl) ||
                targetUrl.Authority != current.Authority || targetUrl.Scheme != current.Scheme ||
                targetUrl.AbsolutePath != "/karte.php" ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)x=-?\d+(?:&|$)") ||
                !System.Text.RegularExpressions.Regex.IsMatch(targetUrl.Query, @"(?:^\?|&)y=-?\d+(?:&|$)"))
                return Result.Fail("Invalid target map link.");
            var row = FarmTargetTroopsParser.GetList(farmPage, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1) return Result.Fail("Target slot is missing or ambiguous.");
            if (await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')")) return Result.Ok(false);
            var link = row.Locator("td.target a");
            if (await link.GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed during scan.");

            var opened = await browser.OpenNewTab();
            if (opened.IsFailed) return Result.Fail(opened.Errors);
            Result<bool> troops = Result.Fail("Oasis troops were not checked.");
            Result closed = Result.Ok();
            try
            {
                troops = await InspectTarget(farmId, target, farmUrl, owner, cancellationToken);
            }
            finally
            {
                closed = await browser.CloseNewTab();
            }
            if (closed.IsFailed) return Result.Fail(closed.Errors);
            if (troops.IsFailed) return Result.Fail(troops.Errors);

            browser.Logger.Information("Farm target {SlotId} ({TargetName}): has troops {HasTroops}",
                target.SlotId, target.Name, troops.Value);
            if (!troops.Value) return Result.Ok(false);
            return await FarmTargetDeactivator.DeactivateFromFarmList(browser, farmId, target, cancellationToken);
        }

        private async Task<Result<bool>> InspectTarget(
            FarmId farmId,
            FarmTargetSlot target,
            string farmUrl,
            VillageId owner,
            CancellationToken cancellationToken)
        {
            var opened = await browser.Navigate(farmUrl);
            if (opened.IsFailed) return Result.Fail(opened.Errors);
            var page = browser.CurrentPage;
            var prepared = await FarmListPreparation.Prepare(browser, farmId, owner);
            if (prepared.IsFailed) return Result.Fail(prepared.Errors);
            var row = FarmTargetTroopsParser.GetList(page, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1)
                return Result.Fail("Target slot is missing or ambiguous in check tab.");
            if (await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')"))
                return Result.Ok(false);
            var link = row.Locator("td.target a");
            if (await link.GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed in check tab.");
            cancellationToken.ThrowIfCancellationRequested();
            var result = await browser.Click(link);
            if (result.IsFailed) return Result.Fail(result.Errors);
            result = await browser.Wait(page.Locator("#dialogContent #tileDetails.oasis"));
            if (result.IsFailed) return Result.Fail(result.Errors);
            var troops = await FarmTargetTroopsParser.HasTroops(page, target.Href);
            if (troops.IsFailed) return Result.Fail(troops.Errors);
            return troops;
        }
    }
}
