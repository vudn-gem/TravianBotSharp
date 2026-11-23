using System.Collections.Immutable;

using StronglyTypedIds;

[assembly: StronglyTypedIdDefaults(backingType: StronglyTypedIdBackingType.Int, converters: StronglyTypedIdConverter.None)]

namespace MainCore.Infrasturecture.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        #region table

        public DbSet<Account> Accounts { get; set; }
        public DbSet<AccountInfo> AccountsInfo { get; set; }
        public DbSet<Access> Accesses { get; set; }
        public DbSet<AccountSetting> AccountsSetting { get; set; }
        public DbSet<HeroItem> HeroItems { get; set; }
        public DbSet<Village> Villages { get; set; }
        public DbSet<Building> Buildings { get; set; }
        public DbSet<QueueBuilding> QueueBuildings { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<Storage> Storages { get; set; }
        public DbSet<VillageSetting> VillagesSetting { get; set; }
        public DbSet<Farm> FarmLists { get; set; }
        public DbSet<FarmOasisSetting> FarmOasisSettings { get; set; }
        public DbSet<HeroOasisSetting> HeroOasisSettings { get; set; }
        public DbSet<ConquestPlan> ConquestPlans { get; set; }

        public void InitializeConquestPlans()
        {
            Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "ConquestPlans" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "AccountId" INTEGER NOT NULL,
                    "VillageId" INTEGER NOT NULL,
                    "TargetX" INTEGER NOT NULL,
                    "TargetY" INTEGER NOT NULL,
                    "WavesJson" TEXT NOT NULL,
                    "ArrivalAt" TEXT NOT NULL,
                    "SendAt" TEXT NOT NULL,
                    "PrepareAt" TEXT NOT NULL,
                    "DelaySeconds" REAL NOT NULL,
                    "OriginName" TEXT NOT NULL,
                    "Status" TEXT NOT NULL,
                    "LastError" TEXT NOT NULL,
                    "Attempts" INTEGER NOT NULL,
                    "ConfirmationAttempted" INTEGER NOT NULL,
                    "CancelRequested" INTEGER NOT NULL,
                    "IsFinished" INTEGER NOT NULL
                )
                """);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<FarmOasisSetting>()
                .HasOne<Farm>().WithOne()
                .HasForeignKey<FarmOasisSetting>(x => x.FarmId)
                .OnDelete(DeleteBehavior.Cascade);
            modelBuilder.Entity<HeroOasisSetting>()
                .HasOne<Farm>().WithOne()
                .HasForeignKey<HeroOasisSetting>(x => x.FarmId)
                .OnDelete(DeleteBehavior.Cascade);
        }

        public void InitializeFarmOasisSettings()
        {
            Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "HeroOasisSettings" (
                    "FarmId" INTEGER NOT NULL PRIMARY KEY,
                    "AllowedAnimals" INTEGER NOT NULL DEFAULT 27,
                    "MinAnimal" INTEGER NOT NULL DEFAULT 1,
                    "SafeAnimal" INTEGER NOT NULL DEFAULT 0,
                    "MaxAnimal" INTEGER NOT NULL DEFAULT 100,
                    "SteppeRiders" INTEGER NOT NULL DEFAULT 1,
                    "Marksmen" INTEGER NOT NULL DEFAULT 1,
                    "Marauders" INTEGER NOT NULL DEFAULT 1,
                    "UseTroop" INTEGER NOT NULL DEFAULT 0,
                    "MinHeroHealth" INTEGER NOT NULL DEFAULT 50,
                    "MaxHeroHealthLost" INTEGER NOT NULL DEFAULT 10,
                    "MaxSubTroopLost" INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY ("FarmId") REFERENCES "FarmLists" ("Id") ON DELETE CASCADE
                )
                """);
            var heroColumns = Database.SqlQueryRaw<string>(
                """SELECT "name" AS "Value" FROM pragma_table_info('HeroOasisSettings')""").ToHashSet();
            if (!heroColumns.Contains("SafeAnimal"))
                Database.ExecuteSqlRaw("""ALTER TABLE "HeroOasisSettings" ADD COLUMN "SafeAnimal" INTEGER NOT NULL DEFAULT 0""");
            if (!heroColumns.Contains("UseTroop"))
                Database.ExecuteSqlRaw("""ALTER TABLE "HeroOasisSettings" ADD COLUMN "UseTroop" INTEGER NOT NULL DEFAULT 0""");
            var heroSettingsFarms = HeroOasisSettings.Select(x => x.FarmId).ToHashSet();
            foreach (var farmId in FarmLists.Select(x => x.Id).ToList())
            {
                if (!heroSettingsFarms.Contains(farmId))
                    HeroOasisSettings.Add(new HeroOasisSetting { FarmId = farmId });
            }
            Database.ExecuteSqlRaw("""
                CREATE TABLE IF NOT EXISTS "FarmOasisSettings" (
                    "FarmId" INTEGER NOT NULL CONSTRAINT "PK_FarmOasisSettings" PRIMARY KEY,
                    "AllowedAnimals" INTEGER NOT NULL,
                    "CheckAllowedAnimals" INTEGER NOT NULL DEFAULT 0,
                    "SafeAnimal" INTEGER NOT NULL DEFAULT 1,
                    "CheckAutoActive" INTEGER NOT NULL DEFAULT 0,
                    "SafetyFactor" TEXT NOT NULL,
                    "AttackTroop" INTEGER NOT NULL DEFAULT 0,
                    "RaidSteppeRiders" INTEGER NOT NULL DEFAULT 2,
                    "RaidMarksmen" INTEGER NOT NULL DEFAULT 0,
                    "RaidMarauders" INTEGER NOT NULL DEFAULT 0,
                    "LimitAnimal" INTEGER NOT NULL DEFAULT 0,
                    CONSTRAINT "FK_FarmOasisSettings_FarmLists_FarmId"
                        FOREIGN KEY ("FarmId") REFERENCES "FarmLists" ("Id") ON DELETE CASCADE
                )
                """);

            var columns = Database.SqlQueryRaw<string>(
                """SELECT "name" AS "Value" FROM pragma_table_info('FarmOasisSettings')""").ToHashSet();
            if (!columns.Contains("AttackTroop"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "AttackTroop" INTEGER NOT NULL DEFAULT 0""");
            if (!columns.Contains("RaidSteppeRiders"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "RaidSteppeRiders" INTEGER NOT NULL DEFAULT 2""");
            if (!columns.Contains("RaidMarksmen"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "RaidMarksmen" INTEGER NOT NULL DEFAULT 0""");
            if (!columns.Contains("RaidMarauders"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "RaidMarauders" INTEGER NOT NULL DEFAULT 0""");
            if (!columns.Contains("LimitAnimal"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "LimitAnimal" INTEGER NOT NULL DEFAULT 0""");
            if (!columns.Contains("CheckAllowedAnimals"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "CheckAllowedAnimals" INTEGER NOT NULL DEFAULT 0""");
            if (!columns.Contains("SafeAnimal"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "SafeAnimal" INTEGER NOT NULL DEFAULT 1""");
            if (!columns.Contains("CheckAutoActive"))
                Database.ExecuteSqlRaw("""ALTER TABLE "FarmOasisSettings" ADD COLUMN "CheckAutoActive" INTEGER NOT NULL DEFAULT 0""");

            var existing = FarmOasisSettings.Select(x => x.FarmId).ToHashSet();
            var legacyKeys = OasisRaidSettings.LegacyAnimalSettings.Keys
                .Append(AccountSettingEnums.OasisRaidSafetyFactor).ToList();
            var legacy = AccountsSetting.Where(x => legacyKeys.Contains(x.Setting))
                .AsEnumerable().GroupBy(x => x.AccountId)
                .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Setting, y => y.Value));
            foreach (var farm in FarmLists.AsNoTracking().ToList())
            {
                if (existing.Contains(farm.Id)) continue;
                var accountSettings = legacy.GetValueOrDefault(farm.AccountId);
                var mask = accountSettings is null ? 27 : OasisRaidSettings.LegacyAnimalSettings
                    .Where(x => accountSettings.GetValueOrDefault(x.Key) == 1)
                    .Aggregate(0, (current, entry) => current | entry.Value);
                var factor = accountSettings is not null &&
                    accountSettings.TryGetValue(AccountSettingEnums.OasisRaidSafetyFactor, out var legacyFactor)
                    ? Math.Max(1, legacyFactor) : 2.5m;
                FarmOasisSettings.Add(new FarmOasisSetting
                {
                    FarmId = farm.Id,
                    AllowedAnimals = mask,
                    SafetyFactor = Math.Max(1, factor),
                });
            }
            SaveChanges();
        }

        #endregion table

        #region account setting

        public static readonly ImmutableDictionary<AccountSettingEnums, int> AccountDefaultSettings = new Dictionary<AccountSettingEnums, int>
        {
            {AccountSettingEnums.ClickDelayMin, 500 },
            {AccountSettingEnums.ClickDelayMax, 900 },
            {AccountSettingEnums.TaskDelayMin, 1000 },
            {AccountSettingEnums.TaskDelayMax, 1500 },
            {AccountSettingEnums.EnableAutoLoadVillageBuilding, 1 },
            {AccountSettingEnums.UseStartAllButton, 0 },
            {AccountSettingEnums.FarmIntervalMin, 540 },
            {AccountSettingEnums.FarmIntervalMax, 660 },
            {AccountSettingEnums.Tribe, 0 },
            {AccountSettingEnums.SleepTimeMin, 480 },
            {AccountSettingEnums.SleepTimeMax, 600 },
            {AccountSettingEnums.WorkTimeMin, 600 },
            {AccountSettingEnums.WorkTimeMax, 720 },
            {AccountSettingEnums.HeadlessChrome, 0 },
            {AccountSettingEnums.EnableAutoStartAdventure, 0 },
            {AccountSettingEnums.EnableAutoClaimDailyQuest, 0 },
            {AccountSettingEnums.EnableAutoSetHeroPoint, 0 },
            {AccountSettingEnums.EnableReportNotifications, 0 },
            {AccountSettingEnums.EnableIncomingTroopsNotifications, 0 },
            {AccountSettingEnums.EnableBotStopNotifications, 0 },
            {AccountSettingEnums.EnableAutoDeactivateLostReportTargets, 0 },
            {AccountSettingEnums.EnableScheduledMonitoring, 0 },
            {AccountSettingEnums.EnableSleepNotifications, 0 },
            {AccountSettingEnums.ScheduledMonitoringIntervalMinMinutes, 10 },
            {AccountSettingEnums.ScheduledMonitoringIntervalMaxMinutes, 10 },
            {AccountSettingEnums.ClearTargetsPerPhaseMin, 999999 },
            {AccountSettingEnums.ClearTargetsPerPhaseMax, 999999 },
            {AccountSettingEnums.AllowRaidRatOasis, 0 },
            {AccountSettingEnums.AllowRaidSpiderOasis, 0 },
            {AccountSettingEnums.AllowRaidSnakeOasis, 0 },
            {AccountSettingEnums.AllowRaidBatOasis, 0 },
            {AccountSettingEnums.AllowRaidWildBoarOasis, 0 },
            {AccountSettingEnums.AllowRaidWolfOasis, 0 },
            {AccountSettingEnums.AllowRaidBearOasis, 0 },
            {AccountSettingEnums.AllowRaidCrocodileOasis, 0 },
            {AccountSettingEnums.AllowRaidTigerOasis, 0 },
            {AccountSettingEnums.AllowRaidElephantOasis, 0 },
            {AccountSettingEnums.OasisRaidSafetyFactor, 2 },
        }.ToImmutableDictionary();

        public void FillAccountSettings()
        {
            var existing = AccountsSetting.AsNoTracking()
                .Select(x => new { x.AccountId, x.Setting, x.Value }).AsEnumerable()
                .GroupBy(x => x.AccountId)
                .ToDictionary(x => x.Key, x => x.ToDictionary(y => y.Setting, y => y.Value));
            var ids = Accounts.Select(x => x.Id).ToList();
            var settings = new List<AccountSetting>();
            foreach (var id in ids)
            {
                var accountSettings = existing.GetValueOrDefault(id) ?? new Dictionary<AccountSettingEnums, int>();
                var converted = new Dictionary<AccountSettingEnums, int>(accountSettings);
                MainCore.Services.ScheduledMonitoringSettings.ConvertLegacyIntervals(converted);
                foreach (var (setting, value) in AccountDefaultSettings)
                {
                    if (accountSettings.ContainsKey(setting)) continue;
                    settings.Add(new AccountSetting
                    {
                        AccountId = id,
                        Setting = setting,
                        Value = converted.GetValueOrDefault(setting, value),
                    });
                }
            }

            if (settings.Count > 0)
            {
                AddRange(settings);
                SaveChanges();
            }
        }

        #endregion account setting

        #region village setting

        public static readonly ImmutableDictionary<VillageSettingEnums, int> VillageDefaultSettings = new Dictionary<VillageSettingEnums, int>
        {
            {VillageSettingEnums.UseHeroResourceForBuilding, 0 },
            {VillageSettingEnums.ApplyRomanQueueLogicWhenBuilding, 0 },
            {VillageSettingEnums.UseSpecialUpgrade, 0 },
            {VillageSettingEnums.SpecialUpgradeMinimumMinutes, 10 },
            {VillageSettingEnums.CompleteImmediately, 0 },
            {VillageSettingEnums.Tribe, 0 },
            {VillageSettingEnums.TrainTroopEnable, 0 },
            {VillageSettingEnums.TrainTroopRepeatTimeMin, 120 },
            {VillageSettingEnums.TrainTroopRepeatTimeMax, 180 },
            {VillageSettingEnums.TrainWhenLowResource, 0 },
            {VillageSettingEnums.BarrackTroop, 0 },
            {VillageSettingEnums.BarrackAmountMin, 1 },
            {VillageSettingEnums.BarrackAmountMax, 10 },
            {VillageSettingEnums.StableTroop, 0 },
            {VillageSettingEnums.StableAmountMin, 1 },
            {VillageSettingEnums.StableAmountMax, 10 },
            {VillageSettingEnums.GreatBarrackTroop, 0 },
            {VillageSettingEnums.GreatBarrackAmountMin, 1 },
            {VillageSettingEnums.GreatBarrackAmountMax, 10 },
            {VillageSettingEnums.GreatStableTroop, 0 },
            {VillageSettingEnums.GreatStableAmountMin, 1 },
            {VillageSettingEnums.GreatStableAmountMax, 10 },
            {VillageSettingEnums.WorkshopTroop, 0 },
            {VillageSettingEnums.WorkshopAmountMin, 1 },
            {VillageSettingEnums.WorkshopAmountMax, 10 },

            {VillageSettingEnums.AutoNPCEnable, 0 },
            {VillageSettingEnums.AutoNPCOverflow, 0 },
            {VillageSettingEnums.AutoNPCGranaryPercent, 95 },
            {VillageSettingEnums.AutoNPCWood, 1 },
            {VillageSettingEnums.AutoNPCClay, 1 },
            {VillageSettingEnums.AutoNPCIron, 1 },
            {VillageSettingEnums.AutoNPCCrop, 0 },

            {VillageSettingEnums.AutoRefreshEnable, 0 },
            {VillageSettingEnums.AutoRefreshMin, 45 },
            {VillageSettingEnums.AutoRefreshMax, 75 },

            {VillageSettingEnums.AutoClaimQuestEnable, 0 },
            {VillageSettingEnums.CompleteImmediatelyTime, 20 },
            {VillageSettingEnums.UpgradeTaskDelayMin, 0 },
            {VillageSettingEnums.UpgradeTaskDelayMax, 0 },
        }.ToImmutableDictionary();

        public void FillVillageSettings()
        {
            var existing = VillagesSetting.Select(x => new { x.VillageId, x.Setting })
                .AsEnumerable().ToLookup(x => x.VillageId, x => x.Setting);
            var legacyDelay = AccountsSetting
                .Where(x => x.Setting == AccountSettingEnums.UpgradeTaskDelayMin ||
                            x.Setting == AccountSettingEnums.UpgradeTaskDelayMax)
                .Select(x => new { x.AccountId, x.Setting, x.Value })
                .AsEnumerable().ToDictionary(x => (x.AccountId, x.Setting), x => x.Value);
            var villages = Villages.Select(x => new { x.Id, x.AccountId }).ToList();

            var settings = new List<VillageSetting>();

            foreach (var village in villages)
            {
                var existingKeys = existing[village.Id].ToHashSet();
                foreach (var (setting, value) in VillageDefaultSettings)
                {
                    if (existingKeys.Contains(setting)) continue;
                    var initialValue = setting switch
                    {
                        VillageSettingEnums.UpgradeTaskDelayMin => legacyDelay.GetValueOrDefault((village.AccountId, AccountSettingEnums.UpgradeTaskDelayMin), value),
                        VillageSettingEnums.UpgradeTaskDelayMax => legacyDelay.GetValueOrDefault((village.AccountId, AccountSettingEnums.UpgradeTaskDelayMax), value),
                        _ => value,
                    };
                    settings.Add(new VillageSetting
                    {
                        VillageId = village.Id,
                        Setting = setting,
                        Value = initialValue,
                    });
                }
            }

            if (settings.Count == 0) return;
            AddRange(settings);
            SaveChanges();
        }

        public void FillVillageSettings(AccountId accountId, VillageId villageId)
        {
            var settings = new List<VillageSetting>();

            foreach (var (setting, value) in VillageDefaultSettings)
            {
                if (setting == VillageSettingEnums.Tribe)
                {
                    var tribe = AccountsSetting
                        .Where(x => x.AccountId == accountId.Value)
                        .Where(x => x.Setting == AccountSettingEnums.Tribe)
                        .Select(x => x.Value)
                        .FirstOrDefault();

                    settings.Add(new VillageSetting
                    {
                        VillageId = villageId.Value,
                        Setting = setting,
                        Value = tribe,
                    });
                    continue;
                }
                settings.Add(new VillageSetting
                {
                    VillageId = villageId.Value,
                    Setting = setting,
                    Value = value,
                });
            }

            AddRange(settings);
        }

        #endregion village setting
    }
}
