using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Home;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openNetworks, Action openRandomNetworks, Action openSgd, Action openProjects)
    {
        OpenSgdCommand = new RelayCommand(openSgd);
        OpenProjectsCommand = new RelayCommand(openProjects);
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenSgdCommand { get; }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenProjectsCommand { get; }
}
