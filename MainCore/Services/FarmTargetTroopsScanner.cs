namespace MainCore.Services
{
    [RegisterScoped<FarmTargetTroopsScanner>]
    public sealed class FarmTargetTroopsScanner(IChromeBrowser browser)
    {
        public async Task<Result> Scan(
            FarmId farmId, bool activate, IReadOnlySet<int> allowedAnimals, int safeAnimal,
            CancellationToken cancellationToken, bool synchronize = false)
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
                browser.Logger.Debug("Switching village to {VillageId} for farm list {FarmId}", owner.Value.Value, farmId.Value);
                var switched = await browser.Click(VillagePanelParser.GetVillageSwitchLink(page, owner.Value));
                if (switched.IsFailed) return switched;
                switched = await browser.Wait(VillagePanelParser.GetActiveVillageNode(page, owner.Value));
                if (switched.IsFailed) return switched;
                browser.Logger.Debug("Village switch confirmed: {VillageId}", owner.Value.Value);
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
            var changedCount = 0;
            foreach (var target in targets.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!synchronize && target.Disabled != activate) continue;
                var targetActivate = synchronize ? target.Disabled : activate;
                var saved = false;
                try
                {
                    var result = await ProcessTarget(
                        farmId, target, targetActivate, allowedAnimals, safeAnimal, cancellationToken);
                    if (result.IsFailed)
                        browser.Logger.Warning("Farm target {SlotId} skipped: {Reason}", target.SlotId, result.Errors[0].Message);
                    else saved = result.Value;
                }
                catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                {
                    browser.Logger.Warning("Farm target {SlotId} check failed ({ErrorType}); status unchanged",
                        target.SlotId, ex.GetType().Name);
                }

                prepared = await FarmListPreparation.Prepare(browser, farmId, owner.Value);
                if (prepared.IsFailed) return prepared;
                if (saved)
                {
                    if (await FarmTargetDeactivator.IsConfirmed(page, farmId, target, targetActivate))
                    {
                        changedCount++;
                        browser.Logger.Information("Farm target {SlotId} {Action} in list {FarmListId}",
                            target.SlotId, targetActivate ? "activated" : "deactivated", farmId.Value);
                    }
                    else browser.Logger.Warning("Farm target {SlotId}: saved status change was not confirmed", target.SlotId);
                }
            }
            browser.Logger.Information("Farm target oasis check completed: list {FarmListId}, {Action}, {TargetCount} targets, {ChangedCount} changed",
                farmId.Value, synchronize ? "synchronize" : activate ? "activate" : "deactivate", targets.Value.Count, changedCount);
            return Result.Ok();
        }

        private async Task<Result<bool>> ProcessTarget(
            FarmId farmId, FarmTargetSlot target, bool activate,
            IReadOnlySet<int> allowedAnimals, int safeAnimal,
            CancellationToken cancellationToken)
        {
            var farmPage = browser.CurrentPage;
            var row = FarmTargetTroopsParser.GetList(farmPage, farmId)
                .Locator($"tr.slot:has(input[data-slot-id='{target.SlotId}'])");
            if (await row.CountAsync() != 1) return Result.Fail("Target slot is missing or ambiguous.");
            var isDisabled = await row.EvaluateAsync<bool>("node => node.classList.contains('disabled')");
            if (isDisabled != activate) return Result.Ok(false);
            var link = row.Locator("td.target a");
            if (await link.GetAttributeAsync("href") != target.Href)
                return Result.Fail("Target coordinates changed during scan.");

            var troops = await FarmTargetOasisTab.Run(browser, target.Href, link, page =>
                InspectTarget(page, target.Href, allowedAnimals, safeAnimal, cancellationToken), background: true);
            if (troops.IsFailed) return Result.Fail(troops.Errors);

            browser.Logger.Information("Farm target {SlotId} ({TargetName}): has troops {HasTroops}, safe {Safe}",
                target.SlotId, target.Name, troops.Value.HasTroops, troops.Value.Safe);
            if (activate)
            {
                if (!troops.Value.Safe) return Result.Ok(false);
                return await FarmTargetDeactivator.ActivateFromFarmList(browser, farmId, target, cancellationToken);
            }
            if (!troops.Value.HasTroops || troops.Value.Safe) return Result.Ok(false);
            return await FarmTargetDeactivator.DeactivateFromFarmList(browser, farmId, target, cancellationToken);
        }

        private async Task<Result<(bool HasTroops, bool Safe)>> InspectTarget(
            IPage page, string targetHref, IReadOnlySet<int> allowedAnimals,
            int safeAnimal, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var troops = await FarmTargetTroopsParser.ReadOasisAnimalTroops(page, targetHref);
            if (troops.IsFailed) return Result.Fail<(bool, bool)>(troops.Errors);
            var present = troops.Value.Where(x => x.Count > 0).ToList();
            var safe = FarmTargetOasisPolicy.HasOnlySafeAllowedAnimals(
                troops.Value, allowedAnimals, safeAnimal);
            return Result.Ok((present.Count > 0, safe));
        }
    }
}
