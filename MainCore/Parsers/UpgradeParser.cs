using System.Globalization;

namespace MainCore.Parsers
{
    public static class UpgradeParser
    {
        public static async Task<ILocator> GetPanel(IPage page, BuildingEnums building)
        {
            if (await IsEmptySite(page))
            {
                return page.Locator($"#contract_building{(int)building} div.upgradeBuilding");
            }
            else
            {
                return page.Locator("div.upgradeBuilding");
            }
        }

        public static async Task<ILocator> GetRequiredResource(IPage page, BuildingEnums building)
        {
            var panel = await GetPanel(page, building);
            var nodes = panel.Locator("#contract div.resourceWrapper div.resource");
            return nodes;
        }

        public static async Task<TimeSpan> GetTimeWhenEnoughResource(IPage page, BuildingEnums building)
        {
            var panel = await GetPanel(page, building);
            var node = panel.Locator("#contract div.errorMessage span.timer");
            var timeValue = await node.GetAttributeAsync("value");
            return TimeSpan.FromSeconds(int.Parse(timeValue ?? "0"));
        }

        public static async Task<TimeSpan?> ReadUpgradeDuration(IPage page, BuildingEnums building)
        {
            var (section, _) = await GetNormalUpgradeControls(page, building);
            const string durationSelector = ":is(.clocks, .duration, .buildDuration)";
            var duration = section.Locator($"{durationSelector}:not(:has({durationSelector}))")
                .Filter(new() { Visible = true });
            if (await duration.CountAsync() == 0)
            {
                var panel = await GetPanel(page, building);
                duration = panel.Locator($"#contract {durationSelector}:not(:has({durationSelector}))" +
                    ":not(.section2 *, .errorMessage *)")
                    .Filter(new() { Visible = true });
            }
            if (await duration.CountAsync() != 1) return null;

            var text = (await duration.InnerTextAsync()).Trim();
            var parts = text.Split(':');
            if (parts.Length == 3 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
                minutes < 60 && seconds < 60)
            {
                var elapsedSeconds = hours * 3600L + minutes * 60L + seconds;
                if (elapsedSeconds <= TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond)
                    return TimeSpan.FromTicks(elapsedSeconds * TimeSpan.TicksPerSecond);
            }

            return TimeSpan.TryParseExact(text, "d\\.hh\\:mm\\:ss", CultureInfo.InvariantCulture, out var elapsed)
                ? elapsed
                : null;
        }

        public static async Task<ILocator> GetFillUpButton(IPage page, BuildingEnums building)
        {
            var panel = await GetPanel(page, building);
            var node = panel.Locator("#contract .resourceWrapper .resource.transfer.fillUp");
            return node.First;
        }

        public static async Task<ILocator> GetNormalButton(IPage page, BuildingEnums building)
        {
            var (section, button) = await GetNormalUpgradeControls(page, building);
            return section.Locator(button);
        }

        private static async Task<(ILocator Section, string Button)> GetNormalUpgradeControls(IPage page, BuildingEnums building) =>
            await IsEmptySite(page)
                ? (page.Locator($"#contract_building{(int)building} div.upgradeButtonsContainer .section1"), "button.new")
                : (page.Locator("div.upgradeButtonsContainer .section1"), "button.build");

        public static async Task<ILocator> GetSpecialButton(IPage page, BuildingEnums building)
        {
            if (await IsEmptySite(page))
            {
                return page.Locator($"#contract_building{(int)building} div.upgradeButtonsContainer .section2 button.videoFeatureButton");
            }
            else
            {
                return page.Locator("div.upgradeButtonsContainer .section2 button.videoFeatureButton");
            }
        }

        private static async Task<bool> IsEmptySite(IPage page)
        {
            var parentPanel = page.Locator("#build");
            var classes = await parentPanel.GetAttributeAsync("class");
            return classes is not null && classes.Contains("gid0");
        }
    }
}