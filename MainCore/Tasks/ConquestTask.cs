using System.Globalization;

using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class ConquestTask(IChromeBrowser browser)
    {
        private const int MaxWaves = 8;

        public sealed class Task : VillageTask
        {
            public Task(
                AccountId accountId,
                VillageId villageId,
                int targetX,
                int targetY,
                string troopWaves,
                int catapultTarget,
                DateTime arrivalAt,
                DateTime sendAt,
                double delaySeconds) : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
                TroopWaves = troopWaves;
                CatapultTarget = catapultTarget;
                ArrivalAt = arrivalAt;
                SendAt = sendAt;
                DelaySeconds = delaySeconds;
                ExecuteAt = Max(DateTime.Now, sendAt.AddMinutes(-2));
            }

            public int TargetX { get; }
            public int TargetY { get; }
            public string TroopWaves { get; }
            public int CatapultTarget { get; }
            public DateTime ArrivalAt { get; }
            public DateTime SendAt { get; }
            public double DelaySeconds { get; }
            public bool ConfirmationAttempted { get; set; }

            protected override string TaskName => $"Conquest attack ({TargetX}|{TargetY})";

            public override bool CanStart(AppDbContext context) =>
                context.Villages.Any(x => x.Id == VillageId.Value && x.AccountId == AccountId.Value);

            public override string Description =>
                $"Conquest attack from village {VillageId.Value} to ({TargetX}|{TargetY}), arrival {ArrivalAt:yyyy-MM-dd HH:mm:ss}";

            public override string Key =>
                $"{AccountId}-{VillageId}-{TargetX}-{TargetY}-{ArrivalAt.Ticks}-{SendAt.Ticks}-{CatapultTarget}-{TroopWaves}";

            private static DateTime Max(DateTime left, DateTime right) => left > right ? left : right;
        }

        private async ValueTask<Result> HandleAsync(Task task, CancellationToken cancellationToken)
        {
            if (task.ConfirmationAttempted)
                return Stop.Error.WithError("The previous confirmation outcome is unknown. Check the game before retrying this attack.");
            if (!browser.IsInitialized || browser.CurrentPage.IsClosed)
                return Stop.Error.WithError("Browser is not ready for the conquest attack.");

            var baseUrl = browser.CurrentUrl;
            var openResult = await browser.OpenNewTab();
            if (openResult.IsFailed) return openResult;

            var result = Result.Ok();
            try
            {
                result = await PrepareAndSend(task, baseUrl, cancellationToken);
            }
            finally
            {
                var closeResult = await browser.CloseNewTab();
                if (closeResult.IsFailed)
                {
                    browser.Logger.Error("Could not close the conquest tab: {Errors}",
                        string.Join(Environment.NewLine, closeResult.Errors.Select(x => x.Message)));
                    if (result.IsSuccess) result = closeResult;
                }
            }

            return result;
        }

        private async System.Threading.Tasks.Task<Result> PrepareAndSend(Task task, string baseUrl, CancellationToken cancellationToken)
        {
            var wavesResult = ParseTroopWaves(task.TroopWaves);
            if (wavesResult.IsFailed) return Stop.Error.WithErrors(wavesResult.Errors);
            if (wavesResult.Value.Count is < 1 or > MaxWaves)
                return Stop.Error.WithError($"Wave Builder supports 1 to {MaxWaves} waves.");
            if (task.CatapultTarget is < 1 or > 5)
                return Stop.Error.WithError("Catapult target must be between 1 and 5.");

            var builder = new UriBuilder(baseUrl)
            {
                Path = "/build.php",
                Query = $"gid=16&tt=2&newdid={task.VillageId.Value}",
            };
            var navigateResult = await browser.Navigate(builder.Uri.AbsoluteUri);
            if (navigateResult.IsFailed) return navigateResult;

            var page = browser.CurrentPage;
            await page.Locator("#troopSendForm").WaitForAsync();
            for (var waveIndex = 0; waveIndex < wavesResult.Value.Count; waveIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (waveIndex > 0)
                {
                    var addWave = page.Locator("#troopSendForm #addWave");
                    if (await addWave.CountAsync() != 1)
                        return Stop.Error.WithError("Wave Builder is unavailable or the maximum wave count was reached.");
                    await addWave.ClickAsync();
                    await page.Locator("#troopSendForm input[name='troop[t1]']").WaitForAsync();
                }

                var fillResult = await FillWave(page, wavesResult.Value[waveIndex], task);
                if (fillResult.IsFailed) return fillResult;

                var sendButton = page.Locator("#troopSendForm button[type='submit']:not(#addWave):not(#confirmSendTroops)");
                if (await sendButton.CountAsync() != 1)
                    return Stop.Error.WithError("The Rally Point troop form submit button is missing or ambiguous.");
                await sendButton.ClickAsync();
                await page.Locator("#troopSendForm #confirmSendTroops").WaitForAsync();
                var catapultCount = wavesResult.Value[waveIndex].ElementAtOrDefault(7);
                if (catapultCount > 0)
                {
                    var targetSelect = page.Locator($"#troopSendForm select[name='troops[{waveIndex}][catapultTarget1]']");
                    if (await targetSelect.CountAsync() != 1)
                        return Stop.Error.WithError($"Catapult target selector for wave {waveIndex + 1} is missing or ambiguous.");
                    var selectedValue = await ResolveCatapultTarget(targetSelect, task.CatapultTarget);
                    if (selectedValue.IsFailed) return Stop.Error.WithErrors(selectedValue.Errors);
                    await targetSelect.SelectOptionAsync(selectedValue.Value);
                }

                var verifyResult = await VerifyWave(page, waveIndex, wavesResult.Value[waveIndex], task);
                if (verifyResult.IsFailed) return verifyResult;
            }

            var travelTimes = await ReadTravelTimes(page, wavesResult.Value.Count);
            if (travelTimes.IsFailed) return Stop.Error.WithErrors(travelTimes.Errors);
            if (WillArriveLate(travelTimes.Value, task))
                return Skip.Error.WithError("Estimated arrival is later than the planned arrival time; the attack was skipped.");

            var wait = task.SendAt - DateTime.Now;
            if (wait > TimeSpan.Zero)
                await System.Threading.Tasks.Task.Delay(wait, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (WillArriveLate(travelTimes.Value, task))
                return Skip.Error.WithError("Estimated arrival is later than the planned arrival time; the attack was skipped.");

            var confirmButton = page.Locator("#troopSendForm #confirmSendTroops");
            if (await confirmButton.CountAsync() != 1)
                return Stop.Error.WithError("The Rally Point confirmation button is missing or ambiguous.");

            task.ConfirmationAttempted = true;
            await confirmButton.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            if (await page.Locator("#troopSendForm #confirmSendTroops").CountAsync() > 0)
                return Stop.Error.WithError("The game did not leave the confirmation page. Check the attack status before retrying.");
            var errors = page.Locator("#content .errorMessage, #content .errorMessageContainer");
            if (await errors.CountAsync() > 0)
                return Stop.Error.WithError("The game reported an error after confirming the attack. Check the game before retrying.");
            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task<Result> FillWave(IPage page, IReadOnlyList<long> troops, Task task)
        {
            var form = page.Locator("#troopSendForm");
            if (await form.CountAsync() != 1)
                return Stop.Error.WithError("Rally Point send-troops form is missing or ambiguous.");

            foreach (var (amount, index) in troops.Select((amount, index) => (amount, index)))
            {
                var input = form.Locator($"input[name='troop[t{index + 1}]']");
                var count = await input.CountAsync();
                if (amount > 0 && count != 1)
                    return Stop.Error.WithError($"Troop field t{index + 1} is unavailable for this village.");
                if (count == 1 && await input.IsEnabledAsync())
                    await input.FillAsync(amount.ToString(CultureInfo.InvariantCulture));
                else if (amount > 0)
                    return Stop.Error.WithError($"Troop field t{index + 1} is disabled for this village.");
            }

            foreach (var (coordinate, name) in new[] { (task.TargetX, "x"), (task.TargetY, "y") })
            {
                var input = form.Locator($"input[name='{name}']");
                if (await input.CountAsync() != 1)
                    return Stop.Error.WithError($"Rally Point coordinate field '{name}' is missing or ambiguous.");
                await input.FillAsync(coordinate.ToString(CultureInfo.InvariantCulture));
            }

            var attackType = form.Locator("input[type='radio'][name='c'][value='3'], input[type='radio'][name='eventType'][value='3']");
            var attackTypeCount = await attackType.CountAsync();
            if (attackTypeCount > 1)
                return Stop.Error.WithError("Rally Point attack type selector is ambiguous.");
            if (attackTypeCount == 1) await attackType.CheckAsync();

            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task<Result> VerifyWave(
            IPage page,
            int waveIndex,
            IReadOnlyList<long> expectedTroops,
            Task task)
        {
            var form = page.Locator("#troopSendForm");
            foreach (var (amount, troopIndex) in expectedTroops.Select((amount, index) => (amount, index)))
            {
                var input = form.Locator(
                    $"input[type='hidden'][name='troops[{waveIndex}][t{troopIndex + 1}]']");
                if (await input.CountAsync() != 1 ||
                    !long.TryParse(await input.GetAttributeAsync("value"), NumberStyles.None, CultureInfo.InvariantCulture, out var actual) ||
                    actual != amount)
                    return Stop.Error.WithError($"Wave Builder troop counts did not match wave {waveIndex + 1}.");
            }

            foreach (var (coordinate, name) in new[] { (task.TargetX, "x"), (task.TargetY, "y") })
            {
                var input = form.Locator($"input[type='hidden'][name='{name}']");
                if (await input.CountAsync() != 1 ||
                    await input.GetAttributeAsync("value") != coordinate.ToString(CultureInfo.InvariantCulture))
                    return Stop.Error.WithError("Wave Builder target coordinates do not match the plan.");
            }

            var eventType = form.Locator("input[type='hidden'][name='eventType']");
            if (await eventType.CountAsync() != 1 || await eventType.GetAttributeAsync("value") != "3")
                return Stop.Error.WithError("The Rally Point did not prepare an attack for this wave.");

            var villageId = form.Locator($"input[type='hidden'][name='troops[{waveIndex}][villageId]']");
            if (await villageId.CountAsync() != 1 ||
                await villageId.GetAttributeAsync("value") != task.VillageId.Value.ToString(CultureInfo.InvariantCulture))
                return Stop.Error.WithError("The Rally Point prepared the wave from a different village than planned.");

            return Result.Ok();
        }

        private static async System.Threading.Tasks.Task<Result<string>> ResolveCatapultTarget(ILocator targetSelect, int target)
        {
            var options = await targetSelect.Locator("option:not([disabled])").EvaluateAllAsync<string[]>(
                "options => options.map(option => JSON.stringify({ value: option.value, text: option.textContent.trim() }))");
            var parsed = new List<(string Value, string Text)>();
            foreach (var option in options)
            {
                using var document = System.Text.Json.JsonDocument.Parse(option);
                parsed.Add((
                    document.RootElement.GetProperty("value").GetString() ?? "",
                    document.RootElement.GetProperty("text").GetString() ?? ""));
            }

            var matches = target switch
            {
                1 => parsed.Where(x => x.Text.Equals("Random", StringComparison.OrdinalIgnoreCase)),
                2 => parsed.Where(x => x.Text.Equals("Cropland", StringComparison.OrdinalIgnoreCase)),
                3 => parsed.Where(x => x.Text.Equals("Residence", StringComparison.OrdinalIgnoreCase)),
                4 => parsed.Where(x => x.Text.Equals("Treasury", StringComparison.OrdinalIgnoreCase)),
                5 => parsed.Where(x =>
                    x.Text.Equals("Residence", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Equals("Palace", StringComparison.OrdinalIgnoreCase) ||
                    x.Text.Equals("Command Center", StringComparison.OrdinalIgnoreCase)),
                _ => [],
            };
            var availableMatches = matches.ToList();
            if (availableMatches.Count == 0)
                return Result.Fail($"Catapult target option {target} is not available in the Rally Point.");
            if (availableMatches.Count > 1)
                return Result.Fail($"Catapult target option {target} is ambiguous in the Rally Point.");
            return Result.Ok(availableMatches[0].Value);
        }

        private static async System.Threading.Tasks.Task<Result<List<TimeSpan>>> ReadTravelTimes(IPage page, int expectedWaveCount)
        {
            var readAt = DateTimeOffset.UtcNow;
            var values = await page.Locator("#troopSendForm table.troop_details tbody.infos #at[value]")
                .EvaluateAllAsync<string[]>("timers => timers.map(timer => timer.getAttribute('value'))");
            if (values.Length != expectedWaveCount ||
                values.Any(value => !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                return Result.Fail("Could not read the game's estimated arrival time.");

            var travelTimes = values.Select(value =>
            {
                var estimatedArrival = DateTimeOffset.FromUnixTimeSeconds(long.Parse(value!, CultureInfo.InvariantCulture));
                return estimatedArrival > readAt ? estimatedArrival - readAt : TimeSpan.Zero;
            }).ToList();
            return Result.Ok(travelTimes);
        }

        private static bool WillArriveLate(IReadOnlyList<TimeSpan> travelTimes, Task task)
        {
            var now = DateTimeOffset.UtcNow;
            var delay = TimeSpan.FromSeconds(task.DelaySeconds);
            var latestArrival = travelTimes
                .Select(travelTime => now + travelTime + delay)
                .Max();
            var plannedArrival = new DateTimeOffset(DateTime.SpecifyKind(task.ArrivalAt, DateTimeKind.Local));
            return latestArrival.ToUnixTimeSeconds() > plannedArrival.ToUnixTimeSeconds();
        }

        public static Result<List<IReadOnlyList<long>>> ParseTroopWaves(string troopWaves)
        {
            if (string.IsNullOrWhiteSpace(troopWaves))
                return Result.Fail("Enter at least one troop wave.");

            var waves = new List<IReadOnlyList<long>>();
            foreach (var wave in troopWaves.Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var values = wave.Split('/', StringSplitOptions.TrimEntries);
                if (values.Length != 11 ||
                    values.Any(value => !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) || amount < 0))
                    return Result.Fail("Each troop wave must contain 11 non-negative troop counts separated by '/'.");
                var troops = values.Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                if (troops.All(amount => amount == 0))
                    return Result.Fail("Each troop wave must contain at least one troop.");
                waves.Add(troops);
            }

            return Result.Ok(waves);
        }
    }
}
