namespace Sortopia.WPF.ViewModels;

/// <summary>Owns the page instances and selects the view model displayed by the shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly HomePageViewModel homePage;
    private readonly SandboxPageViewModel sandboxPage;
    private readonly NetworksPageViewModel networksPage;
    private readonly RandomNetworksPageViewModel randomNetworksPage;
    private ViewModelBase currentPage;

    public MainWindowViewModel()
    {
        homePage = new HomePageViewModel(OpenSandbox, OpenNetworks, OpenRandomNetworks);
        sandboxPage = new SandboxPageViewModel(ReturnHome);
        networksPage = new NetworksPageViewModel(ReturnHome);
        randomNetworksPage = new RandomNetworksPageViewModel(ReturnHome);
        currentPage = homePage;
    }

    public ViewModelBase CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    private void OpenSandbox() => CurrentPage = sandboxPage;

    private void OpenNetworks() => CurrentPage = networksPage;

    private void OpenRandomNetworks() => CurrentPage = randomNetworksPage;

    private void ReturnHome()
    {
        networksPage.Pause();
        randomNetworksPage.Leave();
        CurrentPage = homePage;
    }

    public void Dispose() { networksPage.Dispose(); randomNetworksPage.Dispose(); }
}
