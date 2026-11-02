using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openNetworks, Action openRandomNetworks, Action openSgd, Action openSandbox)
    {
        OpenSgdCommand = new RelayCommand(openSgd);
        OpenSandboxCommand = new RelayCommand(openSandbox);
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenSgdCommand { get; }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenSandboxCommand { get; }
}
