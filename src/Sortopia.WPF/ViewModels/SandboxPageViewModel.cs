using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class SandboxPageViewModel : ViewModelBase
{
    public SandboxPageViewModel(SgdPageViewModel workspace, Action returnHome, Action openSortableTests, Action openProjectFolder)
    {
        Workspace = workspace;
        ReturnHomeCommand = new RelayCommand(returnHome);
        OpenSortableTestsCommand = new RelayCommand(openSortableTests);
        OpenProjectFolderCommand = new RelayCommand(openProjectFolder);
    }

    public string Title => "Sandbox";

    public SgdPageViewModel Workspace { get; }

    public ICommand ReturnHomeCommand { get; }
    public ICommand OpenSortableTestsCommand { get; }
    public ICommand OpenProjectFolderCommand { get; }

    private string projectStatus = "Open a project folder to work with runs.";
    public string ProjectStatus { get => projectStatus; internal set => SetProperty(ref projectStatus, value); }
}
