using MainCore.Infrasturecture.Extensions;
using MainCore.UI.Models.Input;
using MainCore.UI.Models.Output;
using MainCore.UI.ViewModels.Abstract;
using MainCore.UI.ViewModels.UserControls;

using Splat;

namespace MainCore.UI.ViewModels.Tabs
{
    using FluentValidation;

    using ReactiveUI.Primitives;
    using ReactiveUI.Primitives.Signals;

    [RegisterSingleton<FarmingViewModel>]
    public partial class FarmingViewModel : AccountTabViewModelBase
    {
        public AccountSettingInput AccountSettingInput { get; } = new();
        public FarmOasisSettingInput FarmOasisSettingInput { get; } = new();
        public HeroOasisSettingInput HeroOasisSettingInput { get; } = new();
        public ListBoxItemViewModel FarmLists { get; } = new();
        private int? _oasisSettingsFarmId;

        [Reactive]
        private bool _hasOasisSettings;

        [Reactive]
        private bool _hasHeroOasisSettings;

        private readonly DialogService _dialogService;
        private readonly IValidator<AccountSettingInput> _accountsettingInputValidator;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly TaskManager _taskManager;

        private static readonly Dictionary<SplatColor, string> _activeTexts = new()
        {
            { SplatColor.Green , "Deactive" },
            { SplatColor.Red , "Active" },
            { SplatColor.Black , "No farmlist selected" },
        };

        public FarmingViewModel(DialogService dialogService, IValidator<AccountSettingInput> accountsettingInputValidator, TaskManager taskManager, RxQueue rxQueue, IDbContextFactory<AppDbContext> contextFactory)
        {
            _accountsettingInputValidator = accountsettingInputValidator;
            _dialogService = dialogService;
            _taskManager = taskManager;
            _contextFactory = contextFactory;

            LoadFarmListCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(items =>
            {
                FarmLists.Load(items);
                if (items.Count > 0)
                {
                    var color = FarmLists.SelectedItem?.Color ?? SplatColor.Black;
                    ActiveText = _activeTexts[color];
                }
            });
            LoadSettingCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(AccountSettingInput.Set);

            ActiveFarmListCommand
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(x =>
            {
                var color = FarmLists.SelectedItem?.Color ?? SplatColor.Black;
                ActiveText = _activeTexts[color];
            });

            this.WhenAnyValue(x => x.FarmLists.SelectedItem)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(selectedItem =>
                {
                    _oasisSettingsFarmId = null;
                    HasOasisSettings = false;
                    HasHeroOasisSettings = false;
                    ActiveText = _activeTexts[selectedItem?.Color ?? SplatColor.Black];
                    if (selectedItem is null) return;
                    using var context = _contextFactory.CreateDbContext();
                    var heroSetting = context.HeroOasisSettings.AsNoTracking().SingleOrDefault(x =>
                        x.FarmId == selectedItem.Id &&
                        context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == AccountId.Value));
                    if (heroSetting is not null)
                    {
                        HeroOasisSettingInput.Set(heroSetting);
                        HasHeroOasisSettings = true;
                    }
                    var setting = context.FarmOasisSettings.AsNoTracking().SingleOrDefault(x =>
                        x.FarmId == selectedItem.Id &&
                        context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == AccountId.Value));
                    if (setting is null)
                    {
                        _ = _dialogService.SendMessage("Warning", "Oasis settings are missing for the selected farm list.");
                        return;
                    }
                    FarmOasisSettingInput.Set(setting);
                    _oasisSettingsFarmId = selectedItem.Id;
                    HasOasisSettings = true;
                });

            rxQueue.GetObservable<FarmsModified>()
                .InvokeCommand(FarmsModifiedCommand);

            FarmsModifiedCommand
                .Where(x => x)
                .Select(_ => AccountId)
                .Throttle(TimeSpan.FromMilliseconds(1000), RxSchedulers.TaskpoolScheduler)
                .ObserveOn(RxSchedulers.TaskpoolScheduler)
                .InvokeCommand(LoadFarmListCommand);
        }

        [ReactiveCommand(RunInBackground = true)]
        public bool FarmsModified(FarmsModified notification)
        {
            if (!IsActive) return false;
            if (notification.AccountId != AccountId) return false;
            return true;
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadFarmListCommand.Execute(accountId);
            await LoadSettingCommand.Execute(accountId);
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task UpdateFarmList()
        {
            _taskManager.AddOrUpdate<UpdateFarmListTask.Task>(new(AccountId));
            await _dialogService.SendMessage("Information", "Added update farm list task");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task CheckTargetTroops()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null)
            {
                await _dialogService.SendMessage("Warning", "No farm list selected");
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            if (!context.FarmLists.Any(x => x.Id == selected.Id && x.AccountId == AccountId.Value))
            {
                await _dialogService.SendMessage("Warning", "Selected farm list no longer belongs to this account");
                return;
            }
            _taskManager.AddOrUpdate<CheckFarmTargetTroopsTask.Task>(new(AccountId, new FarmId(selected.Id)));
            await _dialogService.SendMessage("Information", "Added check oasis task. Targets with oasis troops will be deactivated.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task ClearOasis()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null)
            {
                await _dialogService.SendMessage("Warning", "No farm list selected");
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            if (!context.FarmLists.Any(x => x.Id == selected.Id && x.AccountId == AccountId.Value))
            {
                await _dialogService.SendMessage("Warning", "Selected farm list no longer belongs to this account");
                return;
            }
            _taskManager.AddOrUpdate<ClearFarmOasisTask.Task>(new(AccountId, new FarmId(selected.Id)));
            await _dialogService.SendMessage("Information", "Added clear oasis task using the selected farm list's saved troop settings.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task RemoveClearOasis()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null)
            {
                await _dialogService.SendMessage("Warning", "No farm list selected");
                return;
            }

            var result = _taskManager.RemoveClearOasis(AccountId, new FarmId(selected.Id));
            switch (result)
            {
                case ClearOasisRemovalResult.Removed:
                    await _dialogService.SendMessage("Information", "Removed clear oasis task for the selected farm list.");
                    break;
                case ClearOasisRemovalResult.Executing:
                    await _dialogService.SendMessage("Warning", "Clear oasis is running for the selected farm list. Wait until it finishes before removing its task.");
                    break;
                case ClearOasisRemovalResult.NotFound:
                    await _dialogService.SendMessage("Information", "No clear oasis task found for the selected farm list.");
                    break;
            }
        }

        [ReactiveCommand]
        private async Task SaveHeroOasisSettings()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null || !HasHeroOasisSettings)
            {
                await _dialogService.SendMessage("Warning", "Select a farm list with loaded hero oasis settings.");
                return;
            }
            var setting = HeroOasisSettingInput.Get(selected.Id);
            var valid = HeroOasisSettings.Validate(setting);
            if (valid.IsFailed)
            {
                await _dialogService.SendMessage("Warning", string.Join(Environment.NewLine, valid.Errors.Select(x => x.Message)));
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            var existing = context.HeroOasisSettings.SingleOrDefault(x => x.FarmId == selected.Id &&
                context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == AccountId.Value));
            if (existing is null)
            {
                await _dialogService.SendMessage("Warning", "Selected farm list or hero settings no longer exist.");
                return;
            }
            context.Entry(existing).CurrentValues.SetValues(setting);
            context.SaveChanges();
            await _dialogService.SendMessage("Information", "Hero oasis settings saved.");
        }

        [ReactiveCommand]
        private async Task HeroClearOasis()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null || !HasHeroOasisSettings)
            {
                await _dialogService.SendMessage("Warning", "Select a farm list with loaded hero oasis settings.");
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            var saved = context.HeroOasisSettings.AsNoTracking().SingleOrDefault(x => x.FarmId == selected.Id &&
                context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == AccountId.Value));
            if (saved is null)
            {
                await _dialogService.SendMessage("Warning", "Selected farm list or hero settings no longer exist.");
                return;
            }
            var valid = HeroOasisSettings.Validate(saved);
            if (valid.IsFailed)
            {
                await _dialogService.SendMessage("Warning", string.Join(Environment.NewLine, valid.Errors.Select(x => x.Message)));
                return;
            }
            var added = _taskManager.TryAddHeroClearOasis(AccountId, new FarmId(selected.Id));
            await _dialogService.SendMessage(added ? "Information" : "Warning", added
                ? "Added hero clear oasis task using saved settings. Adventures will wait until it finishes."
                : "A hero clear oasis task already exists for this account, or the farm list no longer exists.");
        }

        [ReactiveCommand]
        private async Task RemoveHeroClearOasis()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null)
            {
                await _dialogService.SendMessage("Warning", "No farm list selected.");
                return;
            }
            var result = _taskManager.RemoveHeroClearOasis(AccountId, new FarmId(selected.Id));
            await _dialogService.SendMessage(result == ClearOasisRemovalResult.Executing ? "Warning" : "Information",
                result switch
                {
                    ClearOasisRemovalResult.Removed => "Removed hero clear oasis task. Any raid already sent continues in the game.",
                    ClearOasisRemovalResult.Executing => "Hero clear oasis is running. Pause it before removing its task.",
                    _ => "No hero clear oasis task found for the selected farm list.",
                });
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Start()
        {
            using var context = _contextFactory.CreateDbContext();

            var useStartAllButton = context.BooleanByName(AccountId, AccountSettingEnums.UseStartAllButton);
            if (!useStartAllButton)
            {
                var count = context.FarmLists
                    .Where(x => x.AccountId == AccountId.Value)
                    .Count(x => x.IsActive);

                if (count == 0)
                {
                    await _dialogService.SendMessage("Information", "There is no active farm or use start all button is disable");
                    return;
                }
            }
            _taskManager.AddOrUpdate<StartFarmListTask.Task>(new(AccountId));
            await _dialogService.SendMessage("Information", "Added start farm list task");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Stop()
        {
            _taskManager.Remove<StartFarmListTask.Task>(AccountId);
            await _dialogService.SendMessage("Information", "Removed start farm list task");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Save()
        {
            var result = await _accountsettingInputValidator.ValidateAsync(AccountSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, result.Errors.Select(x => x.ErrorMessage)));
                return;
            }

            using var context = _contextFactory.CreateDbContext();
            var settings = AccountSettingInput.Get();
            context.SaveAccountSetting(AccountId, settings);
            await _dialogService.SendMessage("Information", "Saved");
        }

        [ReactiveCommand]
        private async Task SaveOasisSettings()
        {
            var selected = FarmLists.SelectedItem;
            if (selected is null || _oasisSettingsFarmId != selected.Id)
            {
                await _dialogService.SendMessage("Warning", "Select a farm list with loaded oasis settings.");
                return;
            }
            var settings = FarmOasisSettingInput.Get(selected.Id);
            if (settings.SafetyFactor < 1)
            {
                await _dialogService.SendMessage("Warning", "Oasis raid safety factor must be at least 1.");
                return;
            }
            if (!Enum.IsDefined(settings.AttackTroop))
            {
                await _dialogService.SendMessage("Warning", "Select an attack troop.");
                return;
            }
            if (settings.LimitAnimal < 0)
            {
                await _dialogService.SendMessage("Warning", "Animal limit must be zero or greater.");
                return;
            }
            if (!OasisRaidSettings.IsValidRaidComposition(settings.RaidSteppeRiders, settings.RaidMarksmen, settings.RaidMarauders))
            {
                await _dialogService.SendMessage("Warning", "Raid troop counts must be between 0 and 999999, with at least one troop.");
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            var updated = context.FarmOasisSettings
                .Where(x => x.FarmId == settings.FarmId &&
                    context.FarmLists.Any(f => f.Id == x.FarmId && f.AccountId == AccountId.Value))
                .ExecuteUpdate(x => x
                    .SetProperty(y => y.AllowedAnimals, settings.AllowedAnimals)
                    .SetProperty(y => y.SafetyFactor, settings.SafetyFactor)
                    .SetProperty(y => y.AttackTroop, settings.AttackTroop)
                    .SetProperty(y => y.RaidSteppeRiders, settings.RaidSteppeRiders)
                    .SetProperty(y => y.RaidMarksmen, settings.RaidMarksmen)
                    .SetProperty(y => y.RaidMarauders, settings.RaidMarauders)
                    .SetProperty(y => y.LimitAnimal, settings.LimitAnimal));
            if (updated != 1)
            {
                await _dialogService.SendMessage("Warning", "Selected farm list or its oasis settings no longer exist.");
                return;
            }
            await _dialogService.SendMessage("Information", "Farm list oasis settings saved.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task ActiveFarmList()
        {
            if (FarmLists.SelectedItem is null)
            {
                await _dialogService.SendMessage("Warning", "No farm list selected");
                return;
            }

            var selectedFarmList = FarmLists.SelectedItem;
            if (selectedFarmList is null) return;

            using var context = _contextFactory.CreateDbContext();
            context.FarmLists
               .Where(x => x.Id == selectedFarmList.Id)
               .ExecuteUpdate(x => x.SetProperty(x => x.IsActive, x => !x.IsActive));

            await FarmsModifiedCommand.Execute(new FarmsModified(AccountId));
            await _dialogService.SendMessage("Information", "Activated farm list");
        }

        [ReactiveCommand(RunInBackground = true)]
        private Dictionary<AccountSettingEnums, int> LoadSetting(AccountId accountId)
        {
            using var context = _contextFactory.CreateDbContext();
            var settings = context.AccountsSetting
              .Where(x => x.AccountId == AccountId.Value)
              .ToDictionary(x => x.Setting, x => x.Value);
            return settings;
        }

        [ReactiveCommand(RunInBackground = true)]
        private List<ListBoxItem> LoadFarmList(AccountId accountId)
        {
            using var context = _contextFactory.CreateDbContext();
            var items = context.FarmLists
                 .Where(x => x.AccountId == accountId.Value)
                 .Select(x => new ListBoxItem()
                 {
                     Id = x.Id,
                     Color = x.IsActive ? SplatColor.Green : SplatColor.Red,
                     Content = x.Name,
                 })
                 .ToList();
            return items;
        }

        [Reactive]
        private string _activeText = "No farmlist selected";
    }
}
