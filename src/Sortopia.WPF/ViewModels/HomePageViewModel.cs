using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openSandbox, Action openNetworks)
    {
        OpenSandboxCommand = new RelayCommand(openSandbox);
        OpenNetworksCommand = new RelayCommand(openNetworks);
    }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenSandboxCommand { get; }
}
