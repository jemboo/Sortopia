using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class HomePageViewModel : ViewModelBase
{
    public HomePageViewModel(Action openSandbox, Action openNetworks, Action openRandomNetworks, Action openSortableTests, Action openProjectFolder)
    {
        OpenSortableTestsCommand = new RelayCommand(openSortableTests);
        OpenProjectFolderCommand = new RelayCommand(openProjectFolder);
        OpenSandboxCommand = new RelayCommand(openSandbox);
        OpenNetworksCommand = new RelayCommand(openNetworks);
        OpenRandomNetworksCommand = new RelayCommand(openRandomNetworks);
    }

    public ICommand OpenSortableTestsCommand { get; }

    public ICommand OpenProjectFolderCommand { get; }

    private string projectStatus = "Open a project folder to work with runs.";
    public string ProjectStatus { get => projectStatus; internal set => SetProperty(ref projectStatus, value); }

    public ICommand OpenRandomNetworksCommand { get; }

    public ICommand OpenNetworksCommand { get; }

    public ICommand OpenSandboxCommand { get; }
}
