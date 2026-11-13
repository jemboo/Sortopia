using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Networks;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.ViewModels.Pages.RandomNetworks;
using Sortopia.WPF.ViewModels.Pages.Sgd;

namespace Sortopia.WPF.ViewModels;

/// <summary>Owns the top-level pages and selects the view model displayed by the shell.</summary>
public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly HomePageViewModel homePage;
    private readonly FileInspectorPageViewModel fileInspectorPage;
    private readonly ResultsExecutionPageViewModel resultsExecutionPage;
    private readonly WorkspacePageViewModel workspacePage;
    private readonly NetworksPageViewModel networksPage;
    private readonly RandomNetworksPageViewModel randomNetworksPage;
    private readonly SgdPageViewModel sgdPage;
    private ViewModelBase currentPage;

    public MainWindowViewModel(Func<string?, string?> pickWorkspaceFolder,
        WorkspaceFolderSettings? workspaceSettings = null, Func<WorkspaceUnsavedChoice>? confirmWorkspaceChanges = null,
        Func<WorkspaceFolderRequest, string?>? pickExistingWorkspaceFolder = null, Action<string>? openWorkspaceFolder = null)
    {
        homePage = new HomePageViewModel(OpenNetworks, OpenRandomNetworks, OpenSgd, OpenWorkspace, () => OpenFileInspector(false), OpenResultsExecution);
        networksPage = new NetworksPageViewModel(ReturnHome);
        randomNetworksPage = new RandomNetworksPageViewModel(ReturnHome);
        sgdPage = new SgdPageViewModel(ReturnHome);
        fileInspectorPage = new FileInspectorPageViewModel(ReturnHome, pickWorkspaceFolder);
        var executionSettings = new ResultsExecutionSettings();
        resultsExecutionPage = new ResultsExecutionPageViewModel(ReturnHome, pickWorkspaceFolder, executionSettings.Load, executionSettings.Save);
        workspacePage = new WorkspacePageViewModel(ReturnHome, pickWorkspaceFolder, workspaceSettings ?? new WorkspaceFolderSettings(), confirmWorkspaceChanges, pickExistingWorkspaceFolder, openWorkspaceFolder,
            () => OpenFileInspector(true), OpenWorkspaceResultsExecutionAsync);
        currentPage = homePage;
    }

    public ViewModelBase CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    private void OpenWorkspace() => CurrentPage = workspacePage;
    private void OpenResultsExecution()
    {
        resultsExecutionPage.SetReturnAction(ReturnHome, false);
        CurrentPage = resultsExecutionPage;
    }
    private async Task OpenWorkspaceResultsExecutionAsync(string folder, Guid? runId)
    {
        resultsExecutionPage.SetReturnAction(OpenWorkspace, true);
        CurrentPage = resultsExecutionPage;
        await resultsExecutionPage.SetFolderAsync(folder, runId);
    }

    private void OpenFileInspector(bool fromWorkspace)
    {
        CurrentPage = fileInspectorPage;
        fileInspectorPage.SetReturnAction(fromWorkspace ? OpenWorkspace : ReturnHome);
        if (workspacePage.WorkspaceFolder.Length > 0)
            _ = fileInspectorPage.OpenRootAsync(workspacePage.WorkspaceFolder);
    }

    public Task<bool> TryCloseAsync() => workspacePage.TryLeaveAsync();

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

    public void Dispose() { resultsExecutionPage.Dispose(); fileInspectorPage.Dispose(); workspacePage.Cancel(); networksPage.Dispose(); randomNetworksPage.Dispose(); sgdPage.Stop(); }
}
