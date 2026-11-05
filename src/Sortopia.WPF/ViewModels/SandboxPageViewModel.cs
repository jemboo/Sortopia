using System.Windows.Input;

namespace Sortopia.WPF.ViewModels;

public sealed class SandboxPageViewModel : ViewModelBase
{
    public SandboxPageViewModel(SgdPageViewModel workspace) => Workspace = workspace;

    public string Title => "Sandbox";

    public SgdPageViewModel Workspace { get; }

    public ICommand ReturnHomeCommand => Workspace.ReturnHomeCommand;
    public ICommand OpenSortableTestsCommand => Workspace.OpenSortableTestsCommand;
    public ICommand OpenProjectFolderCommand => Workspace.OpenProjectFolderCommand;

    public string ProjectStatus { get => Workspace.ProjectStatus; internal set => Workspace.ProjectStatus = value; }
}
