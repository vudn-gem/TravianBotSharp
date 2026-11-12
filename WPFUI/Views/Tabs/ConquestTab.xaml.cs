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
            this.WhenActivated(disposables =>
            {
                this.OneWayBind(ViewModel, vm => vm.Villages.Items, view => view.OriginVillage.ItemsSource).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.Villages.SelectedItem, view => view.OriginVillage.SelectedItem).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.TargetX, view => view.TargetX.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.TargetY, view => view.TargetY.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArrivalDate, view => view.ArrivalDate.SelectedDate).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArrivalTime, view => view.ArrivalTime.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.SlowestTroopSpeed, view => view.SlowestTroopSpeed.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.BootsPercent, view => view.BootsPercent.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.ArtifactMultiplier, view => view.ArtifactMultiplier.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.DelaySeconds, view => view.DelaySeconds.Text).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.CatapultTargetIndex, view => view.CatapultTarget.SelectedIndex).DisposeWith(disposables);
                this.Bind(ViewModel, vm => vm.TroopWaves, view => view.TroopWaves.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.OriginCoordinates, view => view.OriginCoordinates.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.TournamentSquareText, view => view.TournamentSquareLevel.Text).DisposeWith(disposables);
                this.OneWayBind(ViewModel, vm => vm.PlanPreview, view => view.PlanPreview.Text).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.CalculatePlanCommand, view => view.CalculatePlanButton).DisposeWith(disposables);
                this.BindCommand(ViewModel, vm => vm.ActionCommand, view => view.ActionButton).DisposeWith(disposables);
            });
        }
    }
}
