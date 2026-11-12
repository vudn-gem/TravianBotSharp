using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class FarmOasisSettingInput : ViewModelBase
    {
        [Reactive] private bool _allowRat;
        [Reactive] private bool _allowSpider;
        [Reactive] private bool _allowSnake;
        [Reactive] private bool _allowBat;
        [Reactive] private bool _allowWildBoar;
        [Reactive] private bool _allowWolf;
        [Reactive] private bool _allowBear;
        [Reactive] private bool _allowCrocodile;
        [Reactive] private bool _allowTiger;
        [Reactive] private bool _allowElephant;
        public AmountInputViewModel SafetyFactor { get; } = new();
        [Reactive] private int _attackTroopIndex;
        public AmountInputViewModel RaidSteppeRiders { get; } = new();
        public AmountInputViewModel RaidMarksmen { get; } = new();
        public AmountInputViewModel RaidMarauders { get; } = new();
        public AmountInputViewModel LimitAnimal { get; } = new();

        public void Set(FarmOasisSetting setting)
        {
            var mask = setting.AllowedAnimals;
            AllowRat = (mask & (1 << 0)) != 0;
            AllowSpider = (mask & (1 << 1)) != 0;
            AllowSnake = (mask & (1 << 2)) != 0;
            AllowBat = (mask & (1 << 3)) != 0;
            AllowWildBoar = (mask & (1 << 4)) != 0;
            AllowWolf = (mask & (1 << 5)) != 0;
            AllowBear = (mask & (1 << 6)) != 0;
            AllowCrocodile = (mask & (1 << 7)) != 0;
            AllowTiger = (mask & (1 << 8)) != 0;
            AllowElephant = (mask & (1 << 9)) != 0;
            SafetyFactor.Set(setting.SafetyFactor);
            AttackTroopIndex = (int)setting.AttackTroop;
            RaidSteppeRiders.Set(setting.RaidSteppeRiders);
            RaidMarksmen.Set(setting.RaidMarksmen);
            RaidMarauders.Set(setting.RaidMarauders);
            LimitAnimal.Set(setting.LimitAnimal);
        }

        public FarmOasisSetting Get(int farmId) => new()
        {
            FarmId = farmId,
            AllowedAnimals = (AllowRat ? 1 << 0 : 0) | (AllowSpider ? 1 << 1 : 0) |
                (AllowSnake ? 1 << 2 : 0) | (AllowBat ? 1 << 3 : 0) |
                (AllowWildBoar ? 1 << 4 : 0) | (AllowWolf ? 1 << 5 : 0) |
                (AllowBear ? 1 << 6 : 0) | (AllowCrocodile ? 1 << 7 : 0) |
                (AllowTiger ? 1 << 8 : 0) | (AllowElephant ? 1 << 9 : 0),
            SafetyFactor = SafetyFactor.Get(),
            AttackTroop = (OasisAttackTroop)AttackTroopIndex,
            RaidSteppeRiders = RaidSteppeRiders.Get(),
            RaidMarksmen = RaidMarksmen.Get(),
            RaidMarauders = RaidMarauders.Get(),
            LimitAnimal = LimitAnimal.Get(),
        };
    }
}
