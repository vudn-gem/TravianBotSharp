using MainCore.Entities;
using MainCore.Parsers;
using MainCore.Services;

namespace MainCore.Test
{
    public sealed class HeroOasisSettingsTest
    {
        [Theory]
        [InlineData(50, 50, 0, true, true)]
        [InlineData(90, 50, 0, true, true)]
        [InlineData(90, 49, 0, true, false)]
        [InlineData(90, 51, 2, true, true)]
        [InlineData(90, 51, 3, true, false)]
        [InlineData(91, 50, 0, true, false)]
        [InlineData(90, 50, 0, false, false)]
        public void AppliesInclusivePostSimulationLimits(int before, int after, int lost, bool alive, bool allowed)
        {
            var settings = new HeroOasisSetting { MinHeroHealth = 50, MaxHeroHealthLost = 40, MaxSubTroopLost = 2 };
            HeroOasisSettings.AllowsResult(settings, new(before, after, lost, alive)).ShouldBe(allowed);
        }

        [Theory]
        [InlineData(2, 31, true)]
        [InlineData(5, 31, true)]
        [InlineData(1, 31, false)]
        [InlineData(6, 31, false)]
        [InlineData(3, 32, false)]
        public void AppliesAnimalRangeAndAllowedTypes(int count, int type, bool allowed)
        {
            var settings = new HeroOasisSetting { MinAnimal = 2, MaxAnimal = 5, AllowedAnimals = 1 };
            HeroOasisSettings.AllowsAnimals(settings, [new(type, "Animal", count)]).ShouldBe(allowed);
        }

        [Fact]
        public void ValidatesSettingsAndAllowsHeroOnly()
        {
            var settings = new HeroOasisSetting { SteppeRiders = 0, Marksmen = 0, Marauders = 0 };
            HeroOasisSettings.Validate(settings).IsSuccess.ShouldBeTrue();
            HeroOasisSettings.Troops(settings).ShouldBe([0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1]);
            settings.MaxAnimal = settings.MinAnimal - 1;
            HeroOasisSettings.Validate(settings).IsFailed.ShouldBeTrue();
            settings.MaxAnimal = 100;
            settings.MinHeroHealth = 101;
            HeroOasisSettings.Validate(settings).IsFailed.ShouldBeTrue();
            settings.MinHeroHealth = 50;
            settings.SteppeRiders = -1;
            HeroOasisSettings.Validate(settings).IsFailed.ShouldBeTrue();
        }

        [Theory]
        [InlineData("1794469617", "In 1:29:25 hours", 5365)]
        [InlineData("1794469617", "In 25:00:00 hours", 90000)]
        public void ComputesReturnFromAbsoluteArrival(string unix, string duration, int seconds)
        {
            var result = HeroOasisParser.ParseReturnTime(unix, duration);
            result.IsSuccess.ShouldBeTrue();
            result.Value.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(long.Parse(unix)).AddSeconds(seconds).LocalDateTime);
        }

        [Theory]
        [InlineData("", "In 1:00:00 hours")]
        [InlineData("1794469617", "unknown")]
        [InlineData("1794469617", "In 1:60:00 hours")]
        [InlineData("253402300800", "In 1:00:00 hours")]
        public void RejectsUnknownReturnTimes(string unix, string duration) =>
            HeroOasisParser.ParseReturnTime(unix, duration).IsFailed.ShouldBeTrue();
    }
}
