using System.Globalization;

using MainCore.Tasks.Base;

namespace MainCore.Tasks
{
    [Handler]
    public sealed partial class ConquestTask(IChromeBrowser browser)
    {
        public sealed class Task : VillageTask
        {
            public Task(
                AccountId accountId,
                VillageId villageId,
                int targetX,
                int targetY,
                IReadOnlyList<ConquestWave> waves,
                DateTime arrivalAt,
                DateTime sendAt,
                double delaySeconds) : base(accountId, villageId)
            {
                TargetX = targetX;
                TargetY = targetY;
                Waves = Array.AsReadOnly(waves.ToArray());
                ArrivalAt = arrivalAt;
                SendAt = sendAt;
                DelaySeconds = delaySeconds;
                ExecuteAt = Max(DateTime.Now, sendAt.AddMinutes(-2));
            }

            public int TargetX { get; }
            public int TargetY { get; }
            public IReadOnlyList<ConquestWave> Waves { get; }
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
                $"{AccountId}-{VillageId}-{TargetX}-{TargetY}-{ArrivalAt.Ticks}-{SendAt.Ticks}-" +
                string.Join(",", Waves.Select(wave =>
                    $"{string.Join("/", wave.Troops)}:{wave.CatapultTarget1}:{wave.CatapultTarget2}"));

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
            if (task.Waves.Count is < 1 or > ConquestWave.MaxWaves)
                return Stop.Error.WithError($"Wave Builder supports 1 to {ConquestWave.MaxWaves} waves.");
            foreach (var wave in task.Waves)
            {
                var validation = wave.Validate();
                if (validation.IsFailed) return Stop.Error.WithErrors(validation.Errors);
            }

            var builder = new UriBuilder(baseUrl)
            {
                Path = "/build.php",
                Query = $"gid=16&tt=2&newdid={task.VillageId.Value}",
            };
            var navigateResult = await browser.Navigate(builder.Uri.AbsoluteUri);
            if (navigateResult.IsFailed) return navigateResult;

            var page = browser.CurrentPage;
            var preparedWaves = task.Waves.ToArray();
            await page.Locator("#troopSendForm").WaitForAsync();
            for (var waveIndex = 0; waveIndex < task.Waves.Count; waveIndex++)
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

                var wave = task.Waves[waveIndex];
                var fillResult = await FillWave(page, wave.Troops, task, waveIndex > 0);
                if (fillResult.IsFailed) return fillResult;

                var sendButton = waveIndex > 0
                    ? page.Locator("#troopSendForm #addAsWaveBtn")
                    : page.Locator("#troopSendForm button[type='submit']:not(#addWave):not(#confirmSendTroops)");
                if (await sendButton.CountAsync() != 1)
                    return Stop.Error.WithError("The Rally Point troop form submit button is missing or ambiguous.");
                await sendButton.ClickAsync();
                await page.Locator("#troopSendForm #confirmSendTroops").WaitForAsync();
                var targetsResult = await SetCatapultTargets(page, waveIndex, wave);
                if (targetsResult.IsFailed) return Stop.Error.WithErrors(targetsResult.Errors);
                preparedWaves[waveIndex] = targetsResult.Value;

                var verifyResult = await VerifyWave(page, waveIndex, wave.Troops, task);
                if (verifyResult.IsFailed) return verifyResult;
            }

            for (var waveIndex = 0; waveIndex < task.Waves.Count; waveIndex++)
            {
                var verifyResult = await VerifyWave(page, waveIndex, task.Waves[waveIndex].Troops, task);
                if (verifyResult.IsFailed) return verifyResult;
                var targetsResult = await VerifyCatapultTargets(page, waveIndex, preparedWaves[waveIndex]);
                if (targetsResult.IsFailed) return targetsResult;
            }

            var travelTimes = await ReadTravelTimes(page, task.Waves.Count);
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

        private static async System.Threading.Tasks.Task<Result> FillWave(IPage page, IReadOnlyList<long> troops, Task task, bool additionalWave)
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
                var value = coordinate.ToString(CultureInfo.InvariantCulture);
                if (additionalWave)
                {
                    if (await input.GetAttributeAsync("type") != "hidden" || await input.InputValueAsync() != value)
                        return Stop.Error.WithError("Wave Builder target coordinates do not match the plan.");
                }
                else
                {
                    await input.FillAsync(value);
                }
            }

            if (additionalWave)
            {
                var eventType = form.Locator("input[type='hidden'][name='eventType']");
                if (await eventType.CountAsync() != 1 || await eventType.InputValueAsync() != "3")
                    return Stop.Error.WithError("Wave Builder did not retain Attack: Normal.");
                return Result.Ok();
            }

            var attackType = form.Locator("input[type='radio'][name='c'][value='3'], input[type='radio'][name='eventType'][value='3']");
            var attackTypeCount = await attackType.CountAsync();
            if (attackTypeCount != 1)
                return Stop.Error.WithError("Rally Point Attack: Normal selector is missing or ambiguous.");
            if (!await attackType.IsEnabledAsync())
                return Stop.Error.WithError("Rally Point Attack: Normal option is disabled.");
            await attackType.CheckAsync();
            if (!await attackType.IsCheckedAsync())
                return Stop.Error.WithError("Rally Point Attack: Normal option could not be selected.");

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

        private async System.Threading.Tasks.Task<Result<ConquestWave>> SetCatapultTargets(IPage page, int waveIndex, ConquestWave wave)
        {
            if (wave.Troops[7] == 0) return Result.Ok(wave);
            var firstTarget = wave.CatapultTarget1;
            var secondTarget = wave.CatapultTarget2;

            foreach (var (target, number) in new[] { ((int?)wave.CatapultTarget1, 1), (wave.CatapultTarget2, 2) })
            {
                var select = page.Locator($"#troopSendForm select[name='troops[{waveIndex}][catapultTarget{number}]']");
                var count = await select.CountAsync();
                if (number == 2 && target is null && count == 0) continue;
                if (count != 1)
                    return Result.Fail($"Catapult target {number} selector for wave {waveIndex + 1} is missing or ambiguous.");

                var value = target?.ToString(CultureInfo.InvariantCulture) ?? "";
                var availableValues = await select.Locator("option").EvaluateAllAsync<string[]>(
                    "options => options.filter(option => !option.disabled && !option.parentElement.disabled).map(option => option.value)");
                if (!availableValues.Contains(value))
                {
                    if (target is null || !availableValues.Contains("99"))
                        return Result.Fail($"Catapult target '{value}' is unavailable for wave {waveIndex + 1}, target {number}, and cannot be replaced with Random.");
                    browser.Logger.Warning(
                        "Wave {Wave}, catapult target {TargetNumber}: target {Target} is unavailable; using Random (99)",
                        waveIndex + 1, number, value);
                    value = "99";
                    if (number == 1) firstTarget = 99;
                    else secondTarget = 99;
                }
                await select.SelectOptionAsync(value);
            }

            var preparedWave = new ConquestWave(wave.Troops, firstTarget, secondTarget);
            var verification = await VerifyCatapultTargets(page, waveIndex, preparedWave);
            return verification.IsFailed ? Result.Fail(verification.Errors) : Result.Ok(preparedWave);
        }

        private static async System.Threading.Tasks.Task<Result> VerifyCatapultTargets(IPage page, int waveIndex, ConquestWave wave)
        {
            if (wave.Troops[7] == 0) return Result.Ok();

            foreach (var (target, number) in new[] { ((int?)wave.CatapultTarget1, 1), (wave.CatapultTarget2, 2) })
            {
                var name = $"troops[{waveIndex}][catapultTarget{number}]";
                var input = page.Locator($"#troopSendForm select[name='{name}']");
                if (await input.CountAsync() == 0)
                    input = page.Locator($"#troopSendForm input[type='hidden'][name='{name}']");
                var count = await input.CountAsync();
                if (number == 2 && target is null && count == 0) continue;
                if (count != 1 || await input.InputValueAsync() != (target?.ToString(CultureInfo.InvariantCulture) ?? ""))
                    return Stop.Error.WithError($"Catapult target {number} for wave {waveIndex + 1} does not match the plan.");
            }

            return Result.Ok();
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
