using System.Windows.Input;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels;

public sealed class SandboxPageViewModel : ViewModelBase
{
    public SandboxPageViewModel(Action returnHome, Action openSortableTests, Action openProjectFolder)
    {
        ReturnHomeCommand = new RelayCommand(returnHome);
        OpenSortableTestsCommand = new RelayCommand(openSortableTests);
        OpenProjectFolderCommand = new RelayCommand(openProjectFolder);
    }

    public string Title => "Sandbox";

    public ParameterArrayViewModel Parameters { get; } = new(new double[,]
    {
        { 0.00, 0.07, 0.13, 0.20 },
        { 0.27, 0.33, 0.40, 0.47 },
        { 0.53, 0.60, 0.67, 0.73 },
        { 0.80, 0.87, 0.93, 1.00 }
    }, rowHeaders: ["Row 1", "Row 2", "Row 3", "Row 4"], columnHeaders: ["A", "B", "C", "D"]);

    public ICommand ReturnHomeCommand { get; }
    public ICommand OpenSortableTestsCommand { get; }
    public ICommand OpenProjectFolderCommand { get; }

    private string projectStatus = "Open a project folder to work with runs.";
    public string ProjectStatus { get => projectStatus; internal set => SetProperty(ref projectStatus, value); }
}
