using System.IO;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    private readonly HashSet<Guid> projectsWithGeneratedFiles = [];
    private void RefreshGeneratedLocks()
    {
        if (savedDocument is null) return;
        var dirty = HasUnsavedChanges;
        var locked = WorkspaceFileStore.ProjectsWithRunParameters(WorkspaceFolder, savedDocument).ToHashSet();
        var lockedRuns = WorkspaceFileStore.RunsWithRunParameters(WorkspaceFolder, savedDocument).ToHashSet();
        static IEnumerable<runDto> Runs(ModuleDocument module) => module.Projects.SelectMany(p => p.Runs).Concat(module.Modules.SelectMany(Runs));
        var runControls = savedDocument.Modules.SelectMany(Runs).ToDictionary(r => r.id, r => r.sgd?.Value);
        projectsWithGeneratedFiles.UnionWith(locked);
        var updated = WorkspaceFileStore.ReleaseUnusedProjects(WorkspaceFolder, savedDocument, CancellationToken.None, projectsWithGeneratedFiles.ToArray());
        if (!ReferenceEquals(updated, savedDocument))
        {
            savedDocument = updated;
            ClearRunScope();
            static IEnumerable<ProjectDocument> Projects(ModuleDocument module) => module.Projects.Concat(module.Modules.SelectMany(Projects));
            var definitions = updated.Modules.SelectMany(Projects).ToDictionary(p => p.Definition.id, p => p.Definition);
            foreach (var node in Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsSealed))
                if (definitions.TryGetValue(node.Id, out var definition) && !definition.isSealed) node.ResumeEditing(definition);
            HasUnsavedChanges = dirty;
        }
        bool Visit(WorkspaceTreeNode node)
        {
            if (node.IsRun) node.Editor?.SetSnapshotControlsLock(lockedRuns.Contains(node.Id) ? runControls.GetValueOrDefault(node.Id) : null);
            // A pending import has not reached its final location yet.
            bool importedFiles = node.IsProject && node.SavedFolderPath is null && node.ImportSourcePath is { } source
                && Directory.Exists(Path.Combine(source, "runParameters")) && Directory.EnumerateFileSystemEntries(Path.Combine(source, "runParameters")).Any();
            bool hasFiles = node.IsProject && (locked.Contains(node.Id) || importedFiles);
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
