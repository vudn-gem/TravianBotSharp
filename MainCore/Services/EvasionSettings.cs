namespace MainCore.Services
{
    public sealed record EvasionDestination(int VillageId, string Name, int X, int Y)
    {
        public string Display => VillageId == 0 ? "None" : $"{Name} ({X}|{Y})";
    }

    public sealed record EvasionSettings(
        bool Enabled, TroopEnums TrainTroop, EvasionDestination? ResourceDestination,
        bool EvadeTroops, int X, int Y, int LeadSeconds)
    {
        public static bool IsEnabled(AppDbContext context, AccountId accountId) =>
            context.VillagesSetting.Any(setting =>
                setting.Setting == VillageSettingEnums.EvasionEnable && setting.Value == 1 &&
                context.Villages.Any(village => village.Id == setting.VillageId && village.AccountId == accountId.Value));

        public static Result<EvasionSettings> Read(AppDbContext context, AccountId accountId, VillageId villageId,
            Dictionary<VillageSettingEnums, int>? values = null)
        {
            var village = context.Villages.AsNoTracking().SingleOrDefault(x => x.Id == villageId.Value && x.AccountId == accountId.Value);
            if (village is null) return Result.Fail("Evasion village does not belong to the selected account.");
            values ??= context.VillagesSetting.AsNoTracking().Where(x => x.VillageId == villageId.Value)
                .ToDictionary(x => x.Setting, x => x.Value);
            int Get(VillageSettingEnums key) => values.GetValueOrDefault(key, AppDbContext.VillageDefaultSettings[key]);

            if (Get(VillageSettingEnums.EvasionEnable) is not (0 or 1) ||
                Get(VillageSettingEnums.EvasionTroopEnable) is not (0 or 1))
                return Result.Fail("Evasion switches must be either 0 or 1.");

            var troop = (TroopEnums)Get(VillageSettingEnums.EvasionTrainTroop);
            var tribe = (TribeEnums)Get(VillageSettingEnums.Tribe);
            var trainable = tribe.GetInfantryTroops().Concat(tribe.GetCavalryTroops()).Concat(tribe.GetSiegeTroops());
            if (troop != TroopEnums.None && !trainable.Contains(troop))
                return Result.Fail("Evasion training troop is not trainable by this village's tribe.");
            var lead = Get(VillageSettingEnums.EvasionLeadSeconds);
            if (lead is < 1 or > 3600) return Result.Fail("Evasion lead time must be between 1 and 3600 seconds.");
            var x = Get(VillageSettingEnums.EvasionX);
            var y = Get(VillageSettingEnums.EvasionY);
            var evade = Get(VillageSettingEnums.EvasionTroopEnable) == 1;
            if (evade && (x is < -400 or > 400 || y is < -400 or > 400))
                return Result.Fail("Evasion coordinates must be between -400 and 400.");
            if (evade && x == village.X && y == village.Y)
                return Result.Fail("Evasion troops must be sent to coordinates outside the origin village.");

            EvasionDestination? destination = null;
            var destinationId = Get(VillageSettingEnums.EvasionResourceVillage);
            if (destinationId != 0)
            {
                var target = context.Villages.AsNoTracking().SingleOrDefault(v =>
                    v.Id == destinationId && v.AccountId == accountId.Value && v.Id != villageId.Value);
                if (target is null) return Result.Fail("Evasion resource destination must be another village in this account.");
                destination = new(target.Id, target.Name, target.X, target.Y);
            }
            return Result.Ok(new EvasionSettings(Get(VillageSettingEnums.EvasionEnable) == 1,
                troop, destination, evade, x, y, lead));
        }
    }
}
