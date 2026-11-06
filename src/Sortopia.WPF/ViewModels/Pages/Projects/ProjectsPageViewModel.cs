using System.IO;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Settings;

namespace Sortopia.WPF.ViewModels.Pages.Projects;

public sealed class ProjectsPageViewModel : ViewModelBase
{
    private readonly Func<string?, string?> pickFolder;
    private readonly ProjectsFolderSettings settings;
    private string projectsFolder = "";
    private string status = "No Projects folder selected.";
    private string error = "";

    public ProjectsPageViewModel(Action returnHome, Func<string?, string?> pickFolder, ProjectsFolderSettings settings)
    {
        this.pickFolder = pickFolder;
        this.settings = settings;
        ReturnHomeCommand = new RelayCommand(returnHome);
        BrowseProjectsFolderCommand = new RelayCommand(BrowseProjectsFolder);
        try
        {
            var saved = settings.Load();
            if (!string.IsNullOrWhiteSpace(saved))
            {
                ProjectsFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(saved));
                Status = "Projects folder saved.";
                if (!Directory.Exists(ProjectsFolder))
                    Error = "The saved Projects folder is unavailable. Browse to choose another folder.";
            }
        }
        catch (Exception exception) when (IsFolderError(exception))
        {
            Error = $"Could not load the Projects folder. {exception.Message}";
        }
    }

    public string ProjectsFolder { get => projectsFolder; private set => SetProperty(ref projectsFolder, value); }
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string Error { get => error; private set => SetProperty(ref error, value); }
    public RelayCommand ReturnHomeCommand { get; }
    public RelayCommand BrowseProjectsFolderCommand { get; }

    private void BrowseProjectsFolder()
    {
        try
        {
            var selected = pickFolder(string.IsNullOrEmpty(ProjectsFolder) ? null : ProjectsFolder);
            if (selected is null) return;
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder does not exist: {folder}");
            settings.Save(folder);
            ProjectsFolder = folder;
            Status = "Projects folder saved.";
            Error = "";
        }
        catch (Exception exception) when (IsFolderError(exception))
        {
            Error = $"Could not set the Projects folder. {exception.Message}";
        }
    }

    private static bool IsFolderError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
