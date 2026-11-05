using System.IO;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels;

/// <summary>Owns the page instances and selects the view model displayed by the shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly HomePageViewModel homePage;
    private readonly SandboxPageViewModel sandboxPage;
    private readonly NetworksPageViewModel networksPage;
    private readonly RandomNetworksPageViewModel randomNetworksPage;
    private readonly SgdPageViewModel sgdPage;
    private readonly Func<string?, string?> pickFolder;
    private SortableTestsPageViewModel? sortableTestsPage;
    private string? projectFolder;
    private ViewModelBase currentPage;

    public MainWindowViewModel(Func<string?, string?> pickFolder)
    {
        this.pickFolder = pickFolder;
        homePage = new HomePageViewModel(OpenNetworks, OpenRandomNetworks, OpenSgd, OpenSandbox);
        networksPage = new NetworksPageViewModel(ReturnHome);
        randomNetworksPage = new RandomNetworksPageViewModel(ReturnHome);
        sgdPage = new SgdPageViewModel(ReturnHome);
        sandboxPage = new SandboxPageViewModel(sgdPage, ReturnHome, OpenSortableTests, OpenProjectFolder);
        currentPage = homePage;
    }

    public ViewModelBase CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    private async void OpenProjectFolder()
    {
        if (sortableTestsPage is { CanEdit: false })
        {
            sandboxPage.ProjectStatus = $"A run is active in {projectFolder}. Finish or cancel it before switching projects.";
            return;
        }

        var selected = pickFolder(projectFolder);
        if (selected is null) return;
        try
        {
            var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selected));
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Folder does not exist: {folder}");
            if (!string.Equals(projectFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                var inventory = ProjectInventory.FindProjectDirectory(folder);
                var libraryPath = Path.Combine(folder, "runs");
                if (inventory == null) Directory.CreateDirectory(libraryPath);
                sortableTestsPage = new SortableTestsPageViewModel(ReturnHome, new RunLibrary(libraryPath), pickFolder);
                projectFolder = folder;
                if (inventory != null)
                {
                    var page = sortableTestsPage;
                    page.Tab = 1;
                    page.DesignerTab = 1;
                    ShowRuns(page);
                    sandboxPage.ProjectStatus = $"Project · {projectFolder}";
                    await page.CsvInventory.OpenAsync(inventory);
                    return;
                }
            }
            sandboxPage.ProjectStatus = $"Project · {projectFolder}";
            ShowRuns(sortableTestsPage!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            sandboxPage.ProjectStatus = $"Could not open {selected}. {error.Message}";
        }
    }

    private void OpenSortableTests()
    {
        if (sortableTestsPage is null) { OpenProjectFolder(); return; }
        ShowRuns(sortableTestsPage);
    }

    private void ShowRuns(SortableTestsPageViewModel page)
    {
        if (CurrentPage == sandboxPage) sgdPage.Stop();
        page.Open();
        CurrentPage = page;
    }

    private void OpenSandbox() => CurrentPage = sandboxPage;

    private void OpenNetworks() => CurrentPage = networksPage;

    private void OpenRandomNetworks() => CurrentPage = randomNetworksPage;

    private void OpenSgd() => CurrentPage = sgdPage;

    private void ReturnHome()
    {
        if (CurrentPage is RandomNetworksPageViewModel generated) generated.Leave();
        else if (CurrentPage == sgdPage || CurrentPage == sandboxPage) sgdPage.Stop();
        else if (CurrentPage == networksPage) networksPage.Pause();
        CurrentPage = homePage;
    }

    public void Dispose() { sortableTestsPage?.Stop(); networksPage.Dispose(); randomNetworksPage.Dispose(); sgdPage.Stop(); }
}
