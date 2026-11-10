using System.IO;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceSaveIssue(WorkspaceTreeNode Node, string Section, string Message)
{
    public string Location
    {
        get
        {
            var names = new Stack<string>();
            for (var item = Node; item is not null; item = item.Parent) names.Push(item.Name);
            return $"{string.Join(" / ", names)} · {Node.KindLabel} · {Section}";
        }
    }
}

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand ReviewSaveIssueCommand { get; private set; } = null!;
    public IReadOnlyList<WorkspaceSaveIssue> SaveIssues => Roots.SelectMany(root => root.DescendantsAndSelf()).SelectMany(IssuesFor).ToArray();
    private static IEnumerable<WorkspaceSaveIssue> IssuesFor(WorkspaceTreeNode node)
    {
        if (node.NameError.Length > 0) yield return new(node, "Name", node.NameError);
        if (node.FolderNameError.Length > 0) yield return new(node, "Location", node.FolderNameError);
        if (node.Editor is { } editor)
            foreach (var issue in editor.ValidationIssues) yield return new(node, issue.Section, issue.Message);
    }
    public bool HasSaveIssues => SaveIssues.Count > 0;
    public string SaveIssueSummary => $"Cannot save yet · {SaveIssues.Count} {(SaveIssues.Count == 1 ? "field needs" : "fields need")} attention. Select an item below to fix it.";
    public string SaveHint
    {
        get
        {
            if (IsBusy) return "Wait for the current operation to finish, or cancel it before saving.";
            if (WorkspaceFolder.Length == 0) return "Choose a workspace folder with Browse before saving.";
            if (!Directory.Exists(WorkspaceFolder)) return $"The workspace folder is unavailable: {WorkspaceFolder}. Restore it or choose another folder with Browse.";
            if (workspaceFileState == WorkspaceFileState.Conflict) return $"Cannot save in this folder. {workspaceFileIssue}";
            if (SaveIssues.FirstOrDefault() is { } issue) return $"Cannot save: {issue.Location}. {issue.Message}";
            if (WorkspaceNameError.Length > 0) return WorkspaceNameError;
            if (!ShowNewWorkspace && savedDefinition is null) return "The workspace could not be loaded. Review the load error or choose another folder.";
            if (!ShowNewWorkspace && !HasUnsavedChanges && !HasMissingComponents) return "All changes are already saved.";
            return "Save all workspace, module, project and run changes.";
        }
    }
    private void InitializeSaveValidation()
    {
        ReviewSaveIssueCommand = new RelayCommand(value =>
        {
            if (value is not WorkspaceSaveIssue issue) return;
            Selected = issue.Node;
            InspectSaveIssue(issue);
            if (issue.Node.Editor is not { } editor) return;
            switch (issue.Section)
            {
                case "Results settings": editor.Sections.ResultsSettings = true; break;
                case "Setup settings": editor.Sections.SetupSettings = true; break;
                case "Results parameter defaults": editor.Sections.ResultsDefaults = true; break;
                case "Setup parameter defaults": editor.Sections.SetupDefaults = true; break;
                case "Results parameter spans": editor.Sections.ResultsSpans = true; break;
                case "Setup parameter spans": editor.Sections.SetupSpans = true; break;
            }
        }, () => CanEdit);
    }
    private void NotifySaveValidation()
    {
        OnPropertyChanged(nameof(SaveIssues)); OnPropertyChanged(nameof(HasSaveIssues));
        OnPropertyChanged(nameof(SaveIssueSummary)); OnPropertyChanged(nameof(SaveHint));
        ReviewSaveIssueCommand?.RaiseCanExecuteChanged();
    }
}
