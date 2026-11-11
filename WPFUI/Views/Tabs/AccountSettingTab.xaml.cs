using MainCore.UI.ViewModels.Tabs;

using ReactiveUI;

namespace WPFUI.Views.Tabs
{
    using ReactiveUI.Primitives;

    public class AccountSettingTabBase : ReactiveUserControl<AccountSettingViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for AccountSettingTab.xaml
    /// </summary>
    public partial class AccountSettingTab : AccountSettingTabBase
    {
        public AccountSettingTab()
        {
            InitializeComponent();
            TelegramBotToken.PasswordChanged += (_, _) =>
            {
                if (ViewModel is not null) ViewModel.TelegramBotToken = TelegramBotToken.Password;
            };
            Unloaded += (_, _) => TelegramBotToken.Password = "";
            this.WhenActivated(d =>
            {
                this.BindCommand(ViewModel, vm => vm.ExportCommand, v => v.ExportButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ImportCommand, v => v.ImportButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.SaveCommand, v => v.SaveButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.SaveTelegramCommand, v => v.SaveTelegramButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.TestTelegramCommand, v => v.TestTelegramButton).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.TelegramChatId, v => v.TelegramChatId.Text).DisposeWith(d);
                this.WhenAnyValue(x => x.ViewModel!.TelegramBotToken)
                    .ObserveOn(RxSchedulers.MainThreadScheduler)
                    .Subscribe(token => TelegramBotToken.Password = token).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.AccountSettingInput.ClickDelay, v => v.ClickDelay.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.TaskDelay, v => v.TaskDelay.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.WorkTime, v => v.WorkTime.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.SleepTime, v => v.SleepTime.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoLoadVillage, v => v.EnableAutoLoadVillage.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.HeadlessChrome, v => v.HeadlessChrome.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoStartAdventure, v => v.EnableAutoStartAdventure.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoClaimDailyQuest, v => v.EnableAutoClaimDailyQuest.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoSetHeroPoint, v => v.EnableAutoSetHeroPoint.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableReportNotifications, v => v.EnableReportNotifications.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableAutoDeactivateLostReportTargets, v => v.EnableAutoDeactivateLostReportTargets.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.AccountSettingInput.EnableIncomingTroopsNotifications, v => v.EnableIncomingTroopsNotifications.IsChecked).DisposeWith(d);
            });
        }
    }
}