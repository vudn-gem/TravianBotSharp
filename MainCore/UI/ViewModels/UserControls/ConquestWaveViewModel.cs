using System.Globalization;

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
            Label = index == 11 ? "Hero" : $"t{index}";
        }

        public string Label { get; }

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

        [Reactive]
        private int _catapultTarget1 = 99;

        [Reactive]
        private int? _catapultTarget2;

        [Reactive]
        private bool _hasCatapults;

        [Reactive]
        private bool _canSelectSecondTarget;

        public Result<ConquestWave> BuildWave()
        {
            var amounts = new long[11];
            for (var index = 0; index < amounts.Length; index++)
            {
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
            for (var index = 0; index < Troops.Count; index++)
                copy.Troops[index].Count = Troops[index].Count;
            copy.CatapultTarget1 = CatapultTarget1;
            copy.CatapultTarget2 = CatapultTarget2;
            return copy;
        }
    }
}
