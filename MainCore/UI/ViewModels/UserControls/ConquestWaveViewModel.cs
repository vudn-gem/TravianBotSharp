using System.Globalization;

using Humanizer;

using MainCore.UI.ViewModels.Abstract;

namespace MainCore.UI.ViewModels.UserControls
{
    using Result = FluentResults.Result;

    using ReactiveUI.Primitives;
    using ReactiveUI.Primitives.Signals;

    public partial class ConquestTroopInputViewModel : ViewModelBase
    {
        public ConquestTroopInputViewModel(int index)
        {
            Index = index;
            Label = index == 11 ? "Hero" : $"t{index}";
        }

        public int Index { get; }

        [Reactive]
        private string _label = "";

        public void SetTribe(TribeEnums tribe)
        {
            if (Index == 11)
            {
                Label = "Hero";
                return;
            }

            var firstTroop = tribe switch
            {
                TribeEnums.Romans => TroopEnums.Legionnaire,
                TribeEnums.Teutons => TroopEnums.Clubswinger,
                TribeEnums.Gauls => TroopEnums.Phalanx,
                TribeEnums.Egyptians => TroopEnums.SlaveMilitia,
                TribeEnums.Huns => TroopEnums.Mercenary,
                TribeEnums.Spartan => TroopEnums.Hoplite,
                _ => TroopEnums.None,
            };
            if (firstTroop == TroopEnums.None)
            {
                Label = $"t{Index}";
                return;
            }

            var troop = (TroopEnums)((int)firstTroop + Index - 1);
            var name = troop switch
            {
                TroopEnums.RomanRam or TroopEnums.TeutonRam or TroopEnums.GaulRam or
                    TroopEnums.EgyptianRam or TroopEnums.HunRam or TroopEnums.SpartanRam => "Ram",
                TroopEnums.RomanCatapult => "Fire Catapult",
                TroopEnums.GaulCatapult => "Trebuchet",
                TroopEnums.EgyptianCatapult => "Stone Catapult",
                TroopEnums.TeutonCatapult or TroopEnums.HunCatapult or TroopEnums.SpartanCatapult => "Catapult",
                TroopEnums.RomanChief => "Senator",
                TroopEnums.TeutonChief => "Chief",
                TroopEnums.GaulChief => "Chieftain",
                TroopEnums.EgyptianChief => "Nomarch",
                TroopEnums.HunChief => "Logades",
                TroopEnums.SpartanChief => "Ephor",
                TroopEnums.RomanSettler or TroopEnums.TeutonSettler or TroopEnums.GaulSettler or
                    TroopEnums.EgyptianSettler or TroopEnums.HunSettler or TroopEnums.SpartanSettler => "Settler",
                TroopEnums.Druidrider => "Druidrider",
                _ => troop.Humanize(LetterCasing.Title),
            };
            Label = $"t{Index}: {name}";
        }

        [Reactive]
        private string _count = "0";
    }

    public partial class ConquestWaveViewModel : ViewModelBase
    {
        public ConquestWaveViewModel(int number)
        {
            Number = number;
            Troops = Enumerable.Range(1, 11).Select(index => new ConquestTroopInputViewModel(index)).ToArray();
            Troops[7].WhenAnyValue(troop => troop.Count)
                .Subscribe(text =>
                {
                    var valid = long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count);
                    HasCatapults = valid && count > 0;
                    CanSelectSecondTarget = valid && count >= ConquestWave.SecondTargetMinimumCatapults;
                    if (!CanSelectSecondTarget) CatapultTarget2 = null;
                });
        }

        public int Number { get; }
        public IReadOnlyList<ConquestTroopInputViewModel> Troops { get; }
        private TribeEnums _tribe;

        public void SetTribe(TribeEnums tribe)
        {
            _tribe = tribe;
            foreach (var troop in Troops) troop.SetTribe(tribe);
        }

        [Reactive]
        private int _catapultTarget1 = 99;

        [Reactive]
        private int? _catapultTarget2;

        [Reactive]
        private bool _hasCatapults;

        [Reactive]
        private bool _canSelectSecondTarget;

        public bool IsDraft => Troops.All(troop =>
            string.IsNullOrWhiteSpace(troop.Count) ||
            (long.TryParse(troop.Count, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count == 0));

        public Result<ConquestWave> BuildWave()
        {
            var amounts = new long[11];
            for (var index = 0; index < amounts.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(Troops[index].Count)) continue;
                if (!long.TryParse(Troops[index].Count, NumberStyles.None, CultureInfo.InvariantCulture, out amounts[index]))
                    return Result.Fail($"Wave {Number}: {Troops[index].Label} must be a non-negative whole number.");
            }

            var wave = new ConquestWave(amounts, HasCatapults ? CatapultTarget1 : 99, CatapultTarget2);
            var result = wave.Validate();
            return result.IsFailed
                ? Result.Fail($"Wave {Number}: {string.Join(" ", result.Errors.Select(error => error.Message))}")
                : Result.Ok(wave);
        }

        public ConquestWaveViewModel Copy(int number)
        {
            var copy = new ConquestWaveViewModel(number);
            copy.SetTribe(_tribe);
            for (var index = 0; index < Troops.Count; index++)
                copy.Troops[index].Count = Troops[index].Count;
            copy.CatapultTarget1 = CatapultTarget1;
            copy.CatapultTarget2 = CatapultTarget2;
            return copy;
        }
    }
}
