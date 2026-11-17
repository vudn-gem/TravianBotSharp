using MainCore.Entities;
using MainCore.Parsers;
using MainCore.Tasks;

namespace MainCore.Test
{
    public sealed class OasisTaskDisplayTest
    {
        [Fact]
        public void TaskNamesUseFarmNameWithoutChangingQueueKeys()
        {
            var account = new AccountId(1);
            var farm = new FarmId(1813);
            var clear = new ClearFarmOasisTask.Task(account, farm, "Nearby oasis");
            var hero = new HeroClearFarmOasisTask.Task(account, farm, "Nearby oasis");
            clear.Description.ShouldBe("Clear oasis in farm list Nearby oasis");
            hero.Description.ShouldBe("Hero clear oasis in farm list Nearby oasis");
            clear.Key.ShouldBe(new ClearFarmOasisTask.Task(account, farm, "Renamed").Key);
            hero.Key.ShouldBe(new HeroClearFarmOasisTask.Task(account, farm, "Renamed").Key);
        }

        [Theory]
        [InlineData(false, true, "Check")]
        [InlineData(true, false, "Activate")]
        [InlineData(false, false, "Deactive")]
        public void CheckOasisTaskNamesUseFarmNameWithoutChangingQueueKeys(bool activate, bool synchronize, string action)
        {
            var task = new CheckFarmTargetOasisTask.Task(new AccountId(1), new FarmId(1813),
                activate, synchronize, "Nearby oasis");
            task.Description.ShouldBe($"{action} oasis in farm list Nearby oasis");
            task.Key.ShouldBe(new CheckFarmTargetOasisTask.Task(new AccountId(1), new FarmId(1813),
                activate, synchronize, "Renamed").Key);
        }

        [Theory]
        [InlineData("/karte.php?x=25&y=184", "25|184")]
        [InlineData("/karte.php?y=-6&x=-125", "-125|-6")]
        [InlineData("https://example.com/karte.php?x=0&y=-1", "0|-1")]
        [InlineData("/karte.php?x=unknown&y=1", "unknown coordinates; slot 41997")]
        public void TargetCoordinatesComeFromMapLink(string href, string expected)
        {
            new FarmTargetSlot("41997", href, "Custom target name", false)
                .Coordinates.ShouldBe(expected);
        }
    }
}
