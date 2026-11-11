using MainCore.Parsers;
using MainCore.Entities;

namespace MainCore.Test.Parsers
{
    public sealed class OffensiveReportParserTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Fact]
        public async Task ReadsOnlyUnreadLostReports()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <table><tbody>
                <tr><td><input class="report" name="ids[]" value="830513818"></td>
                <td class="sub newMessage"><img class="messageStatusUnread"><img class="iReport3">
                <div><a href="?id=830513818%7Ctoken&amp;s=1">eye raids 0</a></div></td></tr>
                <tr><td><input class="report" name="ids[]" value="2"></td>
                <td class="sub"><img class="messageStatusRead"><img class="iReport3">
                <div><a href="?id=2%7Ctoken">read loss</a></div></td></tr>
                <tr><td><input class="report" name="ids[]" value="3"></td>
                <td class="sub newMessage"><img class="messageStatusUnread"><img class="iReport1">
                <div><a href="?id=3%7Ctoken">unread win</a></div></td></tr>
                </tbody></table>
                """);
            var result = await OffensiveReportParser.ReadUnreadLostReports(page);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldHaveSingleItem();
            result.Value[0].Id.ShouldBe("830513818");
            result.Value[0].Href.ShouldBe("?id=830513818%7Ctoken&s=1");
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public async Task ResolvesAttackerNotDefenderAndRejectsDuplicateVillageNames(bool duplicate, bool success)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div class="body"><div class="role attacker"><div class="outcome"><i class="attackLost"></i></div>
                <div class="troopHeadline"><a class="village" href="/karte.php?d=77571">7</a></div></div>
                <div class="role defender"><div class="troopHeadline"><a class="village">enemy</a></div></div></div>
                <div id="sidebarBoxVillageList"><div class="listEntry village" data-did="23414"><span class="name">7</span></div></div>
                """);
            if (duplicate)
                await page.EvaluateAsync("""() => document.querySelector('#sidebarBoxVillageList').insertAdjacentHTML('beforeend', '<div class="listEntry village" data-did="2"><span class="name">7</span></div>')""");
            var result = await OffensiveReportParser.ReadAttackerVillage(page);
            result.IsSuccess.ShouldBe(success);
            if (success) result.Value.ShouldBe(new VillageId(23414));
        }

        [Theory]
        [InlineData("", "Edit target", false)]
        [InlineData("6706", "Edit target", true)]
        [InlineData("6706", "Add target", false)]
        public async Task RequiresSelectedFarmListAndEditDialog(string listId, string title, bool success)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync($"""
                <div class="contentV2"><h3>{title}</h3><div id="farmListTargetForm"><select name="listId"><option value="{listId}">list</option></select>
                <div class="deactivateSlot"><input type="checkbox" name="isActive"></div></div></div>
                """);
            (await OffensiveReportParser.ReadSelectedFarmList(page)).IsSuccess.ShouldBe(success);
        }
    }
}
