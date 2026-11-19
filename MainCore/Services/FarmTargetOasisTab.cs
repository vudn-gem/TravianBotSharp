namespace MainCore.Services
{
    public static class FarmTargetOasisTab
    {
        public static async Task<Result> Run(
            IChromeBrowser browser,
            string targetHref,
            ILocator targetLink,
            Func<IPage, Task<Result>> action)
        {
            var farmPage = browser.CurrentPage;
            var current = new Uri(farmPage.Url);
            var target = ResolveTarget(current, targetHref);
            if (target.IsFailed) return Result.Fail(target.Errors);

            Result result = Result.Fail("Oasis target was not processed.");
            Result closed = Result.Ok();
            try
            {
                var opened = await browser.Click(targetLink, openInNewTab: true);
                if (opened.IsFailed)
                    result = Result.Fail(opened.Errors);
                else
                {
                    var page = browser.CurrentPage;
                    opened = await browser.Wait(page.Locator("#tileDetails.oasis"));
                    if (opened.IsFailed)
                        result = Result.Fail(opened.Errors);
                    else
                    {
                        await browser.DelayClick();
                        result = await action(page);
                    }
                }
            }
            finally
            {
                if (browser.CurrentPage != farmPage)
                    closed = await browser.CloseNewTab();
            }

            if (closed.IsFailed)
                return Result.Fail(result.Errors.Concat(closed.Errors).ToList());
            return result;
        }

        public static async Task<Result<T>> Run<T>(
            IChromeBrowser browser,
            string targetHref,
            ILocator targetLink,
            Func<IPage, Task<Result<T>>> action)
        {
            var farmPage = browser.CurrentPage;
            var current = new Uri(farmPage.Url);
            var target = ResolveTarget(current, targetHref);
            if (target.IsFailed) return Result.Fail(target.Errors);

            Result<T> result = Result.Fail("Oasis target was not processed.");
            Result closed = Result.Ok();
            try
            {
                var opened = await browser.Click(targetLink, openInNewTab: true);
                if (opened.IsFailed)
                    result = Result.Fail(opened.Errors);
                else
                {
                    var page = browser.CurrentPage;
                    opened = await browser.Wait(page.Locator("#tileDetails.oasis"));
                    if (opened.IsFailed)
                        result = Result.Fail(opened.Errors);
                    else
                    {
                        await browser.DelayClick();
                        result = await action(page);
                    }
                }
            }
            finally
            {
                if (browser.CurrentPage != farmPage)
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
                resolved.AbsolutePath == "/map" &&
                System.Text.RegularExpressions.Regex.IsMatch(resolved.Query, @"(?:^\?|&)x=-?\d+(?:&|$)") &&
                System.Text.RegularExpressions.Regex.IsMatch(resolved.Query, @"(?:^\?|&)y=-?\d+(?:&|$)"))
                return Result.Ok(resolved);
            return Result.Fail("Invalid target map link.");
        }
    }
}
