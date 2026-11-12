using System;

using MainCore.UI.ViewModels.Tabs;

using ReactiveUI;

namespace WPFUI.Views.Tabs
{
    using ReactiveUI.Primitives;

    public class ConquestTabBase : ReactiveUserControl<ConquestViewModel>
    {
    }

    public partial class ConquestTab : ConquestTabBase
    {
        public ConquestTab()
        {
            InitializeComponent();
            var refreshTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1),
            };
            refreshTimer.Tick += (_, _) => ViewModel?.RefreshConquests();
            Loaded += (_, _) =>
            {
                ViewModel?.RefreshConquests();
                refreshTimer.Start();
            };
            Unloaded += (_, _) => refreshTimer.Stop();
            this.WhenActivated(disposables =>
            {
                this.OneWayBind(ViewModel, vm => vm.Villages.Items, view => view.OriginVillage.ItemsSource).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.Conquests, view => view.ConquestTasks.ItemsSource).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.SelectedConquest, view => view.ConquestTasks.SelectedItem).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.CancelConquestCommand, view => view.CancelConquestButton).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.Villages.SelectedItem, view => view.OriginVillage.SelectedItem).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.TargetX, view => view.TargetX.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.TargetY, view => view.TargetY.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArrivalDate, view => view.ArrivalDate.SelectedDate).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArrivalTime, view => view.ArrivalTime.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.SlowestTroopSpeed, view => view.SlowestTroopSpeed.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.BootsPercent, view => view.BootsPercent.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArtifactMultiplier, view => view.ArtifactMultiplier.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.DelaySeconds, view => view.DelaySeconds.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.TroopWaves, view => view.TroopWaves.ItemsSource).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.AddWaveCommand, view => view.AddWaveButton).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.CopyWaveCommand, view => view.CopyWaveButton).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.OriginCoordinates, view => view.OriginCoordinates.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.TournamentSquareText, view => view.TournamentSquareLevel.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.PlanPreview, view => view.PlanPreview.Text).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.CalculatePlanCommand, view => view.CalculatePlanButton).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.ActionCommand, view => view.ActionButton).DisposeWith(disposables);
            });
        }
    }
}
