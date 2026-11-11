using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Home;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openNetworks, Action openRandomNetworks, Action openSgd, Action openWorkspace, Action? openFileInspector = null, Action? openResultsExecution = null)
    {
        OpenSgdCommand = new RelayCommand(openSgd);
        OpenWorkspaceCommand = new RelayCommand(openWorkspace);
        OpenFileInspectorCommand = new RelayCommand(openFileInspector ?? (() => { }));
        OpenResultsExecutionCommand = new RelayCommand(openResultsExecution ?? (() => { }));
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenSgdCommand { get; }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenWorkspaceCommand { get; }
    public ICommand OpenFileInspectorCommand { get; }
    public ICommand OpenResultsExecutionCommand { get; }
}
