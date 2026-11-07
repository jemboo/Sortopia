using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Home;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openNetworks, Action openRandomNetworks, Action openSgd, Action openWorkspace)
    {
        OpenSgdCommand = new RelayCommand(openSgd);
        OpenWorkspaceCommand = new RelayCommand(openWorkspace);
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenSgdCommand { get; }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenWorkspaceCommand { get; }
}
