using MainCore.UI.ViewModels.Tabs;

using ReactiveUI;


namespace WPFUI.Views.Tabs
{
    using ReactiveUI.Primitives;

    public class FarmingTabBase : ReactiveUserControl<FarmingViewModel>
    {
    }

    /// <summary>
    /// Interaction logic for FarmingTab.xaml
    /// </summary>
    public partial class FarmingTab : FarmingTabBase
    {
        public FarmingTab()
        {
            InitializeComponent();
            this.WhenActivated(d =>
            {
                this.BindCommand(ViewModel, vm => vm.SaveCommand, v => v.SaveButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.StartCommand, v => v.StartButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.StopCommand, v => v.StopButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ActiveFarmListCommand, v => v.ActiveButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.UpdateFarmListCommand, v => v.Load).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.CheckTargetTroopsCommand, v => v.CheckTargetTroopsButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.ClearOasisCommand, v => v.ClearOasisButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.RemoveClearOasisCommand, v => v.RemoveClearOasisButton).DisposeWith(d);
                this.BindCommand(ViewModel, vm => vm.SaveOasisSettingsCommand, v => v.SaveOasisSettingsButton).DisposeWith(d);

                this.OneWayBind(ViewModel, vm => vm.FarmLists.Items, v => v.FarmlistGrid.ItemsSource).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmLists.SelectedItem, v => v.FarmlistGrid.SelectedItem).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.AccountSettingInput.UseStartAllButton, v => v.UseStartAllCheckbox.IsChecked).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.HasOasisSettings, v => v.OasisSettingsGroup.IsEnabled).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowRat, v => v.AllowRatOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowSpider, v => v.AllowSpiderOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowSnake, v => v.AllowSnakeOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowBat, v => v.AllowBatOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowWildBoar, v => v.AllowWildBoarOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowWolf, v => v.AllowWolfOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowBear, v => v.AllowBearOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowCrocodile, v => v.AllowCrocodileOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowTiger, v => v.AllowTigerOasisCheckBox.IsChecked).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AllowElephant, v => v.AllowElephantOasisCheckBox.IsChecked).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.FarmOasisSettingInput.SafetyFactor, v => v.OasisRaidSafetyFactor.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.FarmOasisSettingInput.LimitAnimal, v => v.LimitAnimal.ViewModel).DisposeWith(d);
                this.Bind(ViewModel, vm => vm.FarmOasisSettingInput.AttackTroopIndex, v => v.OasisAttackTroop.SelectedIndex).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.FarmOasisSettingInput.RaidSteppeRiders, v => v.RaidSteppeRiders.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.FarmOasisSettingInput.RaidMarksmen, v => v.RaidMarksmen.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.FarmOasisSettingInput.RaidMarauders, v => v.RaidMarauders.ViewModel).DisposeWith(d);
                this.OneWayBind(ViewModel, vm => vm.AccountSettingInput.FarmInterval, v => v.FarmInterval.ViewModel).DisposeWith(d);

                this.Bind(ViewModel, vm => vm.ActiveText, v => v.ActiveButton.Content).DisposeWith(d);
            });
        }
    }
}
