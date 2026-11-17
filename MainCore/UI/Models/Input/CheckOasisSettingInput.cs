using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.Models.Input
{
    public partial class CheckOasisSettingInput : ViewModelBase
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
        public AmountInputViewModel SafeAnimal { get; } = new();

        public void Set(FarmOasisSetting setting)
        {
            var mask = setting.CheckAllowedAnimals;
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
            SafeAnimal.Set(setting.SafeAnimal);
        }

        public int AllowedAnimals =>
            (AllowRat ? 1 << 0 : 0) | (AllowSpider ? 1 << 1 : 0) |
            (AllowSnake ? 1 << 2 : 0) | (AllowBat ? 1 << 3 : 0) |
            (AllowWildBoar ? 1 << 4 : 0) | (AllowWolf ? 1 << 5 : 0) |
            (AllowBear ? 1 << 6 : 0) | (AllowCrocodile ? 1 << 7 : 0) |
            (AllowTiger ? 1 << 8 : 0) | (AllowElephant ? 1 << 9 : 0);
    }
}
