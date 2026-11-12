namespace MainCore.Services
{
    public sealed record OasisRaidSettings(IReadOnlySet<int> AllowedAnimals, int SafetyFactor)
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
            var allowed = Enumerable.Range(31, 10)
                .Where(id => (setting.AllowedAnimals & (1 << (id - 31))) != 0).ToHashSet();
            return Result.Ok(new OasisRaidSettings(allowed, setting.SafetyFactor));
        }
    }
}
