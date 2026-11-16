namespace MainCore.Services
{
    public static class FarmTargetOasisTab
    {
        public static async Task<Result> Run(
            IChromeBrowser browser,
            string targetHref,
            Func<IPage, Task<Result>> action)
        {
            var current = new Uri(browser.CurrentPage.Url);
            var target = ResolveTarget(current, targetHref);
            if (target.IsFailed) return Result.Fail(target.Errors);

            var opened = await browser.OpenNewTab();
            if (opened.IsFailed) return Result.Fail(opened.Errors);
            Result result = Result.Fail("Oasis target was not processed.");
            Result closed = Result.Ok();
            try
            {
                opened = await browser.Navigate(target.Value.AbsoluteUri);
                if (opened.IsFailed)
                    result = Result.Fail(opened.Errors);
                else
                {
                    var page = browser.CurrentPage;
                    opened = await browser.Wait(page.Locator("#tileDetails.oasis"));
                    result = opened.IsFailed
                        ? Result.Fail(opened.Errors)
                        : await action(page);
                }
            }
            finally
            {
                closed = await browser.CloseNewTab();
            }

            if (closed.IsFailed)
                return Result.Fail(result.Errors.Concat(closed.Errors).ToList());
            return result;
        }

        public static async Task<Result<T>> Run<T>(
            IChromeBrowser browser,
            string targetHref,
            Func<IPage, Task<Result<T>>> action)
        {
            var current = new Uri(browser.CurrentPage.Url);
            var target = ResolveTarget(current, targetHref);
            if (target.IsFailed) return Result.Fail(target.Errors);

            var opened = await browser.OpenNewTab();
            if (opened.IsFailed) return Result.Fail(opened.Errors);
            Result<T> result = Result.Fail("Oasis target was not processed.");
            Result closed = Result.Ok();
            try
            {
                opened = await browser.Navigate(target.Value.AbsoluteUri);
                if (opened.IsFailed)
                    result = Result.Fail(opened.Errors);
                else
                {
                    var page = browser.CurrentPage;
                    opened = await browser.Wait(page.Locator("#tileDetails.oasis"));
                    result = opened.IsFailed
                        ? Result.Fail(opened.Errors)
                        : await action(page);
                }
            }
            finally
            {
                closed = await browser.CloseNewTab();
            }

            if (closed.IsFailed)
                return Result.Fail(result.Errors.Concat(closed.Errors).ToList());
            return result;
        }

        private static Result<Uri> ResolveTarget(Uri current, string targetHref)
        {
            if (Uri.TryCreate(current, targetHref, out var resolved) &&
                resolved.Scheme == current.Scheme && resolved.Authority == current.Authority &&
                resolved.AbsolutePath == "/karte.php" &&
                System.Text.RegularExpressions.Regex.IsMatch(resolved.Query, @"(?:^\?|&)x=-?\d+(?:&|$)") &&
                System.Text.RegularExpressions.Regex.IsMatch(resolved.Query, @"(?:^\?|&)y=-?\d+(?:&|$)"))
                return Result.Ok(resolved);
            return Result.Fail("Invalid target map link.");
        }
    }
}
