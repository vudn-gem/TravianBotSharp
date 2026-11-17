namespace MainCore.Services
{
    public static class HeroOasisSettings
    {
        public static Result Validate(HeroOasisSetting setting)
        {
            if (setting.MinAnimal < 0 || setting.MaxAnimal < setting.MinAnimal)
                return Result.Fail("Animal limits must satisfy 0 <= min animal <= max animal.");
            if (setting.SafeAnimal < 0 || (setting.SafeAnimal != 0 &&
                (setting.SafeAnimal <= setting.MinAnimal || setting.SafeAnimal > setting.MaxAnimal)))
                return Result.Fail("Safe animal must be zero (disabled) or satisfy min animal < safe animal <= max animal.");
            if (setting.AllowedAnimals < 0 || (setting.AllowedAnimals & ~1023) != 0)
                return Result.Fail("Allowed animal selection is invalid.");
            if (setting.SteppeRiders is < 0 or > 999999 ||
                setting.Marksmen is < 0 or > 999999 || setting.Marauders is < 0 or > 999999)
                return Result.Fail("Sub troop counts must be between 0 and 999999.");
            if (setting.MinHeroHealth is < 0 or > 100 || setting.MaxHeroHealthLost is < 0 or > 100)
                return Result.Fail("Hero health thresholds must be between 0 and 100.");
            if (setting.MaxSubTroopLost < 0)
                return Result.Fail("Max sub troop lost must be zero or greater.");
            return Result.Ok();
        }

        public static bool AllowsAnimals(HeroOasisSetting setting, IReadOnlyList<OasisAnimalTroop> animals)
        {
            var total = animals.Sum(x => x.Count);
            return (setting.UseTroop ? total >= setting.MinAnimal : total > setting.MinAnimal) &&
                total > 0 && total <= setting.MaxAnimal &&
                animals.All(x => x.Count == 0 || (x.UnitId is >= 31 and <= 40 &&
                    (setting.AllowedAnimals & (1 << (x.UnitId - 31))) != 0));
        }

        public static bool AllowsSafeClear(HeroOasisSetting setting, IReadOnlyList<OasisAnimalTroop> animals)
        {
            if (setting.SafeAnimal <= 0) return false;
            var total = animals.Sum(x => x.Count);
            return total > setting.MinAnimal && total < setting.SafeAnimal &&
                animals.All(x => x.Count == 0 || (x.UnitId is >= 31 and <= 40 &&
                    (setting.AllowedAnimals & (1 << (x.UnitId - 31))) != 0));
        }

        public static bool AllowsResult(HeroOasisSetting setting, HeroSimulationResult result) =>
            result.HeroSurvives && result.HealthAfter >= setting.MinHeroHealth &&
            result.HealthBefore - result.HealthAfter <= setting.MaxHeroHealthLost &&
            result.SubTroopLost <= setting.MaxSubTroopLost;

        public static int[] Troops(HeroOasisSetting setting) =>
            [0, 0, 0, setting.SteppeRiders, setting.Marksmen, setting.Marauders, 0, 0, 0, 0, 1];

        public static int[] RaidTroops(OasisRaidSettings setting, long animalCount)
        {
            var (steppe, marksmen, marauders) = setting.RequiredTroops(animalCount);
            return [0, 0, 0, checked((int)steppe), checked((int)marksmen), checked((int)marauders), 0, 0, 0, 0, 0];
        }
    }
}
