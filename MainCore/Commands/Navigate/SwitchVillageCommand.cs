namespace MainCore.Commands.Navigate
{
    [Handler]
    public sealed partial class SwitchVillageCommand(IChromeBrowser browser, ILogger logger)
    {
        public sealed record Command(VillageId VillageId) : IVillageCommand;

        private async ValueTask<Result> HandleAsync(Command command)
        {
            var villageId = command.VillageId;

            var villageNode = VillagePanelParser.GetVillageNode(browser.CurrentPage, villageId);
            if (await villageNode.CountAsync() == 0)
            {
                logger.Debug("Switch village skipped: village {VillageId} not found in sidebar", villageId.Value);
                return Skip.Error.WithError("Village not found");
            }

            if (await VillagePanelParser.IsActive(villageNode))
            {
                logger.Debug("Switch village skipped: village {VillageId} is already active", villageId.Value);
                return Result.Ok();
            }

            logger.Debug("Switching village to {VillageId}", villageId.Value);
            Result result;
            var villageLink = VillagePanelParser.GetVillageSwitchLink(browser.CurrentPage, villageId);
            result = await browser.Click(villageLink);
            if (result.IsFailed) return result;

            result = await browser.Wait(VillagePanelParser.GetActiveVillageNode(browser.CurrentPage, villageId));
            if (result.IsFailed) return result;

            logger.Debug("Switched village to {VillageId}", villageId.Value);
            return Result.Ok();
        }
    }
}
