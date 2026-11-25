namespace MainCore.Commands.Features
{
    [Handler]
    public sealed partial class TrainTroopCommand(
        ILogger logger,
        IChromeBrowser browser,
        TimeProvider timeProvider)
    {
        public sealed record Command(TroopEnums Troop, int Amount, bool TrainWhenLowResource,
            bool TrainMaximum = false, DateTime? SubmitBefore = null, Func<Result>? ValidateBeforeSubmit = null);

        private async ValueTask<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            var troop = command.Troop;
            var amount = command.Amount;
            Result result;

            cancellationToken.ThrowIfCancellationRequested();
            if (command.SubmitBefore is { } deadline && timeProvider.GetLocalNow().DateTime >= deadline)
                return Skip.Error.WithError("Incoming arrival has passed; troop training was not submitted.");

            int maxAmount;
            if (command.TrainMaximum)
            {
                var maximum = await TrainTroopParser.ReadMaximum(browser.CurrentPage, troop);
                if (maximum.IsFailed) return Result.Fail(maximum.Errors);
                maxAmount = maximum.Value;
            }
            else maxAmount = await TrainTroopParser.GetMaxAmount(browser.CurrentPage, troop);
            if (maxAmount <= 0)
            {
                return MissingResource.Error(troop);
            }

            if (command.TrainMaximum)
            {
                amount = maxAmount;
                var input = TrainTroopParser.GetInputBox(browser.CurrentPage, troop);
                if (await input.CountAsync() != 1)
                    return Result.Fail("Selected evasion troop training field is missing or ambiguous.");
                // A training form submits every troop field, not just the selected type.
                if (await TrainTroopParser.GetTrainButton(browser.CurrentPage).CountAsync() != 1)
                    return Result.Fail("Evasion troop training submit button is missing or ambiguous.");
                var others = browser.CurrentPage.Locator("div.troop:not(.empty) div.cta input.text");
                for (var i = 0; i < await others.CountAsync(); i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var other = others.Nth(i);
                    if (await other.EvaluateAsync<bool>($"node => !!node.closest('div.troop').querySelector('img.unit.u{(int)troop}')"))
                        continue;
                    var value = await other.InputValueAsync();
                    if (string.IsNullOrWhiteSpace(value) || value == "0") continue;
                    result = await browser.Input(other, "0");
                    if (result.IsFailed) return result;
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            else if (amount > maxAmount && !command.TrainWhenLowResource)
            {
                return MissingResource.Error(troop);
            }

            result = await Train(command, amount, cancellationToken);
            if (result.IsFailed) return result;

            logger.Information("Troop training for {Troop} with amount {Amount} is done.", troop, amount);
            return Result.Ok();
        }

        private async ValueTask<Result> Train(Command command, long amount, CancellationToken cancellationToken)
        {
            Result result;
            result = await browser.Input(TrainTroopParser.GetInputBox(browser.CurrentPage, command.Troop), $"{amount}");
            if (result.IsFailed) return result;

            cancellationToken.ThrowIfCancellationRequested();
            if (command.ValidateBeforeSubmit is { } validate)
            {
                result = validate();
                if (result.IsFailed) return result;
            }
            if (command.SubmitBefore is { } deadline && timeProvider.GetLocalNow().DateTime >= deadline)
                return Skip.Error.WithError("Incoming arrival has passed while filling troop fields; training was not submitted.");

            result = await browser.Click(TrainTroopParser.GetTrainButton(browser.CurrentPage));
            if (result.IsFailed) return result;

            return Result.Ok();
        }
    }
}