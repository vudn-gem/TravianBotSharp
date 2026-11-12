using MainCore.Services;
using MainCore.Tasks;

namespace MainCore.Test
{
    public class ConquestPlannerTest
    {
        [Fact]
        public void DistanceWrapsAroundMapEdges()
        {
            var result = ConquestPlanner.Calculate(
                200, 0, -200, 0,
                slowestTroopSpeed: 6,
                tournamentSquareLevel: 0,
                bootsPercent: 0,
                artifactMultiplier: 1,
                delaySeconds: 0,
                arrivalAt: DateTime.Now.AddHours(1));

            result.IsSuccess.ShouldBeTrue();
            result.Value.Distance.ShouldBe(1);
        }

        [Fact]
        public void TravelTimeMatchesPlanFormula()
        {
            var arrivalAt = new DateTime(2026, 11, 13, 13, 0, 0);
            var result = ConquestPlanner.Calculate(
                -62, 108, -31, 105,
                slowestTroopSpeed: 6,
                tournamentSquareLevel: 15,
                bootsPercent: 0,
                artifactMultiplier: 1,
                delaySeconds: 0.4,
                arrivalAt: arrivalAt);

            result.IsSuccess.ShouldBeTrue();
            result.Value.TravelTime.ShouldBe(TimeSpan.FromSeconds(13671));
            result.Value.SendAt.ShouldBe(arrivalAt - TimeSpan.FromSeconds(13671.4));
        }

        [Fact]
        public void ParseTroopWavesSupportsCommaSeparatedAndMultilineInput()
        {
            const string firstWave = "19/0/0/0/0/0/0/1/0/0/0";
            var result = ConquestTask.ParseTroopWaves($"{firstWave},{firstWave}{Environment.NewLine}{firstWave}");

            result.IsSuccess.ShouldBeTrue();
            result.Value.Count.ShouldBe(3);
            result.Value[0].Count.ShouldBe(11);
        }

        [Theory]
        [InlineData("19/0/0")]
        [InlineData("19/-1/0/0/0/0/0/0/0/0/0")]
        [InlineData("0/0/0/0/0/0/0/0/0/0/0")]
        public void ParseTroopWavesRejectsInvalidWave(string input)
        {
            var result = ConquestTask.ParseTroopWaves(input);

            result.IsFailed.ShouldBeTrue();
        }
    }
}
