namespace MainCore.Models
{
    public sealed class ConquestWave
    {
        public const int MaxWaves = 8;
        public const int SecondTargetMinimumCatapults = 20;

        public ConquestWave(IEnumerable<long> troops, int catapultTarget1 = 99, int? catapultTarget2 = null)
        {
            Troops = Array.AsReadOnly(troops.ToArray());
            CatapultTarget1 = catapultTarget1;
            CatapultTarget2 = catapultTarget2;
        }

        public IReadOnlyList<long> Troops { get; }
        public int CatapultTarget1 { get; }
        public int? CatapultTarget2 { get; }

        public Result Validate()
        {
            if (Troops.Count != 11 || Troops.Any(amount => amount < 0))
                return Result.Fail("Each wave must contain 11 non-negative troop counts.");
            if (Troops.All(amount => amount == 0))
                return Result.Fail("Each wave must contain at least one troop.");
            if (!ConquestCatapultTargets.Options.Any(option => option.Value == CatapultTarget1) ||
                (CatapultTarget2.HasValue && !ConquestCatapultTargets.Options.Any(option => option.Value == CatapultTarget2)))
                return Result.Fail("Select a valid catapult target.");
            if (CatapultTarget2.HasValue && Troops[7] < SecondTargetMinimumCatapults)
                return Result.Fail($"A second target requires at least {SecondTargetMinimumCatapults} catapults.");

            return Result.Ok();
        }
    }

    public sealed record ConquestCatapultTarget(int? Value, string Name, string Group);

    public static class ConquestCatapultTargets
    {
        public static IReadOnlyList<ConquestCatapultTarget> Options { get; } = Array.AsReadOnly<ConquestCatapultTarget>(
        [
            new(99, "Random", "General"),
            new(1, "Woodcutter", "Resources"),
            new(2, "Clay Pit", "Resources"),
            new(3, "Iron Mine", "Resources"),
            new(4, "Cropland", "Resources"),
            new(5, "Sawmill", "Resources"),
            new(6, "Brickyard", "Resources"),
            new(7, "Iron Foundry", "Resources"),
            new(8, "Grain Mill", "Resources"),
            new(9, "Bakery", "Resources"),
            new(10, "Warehouse", "Infrastructure"),
            new(11, "Granary", "Infrastructure"),
            new(15, "Main Building", "Infrastructure"),
            new(17, "Marketplace", "Infrastructure"),
            new(18, "Embassy", "Infrastructure"),
            new(24, "Town Hall", "Infrastructure"),
            new(25, "Residence", "Infrastructure"),
            new(44, "Command Center", "Infrastructure"),
            new(26, "Palace", "Infrastructure"),
            new(27, "Treasury", "Infrastructure"),
            new(28, "Trade Office", "Infrastructure"),
            new(38, "Great Warehouse", "Infrastructure"),
            new(39, "Great Granary", "Infrastructure"),
            new(40, "Wonder Of The World", "Infrastructure"),
            new(13, "Smithy", "Military"),
            new(14, "Tournament Square", "Military"),
            new(16, "Rally Point", "Military"),
            new(19, "Barracks", "Military"),
            new(20, "Stable", "Military"),
            new(21, "Workshop", "Military"),
            new(22, "Academy", "Military"),
            new(29, "Great Barracks", "Military"),
            new(30, "Great Stable", "Military"),
            new(37, "Hero's Mansion", "Military"),
            new(41, "Horse Drinking Trough", "Military"),
            new(46, "Hospital", "Military"),
        ]);

        public static IReadOnlyList<ConquestCatapultTarget> OptionalOptions { get; } =
            Array.AsReadOnly<ConquestCatapultTarget>([new(null, "None", "General"), .. Options]);
    }
}
