using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class AccountSettingInput : ViewModelBase
    {
        public void Set(Dictionary<AccountSettingEnums, int> settings)
        {
            Tribe.Set((TribeEnums)settings.GetValueOrDefault(AccountSettingEnums.Tribe));
            ClickDelay.Set(settings.GetValueOrDefault(AccountSettingEnums.ClickDelayMin), settings.GetValueOrDefault(AccountSettingEnums.ClickDelayMax));
            TaskDelay.Set(settings.GetValueOrDefault(AccountSettingEnums.TaskDelayMin), settings.GetValueOrDefault(AccountSettingEnums.TaskDelayMax));
            WorkTime.Set(settings.GetValueOrDefault(AccountSettingEnums.WorkTimeMin), settings.GetValueOrDefault(AccountSettingEnums.WorkTimeMax));
            SleepTime.Set(settings.GetValueOrDefault(AccountSettingEnums.SleepTimeMin), settings.GetValueOrDefault(AccountSettingEnums.SleepTimeMax));
            EnableAutoLoadVillage = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoLoadVillageBuilding) == 1;
            HeadlessChrome = settings.GetValueOrDefault(AccountSettingEnums.HeadlessChrome) == 1;
            EnableAutoStartAdventure = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoStartAdventure) == 1;
            EnableAutoClaimDailyQuest = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoClaimDailyQuest) == 1;
            EnableAutoSetHeroPoint = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoSetHeroPoint) == 1;
            EnableReportNotifications = settings.GetValueOrDefault(AccountSettingEnums.EnableReportNotifications) == 1;
            EnableIncomingTroopsNotifications = settings.GetValueOrDefault(AccountSettingEnums.EnableIncomingTroopsNotifications) == 1;
            EnableAutoDeactivateLostReportTargets = settings.GetValueOrDefault(AccountSettingEnums.EnableAutoDeactivateLostReportTargets) == 1;
            AllowRaidRatOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidRatOasis) == 1;
            AllowRaidSpiderOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidSpiderOasis) == 1;
            AllowRaidSnakeOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidSnakeOasis) == 1;
            AllowRaidBatOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidBatOasis) == 1;
            AllowRaidWildBoarOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidWildBoarOasis) == 1;
            AllowRaidWolfOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidWolfOasis) == 1;
            AllowRaidBearOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidBearOasis) == 1;
            AllowRaidCrocodileOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidCrocodileOasis) == 1;
            AllowRaidTigerOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidTigerOasis) == 1;
            AllowRaidElephantOasis = settings.GetValueOrDefault(AccountSettingEnums.AllowRaidElephantOasis) == 1;
            OasisRaidSafetyFactor.Set(settings.GetValueOrDefault(AccountSettingEnums.OasisRaidSafetyFactor, 2));

            FarmInterval.Set(settings.GetValueOrDefault(AccountSettingEnums.FarmIntervalMin), settings.GetValueOrDefault(AccountSettingEnums.FarmIntervalMax));
            UseStartAllButton = settings.GetValueOrDefault(AccountSettingEnums.UseStartAllButton) == 1;
        }

        public Dictionary<AccountSettingEnums, int> Get()
        {
            var tribe = (int)Tribe.Get();
            var (clickDelayMin, clickDelayMax) = ClickDelay.Get();
            var (taskDelayMin, taskDelayMax) = TaskDelay.Get();
            var isAutoLoadVillage = EnableAutoLoadVillage ? 1 : 0;
            var (workTimeMin, workTimeMax) = WorkTime.Get();
            var (sleepTimeMin, sleepTimeMax) = SleepTime.Get();
            var headlessChrome = HeadlessChrome ? 1 : 0;
            var autoStartAdventure = EnableAutoStartAdventure ? 1 : 0;
            var autoClaimDailyQuest = EnableAutoClaimDailyQuest ? 1 : 0;
            var enableAutoSetHeroPoint = EnableAutoSetHeroPoint ? 1 : 0;

            var (farmIntervalMin, farmIntervalMax) = FarmInterval.Get();
            var useStartAllButton = UseStartAllButton ? 1 : 0;

            var settings = new Dictionary<AccountSettingEnums, int>()
            {
                { AccountSettingEnums.ClickDelayMin, clickDelayMin },
                { AccountSettingEnums.ClickDelayMax, clickDelayMax },
                { AccountSettingEnums.TaskDelayMin, taskDelayMin },
                { AccountSettingEnums.TaskDelayMax, taskDelayMax },
                { AccountSettingEnums.EnableAutoLoadVillageBuilding, isAutoLoadVillage },

                { AccountSettingEnums.FarmIntervalMin, farmIntervalMin },
                { AccountSettingEnums.FarmIntervalMax, farmIntervalMax },
                { AccountSettingEnums.UseStartAllButton, useStartAllButton },

                { AccountSettingEnums.Tribe, tribe },
                { AccountSettingEnums.WorkTimeMax, workTimeMax },
                { AccountSettingEnums.WorkTimeMin, workTimeMin },
                { AccountSettingEnums.SleepTimeMax, sleepTimeMax },
                { AccountSettingEnums.SleepTimeMin, sleepTimeMin },

                { AccountSettingEnums.HeadlessChrome, headlessChrome },
                { AccountSettingEnums.EnableAutoStartAdventure, autoStartAdventure },
                { AccountSettingEnums.EnableAutoClaimDailyQuest, autoClaimDailyQuest },
                { AccountSettingEnums.EnableAutoSetHeroPoint, enableAutoSetHeroPoint },
                { AccountSettingEnums.EnableReportNotifications, EnableReportNotifications ? 1 : 0 },
                { AccountSettingEnums.EnableIncomingTroopsNotifications, EnableIncomingTroopsNotifications ? 1 : 0 },
                { AccountSettingEnums.EnableAutoDeactivateLostReportTargets, EnableAutoDeactivateLostReportTargets ? 1 : 0 },
                { AccountSettingEnums.AllowRaidRatOasis, AllowRaidRatOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidSpiderOasis, AllowRaidSpiderOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidSnakeOasis, AllowRaidSnakeOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidBatOasis, AllowRaidBatOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidWildBoarOasis, AllowRaidWildBoarOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidWolfOasis, AllowRaidWolfOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidBearOasis, AllowRaidBearOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidCrocodileOasis, AllowRaidCrocodileOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidTigerOasis, AllowRaidTigerOasis ? 1 : 0 },
                { AccountSettingEnums.AllowRaidElephantOasis, AllowRaidElephantOasis ? 1 : 0 },
                { AccountSettingEnums.OasisRaidSafetyFactor, OasisRaidSafetyFactor.Get() },
            };
            return settings;
        }

        public TribeSelectorViewModel Tribe { get; } = new();
        public RangeInputViewModel ClickDelay { get; } = new();
        public RangeInputViewModel TaskDelay { get; } = new();

        public RangeInputViewModel WorkTime { get; } = new();
        public RangeInputViewModel SleepTime { get; } = new();
        public RangeInputViewModel FarmInterval { get; } = new();
        public AmountInputViewModel OasisRaidSafetyFactor { get; } = new();

        [Reactive]
        private bool _enableAutoLoadVillage;

        [Reactive]
        private bool _headlessChrome;

        [Reactive]
        private bool _enableAutoStartAdventure;

        [Reactive]
        private bool _useStartAllButton;

        [Reactive]
        private bool _enableAutoClaimDailyQuest;

        [Reactive]
        private bool _enableAutoSetHeroPoint;

        [Reactive]
        private bool _enableReportNotifications;

        [Reactive]
        private bool _enableIncomingTroopsNotifications;

        [Reactive]
        private bool _enableAutoDeactivateLostReportTargets;

        [Reactive]
        private bool _allowRaidRatOasis;

        [Reactive]
        private bool _allowRaidSpiderOasis;

        [Reactive]
        private bool _allowRaidSnakeOasis;

        [Reactive]
        private bool _allowRaidBatOasis;

        [Reactive]
        private bool _allowRaidWildBoarOasis;

        [Reactive]
        private bool _allowRaidWolfOasis;

        [Reactive]
        private bool _allowRaidBearOasis;

        [Reactive]
        private bool _allowRaidCrocodileOasis;

        [Reactive]
        private bool _allowRaidTigerOasis;

        [Reactive]
        private bool _allowRaidElephantOasis;
    }
}
