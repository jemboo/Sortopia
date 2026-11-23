using Sortopia.Workspaces.Persistence;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    // Load, save, discard and reset deliberately replace the open editors.
    private void ReplaceDraftFromSavedWorkspace(SavedWorkspace value)
    {
        savedWorkspace = value;
        savedConfigurationError = "";
        ReplaceDraft(WorkspaceTreeNode.FromDocument(value.CreateEditDocument()));
    }

    // Generation, resume and file removal advance the baseline while keeping
    // editor instances, selection and unsaved values. Only lifecycle transitions apply.
    private void ApplySavedLifecycleChanges(SavedWorkspace value)
    {
        if (ReferenceEquals(value, savedWorkspace)) return;
        var dirty = HasUnsavedChanges;
        savedWorkspace = value;
        try
        {
            foreach (var project in Roots.SelectMany(root => root.DescendantsAndSelf()).Where(node => node.IsProject))
                if (value.TryReadProjectState(project.Id)?.Value is { } state && state.IsSealed != project.IsSealed)
                    project.ApplyProjectState(state, preserveDraft: true);
        }
        finally { HasUnsavedChanges = dirty; }
        NotifyDraftProperties();
    }
}
