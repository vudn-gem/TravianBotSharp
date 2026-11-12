using System.Globalization;

using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

namespace MainCore.UI.ViewModels.Tabs
{
    using ReactiveUI.Primitives;
    using ReactiveUI.Primitives.Signals;

    [RegisterSingleton<ConquestViewModel>]
    public partial class ConquestViewModel : AccountTabViewModelBase
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly TaskManager _taskManager;
        private readonly DialogService _dialogService;

        public ListBoxItemViewModel Villages { get; } = new();

        [Reactive]
        private int _originX;

        [Reactive]
        private int _originY;

        [Reactive]
        private int _tournamentSquareLevel;

        [Reactive]
        private string _tournamentSquareText = "Tournament Square level: 0";

        [Reactive]
        private string _originCoordinates = "";

        [Reactive]
        private string _targetX = "";

        [Reactive]
        private string _targetY = "";

        [Reactive]
        private string _troopWaves = "";

        [Reactive]
        private string _slowestTroopSpeed = "6";

        [Reactive]
        private string _bootsPercent = "0";

        [Reactive]
        private string _artifactMultiplier = "1";

        [Reactive]
        private string _delaySeconds = "0.4";

        [Reactive]
        private DateTime? _arrivalDate = DateTime.Today.AddDays(1);

        [Reactive]
        private string _arrivalTime = "13:00:00";

        [Reactive]
        private int _catapultTargetIndex;

        [Reactive]
        private string _planPreview = "Choose a village and enter target coordinates to calculate the plan.";

        public ConquestViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            TaskManager taskManager,
            DialogService dialogService,
            RxQueue rxQueue)
        {
            _contextFactory = contextFactory;
            _taskManager = taskManager;
            _dialogService = dialogService;

            LoadVillagesCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(Villages.Load);

            LoadVillageDetailCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(detail =>
                {
                    if (detail is null)
                    {
                        OriginCoordinates = "";
                        OriginX = 0;
                        OriginY = 0;
                        TournamentSquareLevel = 0;
                        TournamentSquareText = "Tournament Square level: 0";
                        return;
                    }

                    OriginCoordinates = $"({detail.X}|{detail.Y})";
                    OriginX = detail.X;
                    OriginY = detail.Y;
                    TournamentSquareLevel = detail.TournamentSquareLevel;
                    TournamentSquareText = $"Tournament Square level: {detail.TournamentSquareLevel}";
                });

            this.WhenAnyValue(vm => vm.Villages.SelectedItem)
                .Select(item => item?.Id ?? 0)
                .InvokeCommand(LoadVillageDetailCommand);

            rxQueue.GetObservable<VillagesModified>()
                .InvokeCommand(VillagesModifiedCommand);

            VillagesModifiedCommand
                .Where(changed => changed)
                .Select(_ => AccountId)
                .Throttle(TimeSpan.FromMilliseconds(1000), RxSchedulers.TaskpoolScheduler)
                .ObserveOn(RxSchedulers.TaskpoolScheduler)
                .InvokeCommand(LoadVillagesCommand);
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadVillagesCommand.Execute(accountId);
        }

        protected override Task OnAccountContextInvalidated()
        {
            Villages.Load([]);
            OriginCoordinates = "";
            OriginX = 0;
            OriginY = 0;
            TournamentSquareLevel = 0;
            TournamentSquareText = "Tournament Square level: 0";
            return Task.CompletedTask;
        }

        [ReactiveCommand(RunInBackground = true)]
        public bool VillagesModified(VillagesModified notification) =>
            IsActive && notification.AccountId == AccountId;

        [ReactiveCommand(RunInBackground = true)]
        private List<ListBoxItem> LoadVillages(AccountId accountId)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Villages
                .Where(village => village.AccountId == accountId.Value)
                .OrderBy(village => village.Name)
                .Select(village => new ListBoxItem
                {
                    Id = village.Id,
                    Content = $"{village.Name} ({village.X}|{village.Y})",
                })
                .ToList();
        }

        [ReactiveCommand(RunInBackground = true)]
        private VillageDetail? LoadVillageDetail(int villageId)
        {
            if (villageId <= 0 || AccountId == AccountId.Empty) return null;

            using var context = _contextFactory.CreateDbContext();
            return context.Villages
                .Where(village => village.Id == villageId && village.AccountId == AccountId.Value)
                .Select(village => new VillageDetail(
                    village.X,
                    village.Y,
                    village.Buildings
                        .Where(building => building.Type == BuildingEnums.TournamentSquare)
                        .Select(building => building.Level)
                        .DefaultIfEmpty(0)
                        .Max()))
                .SingleOrDefault();
        }

        [ReactiveCommand]
        private async Task CalculatePlan()
        {
            var result = TryBuildPlan();
            if (result.IsFailed)
            {
                PlanPreview = string.Join(Environment.NewLine, result.Errors.Select(error => error.Message));
                await _dialogService.SendMessage("Warning", PlanPreview);
                return;
            }

            var plan = result.Value;
            PlanPreview =
                $"Distance: {plan.Calculation.Distance:F2} fields | Travel: {plan.Calculation.TravelTime:hh\\:mm\\:ss}" +
                $"{Environment.NewLine}Send: {plan.Calculation.SendAt:yyyy-MM-dd HH:mm:ss}" +
                $"{Environment.NewLine}Arrive: {plan.ArrivalAt:yyyy-MM-dd HH:mm:ss} | Waves: {plan.Waves.Count}";
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Action()
        {
            var result = TryBuildPlan();
            if (result.IsFailed)
            {
                await _dialogService.SendMessage("Warning", string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
                return;
            }

            var plan = result.Value;
            var added = _taskManager.TryAdd(new ConquestTask.Task(
                AccountId,
                plan.VillageId,
                plan.TargetX,
                plan.TargetY,
                plan.TroopWaves,
                plan.CatapultTarget,
                plan.ArrivalAt,
                plan.Calculation.SendAt,
                plan.DelaySeconds));

            if (!added)
            {
                await _dialogService.SendMessage("Warning", "This identical conquest plan is already in the task list.");
                return;
            }

            await _dialogService.SendMessage(
                "Information",
                $"Added conquest task. Scheduled to prepare at {(DateTime.Now > plan.Calculation.SendAt.AddMinutes(-2) ? DateTime.Now : plan.Calculation.SendAt.AddMinutes(-2)):yyyy-MM-dd HH:mm:ss}.");
        }

        private Result<PlannedConquest> TryBuildPlan()
        {
            if (AccountId == AccountId.Empty)
                return Result.Fail("Select an account.");
            if (Villages.SelectedItem is null)
                return Result.Fail("Select an origin village.");
            if (!TryParseCoordinate(TargetX, out var targetX) || !TryParseCoordinate(TargetY, out var targetY))
                return Result.Fail("Target coordinates must be whole numbers between -200 and 200.");
            if (!double.TryParse(SlowestTroopSpeed, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) ||
                !double.TryParse(BootsPercent, NumberStyles.Float, CultureInfo.InvariantCulture, out var boots) ||
                !double.TryParse(ArtifactMultiplier, NumberStyles.Float, CultureInfo.InvariantCulture, out var artifact) ||
                !double.TryParse(DelaySeconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay))
                return Result.Fail("Speed, boots, artifact, and delay values must be valid numbers (use '.' for decimals).");
            if (CatapultTargetIndex is < 0 or > 4)
                return Result.Fail("Select a catapult target.");
            if (ArrivalDate is null ||
                !TimeOnly.TryParseExact(ArrivalTime, ["H:mm:ss", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                return Result.Fail("Choose an arrival date and enter the time as HH:mm:ss.");

            var waves = ConquestTask.ParseTroopWaves(TroopWaves);
            if (waves.IsFailed) return Result.Fail(waves.Errors);
            if (waves.Value.Count > 8)
                return Result.Fail("Wave Builder supports at most 8 waves.");

            using var context = _contextFactory.CreateDbContext();
            var village = context.Villages
                .Where(item => item.Id == Villages.SelectedItem.Id && item.AccountId == AccountId.Value)
                .Select(item => new VillageDetail(
                    item.X,
                    item.Y,
                    item.Buildings
                        .Where(building => building.Type == BuildingEnums.TournamentSquare)
                        .Select(building => building.Level)
                        .DefaultIfEmpty(0)
                        .Max()))
                .SingleOrDefault();
            if (village is null)
                return Result.Fail("Selected village no longer belongs to this account.");

            var arrivalAt = ArrivalDate.Value.Date.Add(time.ToTimeSpan());
            if (arrivalAt <= DateTime.Now)
                return Result.Fail("Arrival time must be in the future.");

            var calculation = ConquestPlanner.Calculate(
                village.X,
                village.Y,
                targetX,
                targetY,
                speed,
                village.TournamentSquareLevel,
                boots,
                artifact,
                delay,
                arrivalAt);
            if (calculation.IsFailed) return Result.Fail(calculation.Errors);

            return Result.Ok(new PlannedConquest(
                new VillageId(Villages.SelectedItem.Id),
                targetX,
                targetY,
                TroopWaves,
                CatapultTargetIndex + 1,
                arrivalAt,
                calculation.Value,
                delay,
                waves.Value));
        }

        private static bool TryParseCoordinate(string input, out int coordinate) =>
            int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out coordinate) &&
            coordinate is >= -200 and <= 200;

        private sealed record VillageDetail(int X, int Y, int TournamentSquareLevel);

        private sealed record PlannedConquest(
            VillageId VillageId,
            int TargetX,
            int TargetY,
            string TroopWaves,
            int CatapultTarget,
            DateTime ArrivalAt,
            ConquestPlanCalculation Calculation,
            double DelaySeconds,
            IReadOnlyList<IReadOnlyList<long>> Waves);
    }
}
