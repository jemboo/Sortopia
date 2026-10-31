namespace Sortopia.WPF.ViewModels;

/// <summary>Owns the page instances and selects the view model displayed by the shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly HomePageViewModel homePage;
    private readonly SandboxPageViewModel sandboxPage;
    private readonly NetworksPageViewModel networksPage;
    private ViewModelBase currentPage;

    public MainWindowViewModel()
    {
        homePage = new HomePageViewModel(OpenSandbox, OpenNetworks);
        sandboxPage = new SandboxPageViewModel(ReturnHome);
        networksPage = new NetworksPageViewModel(ReturnHome);
        currentPage = homePage;
    }

    public ViewModelBase CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    private void OpenSandbox() => CurrentPage = sandboxPage;

    private void OpenNetworks() => CurrentPage = networksPage;

    private void ReturnHome()
    {
        networksPage.Pause();
        CurrentPage = homePage;
    }

    public void Dispose() => networksPage.Dispose();
}
