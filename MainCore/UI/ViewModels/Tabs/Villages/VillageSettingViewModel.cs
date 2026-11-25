using System.Text.Json;
using System.Collections.ObjectModel;

using MainCore.Infrasturecture.Extensions;
using MainCore.UI.Models.Input;
using MainCore.UI.ViewModels.Abstract;

namespace MainCore.UI.ViewModels.Tabs.Villages
{
    using FluentValidation;

    using ReactiveUI.Primitives;
    using ReactiveUI.Primitives.Signals;

    [RegisterSingleton<VillageSettingViewModel>]
    public partial class VillageSettingViewModel : VillageTabViewModelBase
    {
        public VillageSettingInput VillageSettingInput { get; } = new();
        public ObservableCollection<EvasionDestination> EvasionDestinations { get; } = [];

        private readonly DialogService _dialogService;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IValidator<VillageSettingInput> _villageSettingInputValidator;
        private readonly TaskManager _taskManager;

        public VillageSettingViewModel(DialogService dialogService, IValidator<VillageSettingInput> villageSettingInputValidator, CustomServiceScopeFactory serviceScopeFactory, IDbContextFactory<AppDbContext> contextFactory, TaskManager taskManager)
        {
            _dialogService = dialogService;
            _villageSettingInputValidator = villageSettingInputValidator;
            _contextFactory = contextFactory;
            _taskManager = taskManager;

            LoadSettingCommand.ObserveOn(RxSchedulers.MainThreadScheduler).Subscribe(VillageSettingInput.Set);
        }

        public async Task SettingRefresh(VillageId villageId)
        {
            if (!IsActive) return;
            if (villageId != VillageId) return;
            await LoadSettingCommand.Execute(villageId);
        }

        protected override async Task Load(AccountId accountId, VillageId villageId)
        {
            using var context = _contextFactory.CreateDbContext();
            var destinations = context.Villages.AsNoTracking()
                .Where(x => x.AccountId == accountId.Value && x.Id != villageId.Value)
                .OrderBy(x => x.Name)
                .Select(x => new EvasionDestination(x.Id, x.Name, x.X, x.Y)).ToList();
            await Signal.Start(() =>
            {
                EvasionDestinations.Clear();
                EvasionDestinations.Add(new(0, "None", 0, 0));
                foreach (var destination in destinations) EvasionDestinations.Add(destination);
            }, RxSchedulers.MainThreadScheduler);
            await LoadSettingCommand.Execute(villageId);
        }

        protected override async Task OnContextInvalidated(AccountId accountId)
        {
            await Signal.Start(() =>
            {
                EvasionDestinations.Clear();
                VillageSettingInput.Set([]);
            }, RxSchedulers.MainThreadScheduler);
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task TestEvasion()
        {
            var accountId = AccountId;
            var villageId = VillageId;
            using var context = _contextFactory.CreateDbContext();
            var saved = EvasionSettings.Read(context, accountId, villageId);
            if (saved.IsFailed || !saved.Value.Enabled)
            {
                await _dialogService.SendMessage("Warning", saved.IsFailed
                    ? string.Join(Environment.NewLine, saved.Errors.Select(x => x.Message))
                    : "Save settings with evasion enabled before testing.");
                return;
            }
            var settings = saved.Value;
            if (settings.TrainTroop == TroopEnums.None && !settings.EvadeTroops)
            {
                await _dialogService.SendMessage("Warning", "Select a troop to train or enable troop evasion, then save settings.");
                return;
            }
            var confirmed = await _dialogService.SendConfirm("LIVE evasion test",
                $"Village {villageId.Value}. Uses SAVED settings, without an incoming attack.\n" +
                $"Train maximum: {settings.TrainTroop}.\n" +
                (settings.EvadeTroops
                    ? $"Send ALL available troops and Hero by REAL Raid to ({settings.X}|{settings.Y}). Troops may die; no automatic recall.\n"
                    : "Troop sending is disabled.\n") +
                "Resource transfer is not implemented. The task runs when the account scheduler is available.\nContinue?");
            if (!confirmed) return;
            var latest = EvasionSettings.Read(context, accountId, villageId);
            if (latest.IsFailed || latest.Value != settings)
            {
                await _dialogService.SendMessage("Warning", "Saved evasion settings changed; test was not queued. Review settings and try again.");
                return;
            }
            var incoming = new EvasionIncoming(-DateTime.UtcNow.Ticks, false, DateTime.MaxValue);
            var task = new EvasionReadinessTask.Task(accountId, villageId, incoming, settings, manualTest: true);
            if (!_taskManager.TryAdd(task))
            {
                await _dialogService.SendMessage("Warning", "An evasion test is already queued for this village.");
                return;
            }
            await _dialogService.SendMessage("Information", "LIVE evasion test queued. Keep the account running; check the task list and logs for the result.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Save()
        {
            var result = await _villageSettingInputValidator.ValidateAsync(VillageSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, result.Errors.Select(x => x.ErrorMessage)));
                return;
            }

            using var context = _contextFactory.CreateDbContext();
            var settings = VillageSettingInput.Get();
            var evasion = EvasionSettings.Read(context, AccountId, VillageId, settings);
            if (evasion.IsFailed)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, evasion.Errors.Select(x => x.Message)));
                return;
            }
            context.SaveVillageSetting(VillageId, settings);
            context.TriggerTask(_taskManager, AccountId, VillageId, settings);

            await _dialogService.SendMessage("Information", "Settings saved.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Import()
        {
            var path = await _dialogService.OpenFileDialog();
            Dictionary<VillageSettingEnums, int> settings;
            try
            {
                var jsonString = await File.ReadAllTextAsync(path);
                settings = JsonSerializer.Deserialize<Dictionary<VillageSettingEnums, int>>(jsonString)
                    ?? throw new JsonException("Village settings must be an object.");
            }
            catch
            {
                await _dialogService.SendMessage("Warning", "Invalid file.");
                return;
            }

            settings.TryAdd(VillageSettingEnums.UpgradeTaskDelayMin, 0);
            settings.TryAdd(VillageSettingEnums.UpgradeTaskDelayMax, 0);
            settings.TryAdd(VillageSettingEnums.EvasionEnable, 0);
            settings.TryAdd(VillageSettingEnums.EvasionTrainTroop, 0);
            settings.TryAdd(VillageSettingEnums.EvasionResourceVillage, 0);
            settings.TryAdd(VillageSettingEnums.EvasionTroopEnable, 0);
            settings.TryAdd(VillageSettingEnums.EvasionX, 0);
            settings.TryAdd(VillageSettingEnums.EvasionY, 0);
            settings.TryAdd(VillageSettingEnums.EvasionLeadSeconds, 120);
            using var context = _contextFactory.CreateDbContext();
            // Tribe is local to the village and is never imported.
            var localTribe = context.ByName(VillageId, VillageSettingEnums.Tribe);
            settings[VillageSettingEnums.Tribe] = localTribe;
            var evasion = EvasionSettings.Read(context, AccountId, VillageId, settings);
            if (evasion.IsFailed)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, evasion.Errors.Select(x => x.Message)));
                return;
            }
            await Signal.Start(() => VillageSettingInput.Set(settings), RxSchedulers.MainThreadScheduler);
            var result = await _villageSettingInputValidator.ValidateAsync(VillageSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, result.Errors.Select(x => x.ErrorMessage)));
                return;
            }
            settings.Remove(VillageSettingEnums.Tribe);

            context.SaveVillageSetting(VillageId, settings);
            context.TriggerTask(_taskManager, AccountId, VillageId, settings);

            await _dialogService.SendMessage("Information", "Settings imported");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Export()
        {
            var path = await _dialogService.SaveFileDialog();
            if (string.IsNullOrEmpty(path)) return;

            using var context = _contextFactory.CreateDbContext();
            var settings = context.VillagesSetting
               .Where(x => x.VillageId == VillageId.Value)
               .ToDictionary(x => x.Setting, x => x.Value);
            var jsonString = JsonSerializer.Serialize(settings);
            await File.WriteAllTextAsync(path, jsonString);
            await _dialogService.SendMessage("Information", "Settings exported");
        }

        [ReactiveCommand(RunInBackground = true)]
        private Dictionary<VillageSettingEnums, int> LoadSetting(VillageId villageId)
        {
            using var context = _contextFactory.CreateDbContext();
            var settings = context.VillagesSetting
               .Where(x => x.VillageId == VillageId.Value)
               .ToDictionary(x => x.Setting, x => x.Value);
            return settings;
        }
    }
}