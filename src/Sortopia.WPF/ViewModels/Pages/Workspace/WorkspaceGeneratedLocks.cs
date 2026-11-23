using Sortopia.Workspaces.Persistence;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    private readonly HashSet<Guid> projectsWithGeneratedFiles = [];
    private void RefreshGeneratedLocks()
    {
        if (savedWorkspace is null) return;
        var refreshed = WorkspaceFileStore.RefreshProjectStates(savedWorkspace, CancellationToken.None, projectsWithGeneratedFiles.ToArray());
        var locked = refreshed.GeneratedFiles.LockedProjectIds.ToHashSet();
        var lockedModules = refreshed.GeneratedFiles.LockedModuleIds.ToHashSet();
        var lockedRuns = refreshed.GeneratedFiles.LockedRunIds.ToHashSet();
        projectsWithGeneratedFiles.UnionWith(locked);
        ApplySavedLifecycleChanges(refreshed.Workspace);
        bool Visit(WorkspaceTreeNode node)
        {
            if (node.IsRun) node.Editor?.SetSnapshotControlsLock(lockedRuns.Contains(node.Id) ? refreshed.Workspace.TryReadRunSnapshotControls(node.Id)?.Value : null);
            // A pending import has not reached its final location yet.
            bool importedFiles = node.IsProject && node.SavedFolderPath is null && node.ImportSourcePath is { } source
                && WorkspaceFileStore.HasRunParameterFiles(source, CancellationToken.None);
            bool hasFiles = node.IsProject && (locked.Contains(node.Id) || importedFiles)
                || node.Kind == WorkspaceEntityKind.Module && lockedModules.Contains(node.Id);
            foreach (var child in node.Children) hasFiles |= Visit(child);
            if (node.IsProject || node.Kind == WorkspaceEntityKind.Module) node.SetGeneratedParametersLock(hasFiles);
            return hasFiles;
        }
        foreach (var root in Roots) Visit(root);
    }

    private void KeepSealedAncestorsLocked()
    {
        foreach (var project in Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsSealed))
            for (var node = project; node is { IsWorkspace: false }; node = node.Parent)
                node.SetGeneratedParametersLock(true);
    }
}
