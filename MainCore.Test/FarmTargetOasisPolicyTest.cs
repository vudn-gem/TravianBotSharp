using MainCore.Parsers;
using MainCore.Services;

namespace MainCore.Test
{
    public sealed class FarmTargetOasisPolicyTest
    {
        private static readonly HashSet<int> Allowed = [31, 32];

        [Theory]
        [InlineData(true, 1, 0)]
        [InlineData(true, 1, 1)]
        [InlineData(false, 2, 1)]
        [InlineData(false, 3, 0)]
        public void SafeAllowedAnimalsAreBasedOnTotalCount(
            bool expected, long firstCount, long secondCount)
        {
            var troops = new[]
            {
                new OasisAnimalTroop(31, "Rat", firstCount),
                new OasisAnimalTroop(32, "Spider", secondCount),
            };
            FarmTargetOasisPolicy.HasOnlySafeAllowedAnimals(troops, Allowed, 2).ShouldBe(expected);
        }

        [Fact]
        public void RequiresEveryPresentAnimalTypeToBeAllowed()
        {
            var troops = new[]
            {
                new OasisAnimalTroop(31, "Rat", 1),
                new OasisAnimalTroop(33, "Snake", 1),
            };
            FarmTargetOasisPolicy.HasOnlySafeAllowedAnimals(troops, Allowed, 2).ShouldBeFalse();
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(-1, false)]
        public void EmptyOasisOrInvalidLimitIsNotSafe(int count, bool expected)
        {
            IReadOnlyList<OasisAnimalTroop> troops = count < 0
                ? Array.Empty<OasisAnimalTroop>()
                : new[] { new OasisAnimalTroop(31, "Rat", count) };
            FarmTargetOasisPolicy.HasOnlySafeAllowedAnimals(troops, Allowed, count < 0 ? count : 1)
                .ShouldBe(expected);
        }
    }
}
