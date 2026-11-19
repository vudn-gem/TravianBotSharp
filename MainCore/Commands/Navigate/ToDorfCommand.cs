namespace MainCore.Commands.Navigate
{
    [Handler]
    public sealed partial class ToDorfCommand(IChromeBrowser browser)
    {
        public sealed record Command(int Dorf) : ICommand;

        private async ValueTask<Result> HandleAsync(Command command)
        {
            var dorf = command.Dorf;

            var currentUrl = browser.CurrentUrl;
            var currentDorf = GetCurrentDorf(currentUrl);
            if (dorf == 0)
            {
                if (currentDorf == 0) dorf = 2;
                else dorf = currentDorf;
            }

            if (currentDorf != 0 && dorf == currentDorf)
            {
                return Result.Ok();
            }

            Result result;
            result = await browser.Click(NavigationBarParser.GetDorfButton(browser.CurrentPage, dorf));
            if (result.IsFailed) return result;

            result = await browser.WaitPageChanged(GetDorfPath(dorf));
            if (result.IsFailed) return result;

            return Result.Ok();
        }

        private static int GetCurrentDorf(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return 0;
            return uri.AbsolutePath switch
            {
                "/resources" => 1,
                "/village" => 2,
                _ => 0,
            };
        }

        private static string GetDorfPath(int dorf)
        {
            return dorf switch
            {
                1 => "resources",
                2 => "village",
                _ => throw new ArgumentOutOfRangeException(nameof(dorf), dorf, "Dorf must be 1 or 2."),
            };
        }
    }
}
