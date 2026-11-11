using System.Security.Cryptography;
using System.Text.Json;

using MainCore.Infrasturecture.Extensions;
using MainCore.UI.Models.Input;
using MainCore.UI.ViewModels.Abstract;

namespace MainCore.UI.ViewModels.Tabs
{
    using FluentValidation;

    using ReactiveUI.Primitives;
    using ReactiveUI.Primitives.Signals;

    [RegisterSingleton<AccountSettingViewModel>]
    public partial class AccountSettingViewModel : AccountTabViewModelBase
    {
        public AccountSettingInput AccountSettingInput { get; } = new();

        private readonly DialogService _dialogService;
        private readonly IValidator<AccountSettingInput> _accountsettingInputValidator;
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly TaskManager _taskManager;
        private readonly TelegramSettingsStore _telegramSettingsStore;
        private readonly TelegramSender _telegramSender;
        private readonly ReportMonitor _reportMonitor;

        public AccountSettingViewModel(DialogService dialogService, IValidator<AccountSettingInput> accountsettingInputValidator, IDbContextFactory<AppDbContext> contextFactory, TaskManager taskManager, TelegramSettingsStore telegramSettingsStore, TelegramSender telegramSender, ReportMonitor reportMonitor)
        {
            _dialogService = dialogService;
            _accountsettingInputValidator = accountsettingInputValidator;
            _contextFactory = contextFactory;
            _taskManager = taskManager;
            _telegramSettingsStore = telegramSettingsStore;
            _telegramSender = telegramSender;
            _reportMonitor = reportMonitor;

            LoadSettingsCommand.ObserveOn(RxSchedulers.MainThreadScheduler).Subscribe(AccountSettingInput.Set);
        }

        protected override async Task Load(AccountId accountId)
        {
            await LoadSettingsCommand.Execute(accountId);
            try
            {
                TelegramChatId = _telegramSettingsStore.Load().ChatId;
                TelegramBotToken = "";
            }
            catch (Exception ex) when (IsTelegramSettingsError(ex))
            {
                await _dialogService.SendMessage("Error", "Unable to load Telegram settings. Save them again on this Windows account.");
            }
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
            if (!await ValidateReportSettings(settings)) return;
            context.SaveAccountSetting(AccountId, settings);
            context.TriggerTask(_taskManager, AccountId, settings, _reportMonitor);
            await _dialogService.SendMessage("Information", "Settings saved.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Import()
        {
            var path = await _dialogService.OpenFileDialog();
            if (string.IsNullOrEmpty(path)) return;
            Dictionary<AccountSettingEnums, int> settings;
            try
            {
                var jsonString = await File.ReadAllTextAsync(path);
                settings = JsonSerializer.Deserialize<Dictionary<AccountSettingEnums, int>>(jsonString)!;
            }
            catch
            {
                await _dialogService.SendMessage("Warning", "Invalid file.");
                return;
            }
            await Signal.Start(() => AccountSettingInput.Set(settings), RxSchedulers.MainThreadScheduler);

            var result = await _accountsettingInputValidator.ValidateAsync(AccountSettingInput);
            if (!result.IsValid)
            {
                await _dialogService.SendMessage("Error", string.Join(Environment.NewLine, result.Errors.Select(x => x.ErrorMessage)));
                return;
            }
            using var context = _contextFactory.CreateDbContext();
            settings.Remove(AccountSettingEnums.Tribe);
            if (!await ValidateReportSettings(settings)) return;
            context.SaveAccountSetting(AccountId, settings);
            context.TriggerTask(_taskManager, AccountId, settings, _reportMonitor);
            await _dialogService.SendMessage("Information", "Settings imported.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task Export()
        {
            var path = await _dialogService.SaveFileDialog();
            if (string.IsNullOrEmpty(path)) return;

            using var context = _contextFactory.CreateDbContext();

            var settings = context.AccountsSetting
              .Where(x => x.AccountId == AccountId.Value)
              .ToDictionary(x => x.Setting, x => x.Value);

            var jsonString = JsonSerializer.Serialize(settings);
            await File.WriteAllTextAsync(path, jsonString);
            await _dialogService.SendMessage("Information", "Settings exported.");
        }

        [ReactiveCommand(RunInBackground = true)]
        private Dictionary<AccountSettingEnums, int> LoadSettings(AccountId accountId)
        {
            using var context = _contextFactory.CreateDbContext();

            var settings = context.AccountsSetting
              .Where(x => x.AccountId == AccountId.Value)
              .ToDictionary(x => x.Setting, x => x.Value);
            return settings;
        }

        [Reactive]
        private string _telegramBotToken = "";

        [Reactive]
        private string _telegramChatId = "";

        [ReactiveCommand(RunInBackground = true)]
        private async Task SaveTelegram()
        {
            try
            {
                var credentials = GetTelegramCredentials();
                if (!credentials.IsValid)
                {
                    await _dialogService.SendMessage("Error", "Enter a valid bot token and numeric chat ID.");
                    return;
                }
                _telegramSettingsStore.Save(credentials);
                TelegramBotToken = "";
                await _dialogService.SendMessage("Information", "Global Telegram settings saved for this Windows user.");
            }
            catch (Exception ex) when (IsTelegramSettingsError(ex))
            {
                await _dialogService.SendMessage("Error", "Unable to save protected Telegram settings.");
            }
        }

        [ReactiveCommand(RunInBackground = true)]
        private async Task TestTelegram(CancellationToken cancellationToken)
        {
            try
            {
                var result = await _telegramSender.Send(
                    GetTelegramCredentials(), "[TBS] Telegram test message.", cancellationToken);
                await _dialogService.SendMessage(result.Success ? "Information" : "Error",
                    result.Success ? "Telegram test message sent." : result.Error);
            }
            catch (Exception ex) when (IsTelegramSettingsError(ex))
            {
                await _dialogService.SendMessage("Error", "Unable to read Telegram settings. Enter the token and chat ID again.");
            }
        }

        private TelegramCredentials GetTelegramCredentials() => new()
        {
            BotToken = string.IsNullOrWhiteSpace(TelegramBotToken)
                ? _telegramSettingsStore.Load().BotToken
                : TelegramBotToken.Trim(),
            ChatId = TelegramChatId.Trim(),
        };

        private async Task<bool> ValidateReportSettings(Dictionary<AccountSettingEnums, int> settings)
        {
            if (settings.GetValueOrDefault(AccountSettingEnums.EnableReportNotifications) != 1) return true;
            try
            {
                if (_telegramSettingsStore.Load().IsValid) return true;
            }
            catch (Exception ex) when (IsTelegramSettingsError(ex))
            {
                await _dialogService.SendMessage("Error", "Unable to read Telegram settings. Save them again before enabling notifications.");
                return false;
            }
            await _dialogService.SendMessage("Error", "Save the global Telegram settings before enabling report notifications.");
            return false;
        }

        private static bool IsTelegramSettingsError(Exception exception) =>
            exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException or PlatformNotSupportedException;
    }
}