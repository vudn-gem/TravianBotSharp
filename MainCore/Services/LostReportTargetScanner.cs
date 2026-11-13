namespace MainCore.Services
{
    [RegisterScoped<LostReportTargetScanner>]
    public sealed class LostReportTargetScanner(IChromeBrowser browser)
    {
        public async Task Scan(CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var originalUrl = page.Url;
            var originalVillage = await VillagePanelParser.GetCurrentVillageId(page);
            try
            {
                var result = await browser.Click(page.Locator("a.reports[accesskey='5']"));
                if (result.IsFailed) { Warn(result); return; }
                result = await browser.Click(page.Locator("a.tabItem[href='/report/offensive']"));
                if (result.IsFailed) { Warn(result); return; }
                result = await browser.Wait(page.Locator("a.tabItem[href='/report/offensive']"));
                if (result.IsFailed) { Warn(result); return; }
                var reports = await OffensiveReportParser.ReadUnreadLostReports(page);
                if (reports.IsFailed) { Warn(Result.Fail(reports.Errors)); return; }
                var listUrl = new Uri(page.Url);
                foreach (var report in reports.Value)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!Uri.TryCreate(listUrl, report.Href, out var url) ||
                        url.Scheme != listUrl.Scheme || url.Authority != listUrl.Authority ||
                        url.AbsolutePath != listUrl.AbsolutePath ||
                        !Uri.UnescapeDataString(url.Query).Contains($"id={report.Id}|", StringComparison.Ordinal))
                    {
                        browser.Logger.Warning("Lost report {ReportId} skipped: invalid report link", report.Id);
                        continue;
                    }
                    try
                    {
                        result = await ProcessReport(url.AbsoluteUri, cancellationToken);
                        if (result.IsFailed)
                            browser.Logger.Warning("Lost report {ReportId} target skipped: {Reason}", report.Id, result.Errors[0].Message);
                    }
                    catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
                    {
                        browser.Logger.Warning("Lost report {ReportId} target processing failed ({ErrorType})", report.Id, ex.GetType().Name);
                    }
                }
            }
            finally
            {
                // Restore browser context even when a report fails; later tasks share this page.
                var restored = await RestoreOriginalPage(originalUrl);
                if (restored.IsFailed) Warn(restored);
                var village = VillagePanelParser.GetVillageNode(page, originalVillage);
                if (originalVillage != VillageId.Empty && await village.CountAsync() == 1 &&
                    !await VillagePanelParser.IsActive(village))
                {
                    var switched = await browser.Click(village);
                    if (switched.IsFailed) Warn(switched);
                    else
                    {
                        var active = await browser.Wait(page.Locator(
                            $"#sidebarBoxVillageList .listEntry.village.active[data-did='{originalVillage}']"));
                        if (active.IsFailed) Warn(active);
                        restored = await RestoreOriginalPage(originalUrl, originalVillage);
                        if (restored.IsFailed) Warn(restored);
                    }
                }
            }
        }

        private Task<Result> RestoreOriginalPage(string url, VillageId? owner = null)
        {
            var uri = new Uri(url);
            return uri.AbsolutePath == "/build.php" && HeroOasisParser.Query(uri, "gid") == "16" &&
                HeroOasisParser.Query(uri, "tt") == "99"
                ? FarmListNavigation.Open(browser, url, owner)
                : browser.Navigate(url);
        }

        private async Task<Result> ProcessReport(string url, CancellationToken cancellationToken)
        {
            var page = browser.CurrentPage;
            var result = await OpenReport(url);
            if (result.IsFailed) return result;
            var village = await OffensiveReportParser.ReadAttackerVillage(page);
            if (village.IsFailed) return Result.Fail(village.Errors);
            var node = VillagePanelParser.GetVillageNode(page, village.Value);
            if (!await VillagePanelParser.IsActive(node))
            {
                result = await browser.Click(node);
                if (result.IsFailed) return result;
                result = await browser.Wait(page.Locator(
                    $"#sidebarBoxVillageList .listEntry.village.active[data-did='{village.Value}']"));
                if (result.IsFailed) return result;
                result = await OpenReport(url);
                if (result.IsFailed) return result;
            }
            if (await VillagePanelParser.GetCurrentVillageId(page) != village.Value)
                return Result.Fail("Attacker village switch was not confirmed.");
            if (!await OffensiveReportParser.HasAvailableRaidListButton(page))
                return Result.Fail("Report raid list button is unavailable or disabled.");
            cancellationToken.ThrowIfCancellationRequested();
            result = await browser.Click(page.Locator("button:has(i.reportButton.raidList)"));
            if (result.IsFailed) return result;
            result = await browser.Wait(page.Locator("#farmListTargetForm"));
            if (result.IsFailed) return result;
            var selected = await OffensiveReportParser.ReadSelectedFarmList(page);
            if (selected.IsFailed) return Result.Fail(selected.Errors);
            var checkbox = page.Locator("#farmListTargetForm .deactivateSlot input[name='isActive']");
            if (await checkbox.IsCheckedAsync())
                return await OpenReport(url);

            cancellationToken.ThrowIfCancellationRequested();
            // Despite its isActive name, the supplied UI labels checked as Deactivate target.
            await checkbox.CheckAsync(new LocatorCheckOptions { Timeout = 3000 });
            result = await browser.Click(page.Locator("#farmListTargetForm .actionButtons button.save[type='submit']"));
            if (result.IsFailed) return result;
            await page.Locator("#farmListTargetForm").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
            result = await OpenReport(url);
            if (result.IsFailed) return result;
            if (!await OffensiveReportParser.HasAvailableRaidListButton(page))
                return Result.Fail("Unable to verify saved target: raid list button unavailable.");
            result = await browser.Click(page.Locator("button:has(i.reportButton.raidList)"));
            if (result.IsFailed) return result;
            result = await browser.Wait(page.Locator("#farmListTargetForm"));
            if (result.IsFailed) return result;
            var savedList = await OffensiveReportParser.ReadSelectedFarmList(page);
            if (savedList.IsFailed || savedList.Value != selected.Value || !await checkbox.IsCheckedAsync())
                return Result.Fail("Target deactivation was not confirmed after reopening the dialog.");
            browser.Logger.Information("Lost report target deactivated: village {VillageId}, farm list {FarmListId}",
                village.Value, selected.Value);
            return await OpenReport(url);
        }

        private async Task<Result> OpenReport(string url)
        {
            var result = await browser.Navigate(url);
            if (result.IsFailed) return result;
            return await browser.Wait(browser.CurrentPage.Locator(".body .role.attacker .troopHeadline a.village"));
        }

        private void Warn(Result result) =>
            browser.Logger.Warning("Lost report target scan failed: {Reason}", result.Errors[0].Message);
    }
}
