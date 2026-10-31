using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openSandbox, Action openNetworks, Action openRandomNetworks)
    {
        OpenSandboxCommand = new RelayCommand(openSandbox);
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenSandboxCommand { get; }
}
