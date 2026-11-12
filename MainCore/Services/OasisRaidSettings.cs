namespace MainCore.Services
{
    public sealed record OasisRaidSettings(
        IReadOnlySet<int> AllowedAnimals,
        int SafetyFactor,
        OasisAttackTroop AttackTroop,
        int RaidSteppeRiders,
        int RaidMarksmen,
        int RaidMarauders,
        int LimitAnimal = 0)
    {
        public static readonly IReadOnlyDictionary<AccountSettingEnums, int> LegacyAnimalSettings =
            new Dictionary<AccountSettingEnums, int>
            {
                { AccountSettingEnums.AllowRaidRatOasis, 1 << 0 },
                { AccountSettingEnums.AllowRaidSpiderOasis, 1 << 1 },
                { AccountSettingEnums.AllowRaidSnakeOasis, 1 << 2 },
                { AccountSettingEnums.AllowRaidBatOasis, 1 << 3 },
                { AccountSettingEnums.AllowRaidWildBoarOasis, 1 << 4 },
                { AccountSettingEnums.AllowRaidWolfOasis, 1 << 5 },
                { AccountSettingEnums.AllowRaidBearOasis, 1 << 6 },
                { AccountSettingEnums.AllowRaidCrocodileOasis, 1 << 7 },
                { AccountSettingEnums.AllowRaidTigerOasis, 1 << 8 },
                { AccountSettingEnums.AllowRaidElephantOasis, 1 << 9 },
            };

        public static Result<OasisRaidSettings> Read(AppDbContext context, AccountId accountId, FarmId farmId)
        {
            var setting = context.FarmOasisSettings
                .Where(x => x.FarmId == farmId.Value &&
                    context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == accountId.Value))
                .SingleOrDefault();
            if (setting is null)
                return Result.Fail("Farm list oasis settings are missing or the farm list does not belong to this account.");
            if (setting.SafetyFactor < 1)
                return Result.Fail("Farm list oasis raid safety factor must be at least 1.");
            if (setting.LimitAnimal < 0)
                return Result.Fail("Farm list animal limit must be zero or greater.");
            if (!Enum.IsDefined(setting.AttackTroop))
                return Result.Fail("Farm list attack troop is invalid.");
            if (!IsValidRaidComposition(setting.RaidSteppeRiders, setting.RaidMarksmen, setting.RaidMarauders))
                return Result.Fail("Farm list raid troop counts must be between 0 and 999999, with at least one troop.");
            var allowed = Enumerable.Range(31, 10)
                .Where(id => (setting.AllowedAnimals & (1 << (id - 31))) != 0).ToHashSet();
            return Result.Ok(new OasisRaidSettings(allowed, setting.SafetyFactor, setting.AttackTroop,
                setting.RaidSteppeRiders, setting.RaidMarksmen, setting.RaidMarauders, setting.LimitAnimal));
        }

        public static bool IsValidRaidComposition(int steppe, int marksmen, int marauders) =>
            steppe is >= 0 and <= 999999 && marksmen is >= 0 and <= 999999 &&
            marauders is >= 0 and <= 999999 && (steppe > 0 || marksmen > 0 || marauders > 0);

        public (long Steppe, long Marksmen, long Marauders) RequiredTroops(long animalCount)
        {
            if (animalCount == 0) return (RaidSteppeRiders, RaidMarksmen, RaidMarauders);
            var amount = checked(animalCount * SafetyFactor);
            return AttackTroop switch
            {
                OasisAttackTroop.SteppeRider => (amount, 0, 0),
                OasisAttackTroop.Marksman => (0, amount, 0),
                OasisAttackTroop.Marauder => (0, 0, amount),
                _ => throw new InvalidOperationException("Invalid oasis attack troop."),
            };
        }
    }
}
