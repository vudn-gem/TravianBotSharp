using MainCore.Entities;
using MainCore.Parsers;

namespace MainCore.Test.Parsers
{
    public sealed class FarmTargetTroopsParserTest(PlaywrightFixture fixture) : ParserTestBase(fixture)
    {
        [Theory]
        [InlineData("<tr><td>none</td></tr>", true, false)]
        [InlineData("<tr><td><img class='unit u31'></td><td class='val'>2</td></tr>", true, true)]
        [InlineData("<tr><td><img class='unit u31'></td><td class='val'>0</td></tr>", true, false)]
        [InlineData("<tr><td><img class='unit u31'></td><td class='val'>?</td></tr>", false, false)]
        [InlineData("<tr><td>No information available!</td></tr>", false, false)]
        public async Task ReadsOnlyOasisTroopsNotReportHistory(string rows, bool success, bool troops)
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync($"""
                <div id="dialogContent"><div id="tileDetails" class="oasis">
                <a data-centermap href="https://example.com/karte.php?x=25&amp;y=184">Center map</a>
                <div id="map_details"><table id="troop_info"><tbody>{rows}</tbody></table>
                <div class="tabContainer"><table id="troop_info" class="rep"><tr><td><img class="unit"><span>99</span></td></tr></table></div>
                </div></div></div>
                """);
            var result = await FarmTargetTroopsParser.HasTroops(page, "https://example.com/karte.php?x=25&y=184");
            result.IsSuccess.ShouldBe(success);
            if (success) result.Value.ShouldBe(troops);
            (await FarmTargetTroopsParser.HasTroops(page, "https://example.com/karte.php?x=26&y=184")).IsFailed.ShouldBeTrue();
        }

        [Fact]
        public async Task ReadsOnlySelectedListsSlotsAndFindsExactEditLink()
        {
            await using var context = await CreateContextAsync();
            var page = await context.NewPageAsync();
            await page.SetContentAsync("""
                <div id="rallyPointFarmList">
                <div class="farmListWrapper"><div class="farmListHeader"><div data-list="6706"></div></div>
                <table class="slots"><tbody><tr class="slot disabled"><td><input name="selectOne" data-slot-id="637157"></td>
                <td class="target"><a href="/karte.php?x=-3&amp;y=0">oasis</a></td></tr></tbody></table></div>
                <div class="farmListWrapper"><div class="farmListHeader"><div data-list="56"></div></div>
                <table class="slots"><tbody><tr class="slot"><td><input name="selectOne" data-slot-id="2"></td></tr></tbody></table></div></div>
                <div id="dialogContent"><div id="tileDetails"><div class="options">
                <a onclick="SLOT_DIALOG_TYPE_CREATE">Add to farm list</a>
                <a onclick="SLOT_DIALOG_TYPE_EDIT, listId: 56, editSlotIds: [637157]">Edit farm list wrong</a>
                <a onclick="SLOT_DIALOG_TYPE_EDIT, listId: 6706, editSlotIds: [637157]">Edit farm list correct</a>
                </div></div></div>
                """);
            var result = await FarmTargetTroopsParser.ReadTargets(page, new FarmId(6706));
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldHaveSingleItem();
            result.Value[0].Disabled.ShouldBeTrue();
            var edit = await FarmTargetTroopsParser.FindEditLink(page, new FarmId(6706), "637157");
            edit.IsSuccess.ShouldBeTrue();
            (await edit.Value.InnerTextAsync()).ShouldBe("Edit farm list correct");
            (await FarmTargetTroopsParser.FindEditLink(page, new FarmId(6706), "2")).IsFailed.ShouldBeTrue();
        }
    }
}
