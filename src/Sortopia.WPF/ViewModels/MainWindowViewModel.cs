using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Networks;
using Sortopia.WPF.ViewModels.Pages.Projects;
using Sortopia.WPF.ViewModels.Pages.RandomNetworks;
using Sortopia.WPF.ViewModels.Pages.Sgd;

namespace Sortopia.WPF.ViewModels;

/// <summary>Owns the top-level pages and selects the view model displayed by the shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly HomePageViewModel homePage;
    private readonly ProjectsPageViewModel projectsPage;
    private readonly NetworksPageViewModel networksPage;
    private readonly RandomNetworksPageViewModel randomNetworksPage;
    private readonly SgdPageViewModel sgdPage;
    private ViewModelBase currentPage;

    public MainWindowViewModel(Func<string?, string?> pickProjectsFolder,
        ProjectsFolderSettings? projectsSettings = null)
    {
        homePage = new HomePageViewModel(OpenNetworks, OpenRandomNetworks, OpenSgd, OpenProjects);
        networksPage = new NetworksPageViewModel(ReturnHome);
        randomNetworksPage = new RandomNetworksPageViewModel(ReturnHome);
        sgdPage = new SgdPageViewModel(ReturnHome);
        projectsPage = new ProjectsPageViewModel(ReturnHome, pickProjectsFolder, projectsSettings ?? new ProjectsFolderSettings());
        currentPage = homePage;
    }

    public ViewModelBase CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    private void OpenProjects() => CurrentPage = projectsPage;

    private void OpenNetworks() => CurrentPage = networksPage;

    private void OpenRandomNetworks() => CurrentPage = randomNetworksPage;

    private void OpenSgd() => CurrentPage = sgdPage;

    private void ReturnHome()
    {
        if (CurrentPage is RandomNetworksPageViewModel generated) generated.Leave();
        else if (CurrentPage == sgdPage) sgdPage.Stop();
        else if (CurrentPage == networksPage) networksPage.Pause();
        CurrentPage = homePage;
    }

    public void Dispose() { networksPage.Dispose(); randomNetworksPage.Dispose(); sgdPage.Stop(); }
}
