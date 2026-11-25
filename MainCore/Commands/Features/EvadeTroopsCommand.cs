using System.Data.Common;
using System.Text.Json;

namespace MainCore.Commands.Features
{
    [Handler]
    public sealed partial class EvadeTroopsCommand(
        IChromeBrowser browser, IDbContextFactory<AppDbContext> contextFactory, TimeProvider timeProvider)
    {
        public sealed record Command(AccountId AccountId, VillageId VillageId, EvasionIncoming Incoming, int X, int Y,
            bool ManualTest = false);

        private async ValueTask<Result> HandleAsync(Command command, CancellationToken cancellationToken)
        {
            try
            {
                return await Send(command, cancellationToken);
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException or DbException or DbUpdateException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                browser.Logger.Warning(ex, "Evasion troop send failed; no automatic retry. Check the game before sending again");
                return Skip.Error.WithError($"Evasion troop send failed; check the game before retrying. {ex.Message}");
            }
        }

        private async ValueTask<Result> Send(Command command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var context = contextFactory.CreateDbContext();
            if (context.EvasionTroopAttempts.Any(x => x.AccountId == command.AccountId.Value &&
                x.VillageId == command.VillageId.Value && x.MovementId == command.Incoming.MovementId))
                return Skip.Error.WithError("This incoming movement already has an evasion confirmation record; check the game, no resend.");
            var valid = ValidateSettings(context, command);
            if (valid.IsFailed) return valid;
            if (command.Incoming.Arrival <= timeProvider.GetLocalNow().DateTime)
                return Skip.Error.WithError("Incoming arrival has passed; evasion was not sent.");
            var deadline = command.Incoming.Arrival;
            if (!command.ManualTest)
            {
                var result = await EvasionIncomingNavigation.Open(browser, command.VillageId, cancellationToken);
                if (result.IsFailed) return result;
                var observedAt = timeProvider.GetLocalNow().DateTime;
                var incoming = await EvasionIncomingParser.Read(browser.CurrentPage, observedAt);
                if (incoming.IsFailed) return Result.Fail(incoming.Errors);
                var movement = incoming.Value.SingleOrDefault(x => x.MovementId == command.Incoming.MovementId);
                if (movement is null) return Skip.Error.WithError("Incoming movement no longer exists; evasion was not sent.");
                if (movement.Arrival < deadline) deadline = movement.Arrival;
            }
            else browser.Logger.Warning("LIVE evasion test in village {VillageId}: incoming checks bypassed; real Raid will be sent",
                command.VillageId.Value);
            return await RallyPointSendTroopsTab.Run(browser, command.VillageId,
                _ => SendArmy(command, context, deadline, cancellationToken), cancellationToken);
        }

        private async Task<Result> SendArmy(Command command, AppDbContext context, DateTime deadline,
            CancellationToken cancellationToken)
        {
            var available = await EvasionTroopsParser.ReadAvailable(browser.CurrentPage, command.VillageId, cancellationToken);
            if (available.IsFailed) return Result.Fail(available.Errors);
            var result = await EvasionTroopsParser.Fill(browser, available.Value, command.X, command.Y, cancellationToken);
            if (result.IsFailed) return result;
            result = await EvasionTroopsParser.Validate(browser.CurrentPage, command.VillageId, available.Value, command.X, command.Y, false, cancellationToken);
            if (result.IsFailed) return result;
            result = ValidateSettings(context, command);
            if (result.IsFailed) return result;
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline <= timeProvider.GetLocalNow().DateTime)
                return Skip.Error.WithError("Incoming arrived during evasion preparation; no send.");
            result = await RallyPointSendTroopsTab.Submit(browser.CurrentPage);
            if (result.IsFailed) return result;
            var page = browser.CurrentPage;
            result = await browser.Wait(page.Locator("#troopSendForm #confirmSendTroops"));
            if (result.IsFailed) return result;
            result = await EvasionTroopsParser.Validate(page, command.VillageId, available.Value, command.X, command.Y, true, cancellationToken);
            if (result.IsFailed) return result;
            var confirm = page.Locator("#troopSendForm #confirmSendTroops");
            if (await confirm.CountAsync() != 1 || !await confirm.IsEnabledAsync())
                return Result.Fail("Evasion Confirm button is unavailable.");
            result = ValidateSettings(context, command);
            if (result.IsFailed) return result;
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline <= timeProvider.GetLocalNow().DateTime)
                return Skip.Error.WithError("Incoming arrived before Confirm; evasion was not sent.");

            var attempt = new EvasionTroopAttempt
            {
                AccountId = command.AccountId.Value, VillageId = command.VillageId.Value,
                MovementId = command.Incoming.MovementId, TargetX = command.X, TargetY = command.Y,
            };
            context.EvasionTroopAttempts.Add(attempt);
            context.SaveChanges();
            cancellationToken.ThrowIfCancellationRequested();
            result = ValidateSettings(context, command);
            if (result.IsFailed) return result;
            if (deadline <= timeProvider.GetLocalNow().DateTime)
                return Skip.Error.WithError("Recording evasion confirmation missed the deadline; no click, no automatic retry.");
            var origin = new Uri(page.Url);
            result = await browser.Click(confirm);
            if (result.IsFailed) return result;
            await page.WaitForURLAsync(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                uri.Scheme == origin.Scheme && uri.Authority == origin.Authority && uri.AbsolutePath == "/building" &&
                HeroOasisParser.Query(uri, "tt") == "1", new() { Timeout = 5000 });
            result = await browser.Wait(page.Locator("#content.buildRallyPoint .rallyPointOverviewContainer"));
            if (result.IsFailed) return result;
            if (!await EvasionTroopsParser.HasVillage(page, command.VillageId) ||
                await page.Locator("#troopSendForm").CountAsync() != 0 ||
                await page.Locator("#content .errorMessage, #content .errorMessageContainer").CountAsync() > 0)
                return Result.Fail("Evasion confirmation outcome is uncertain; check the game, no automatic resend.");
            attempt.Status = "Sent";
            context.SaveChanges();
            browser.Logger.Information("Evasion raid sent: village {VillageId}, incoming {MovementId}, target ({X}|{Y}), army {Troops}",
                command.VillageId.Value, command.Incoming.MovementId, command.X, command.Y, available.Value);
            return Result.Ok();
        }

        private static Result ValidateSettings(AppDbContext context, Command command)
        {
            var settings = EvasionSettings.Read(context, command.AccountId, command.VillageId);
            if (settings.IsFailed) return Result.Fail(settings.Errors);
            return settings.Value.Enabled && settings.Value.EvadeTroops &&
                settings.Value.X == command.X && settings.Value.Y == command.Y
                ? Result.Ok() : Skip.Error.WithError("Evasion troop settings changed or were disabled; no send.");
        }

    }
}
