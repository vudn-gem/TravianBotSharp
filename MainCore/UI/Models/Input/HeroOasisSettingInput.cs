using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class HeroOasisSettingInput : ViewModelBase
    {
        public FarmOasisSettingInput Animals { get; } = new();
        public AmountInputViewModel MinAnimal { get; } = new();
        public AmountInputViewModel SafeAnimal { get; } = new();
        public AmountInputViewModel MaxAnimal { get; } = new();
        public AmountInputViewModel SteppeRiders { get; } = new();
        public AmountInputViewModel Marksmen { get; } = new();
        public AmountInputViewModel Marauders { get; } = new();
        [Reactive] private bool _useTroop;
        public AmountInputViewModel MinHeroHealth { get; } = new();
        public AmountInputViewModel MaxHeroHealthLost { get; } = new();
        public AmountInputViewModel MaxSubTroopLost { get; } = new();

        public void Set(HeroOasisSetting setting)
        {
            Animals.Set(new FarmOasisSetting { AllowedAnimals = setting.AllowedAnimals });
            MinAnimal.Set(setting.MinAnimal);
            SafeAnimal.Set(setting.SafeAnimal);
            MaxAnimal.Set(setting.MaxAnimal);
            SteppeRiders.Set(setting.SteppeRiders);
            Marksmen.Set(setting.Marksmen);
            Marauders.Set(setting.Marauders);
            UseTroop = setting.UseTroop;
            MinHeroHealth.Set(setting.MinHeroHealth);
            MaxHeroHealthLost.Set(setting.MaxHeroHealthLost);
            MaxSubTroopLost.Set(setting.MaxSubTroopLost);
        }

        public HeroOasisSetting Get(int farmId) => new()
        {
            FarmId = farmId,
            AllowedAnimals = Animals.Get(farmId).AllowedAnimals,
            MinAnimal = MinAnimal.Get(),
            SafeAnimal = SafeAnimal.Get(),
            MaxAnimal = MaxAnimal.Get(),
            SteppeRiders = SteppeRiders.Get(),
            Marksmen = Marksmen.Get(),
            Marauders = Marauders.Get(),
            UseTroop = UseTroop,
            MinHeroHealth = MinHeroHealth.Get(),
            MaxHeroHealthLost = MaxHeroHealthLost.Get(),
            MaxSubTroopLost = MaxSubTroopLost.Get(),
        };
    }
}
