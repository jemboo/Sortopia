using Sortopia.WPF.ViewModels.Shared;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    public async Task<bool> OpenProducingRunAsync(ProducingRun target)
    {
        if (IsBusy || resolvingNavigation) return false;
        if (!string.Equals(WorkspaceFolder, target.WorkspaceFolder, StringComparison.OrdinalIgnoreCase))
        {
            var saved = await Task.Run(() => WorkspaceFileStore.LoadWorkspace(target.WorkspaceFolder));
            var run = new WorkspaceDocumentIndex(saved.CreateEditDocument()).TryFindRun(target.RunId);
            if (run is null || run.Value.Document.parentProjectId != target.ProjectId)
                throw new InvalidOperationException("The producing run is no longer in this workspace.");
            if (!await TryNavigateAsync(_ =>
            {
                settings.Save(target.WorkspaceFolder);
                WorkspaceFolder = target.WorkspaceFolder;
                executions.Clear();
                ReplaceDraftFromSavedWorkspace(saved);
                RefreshDiskStatus();
            })) return false;
        }
        var node = Roots.SelectMany(root => root.DescendantsAndSelf())
            .FirstOrDefault(node => node.IsRun && node.Id == target.RunId && node.Parent?.Id == target.ProjectId);
        if (node is null) throw new InvalidOperationException("The producing run is not present in the current workspace draft.");
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) parent.IsExpanded = true;
        Selected = node;
        return true;
    }
}
