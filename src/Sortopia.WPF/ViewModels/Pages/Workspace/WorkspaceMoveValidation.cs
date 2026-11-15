using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceFunctionConflict(WorkspaceTreeNode Node, bool IsSetup, string? SourceFolder = null)
{
    public string ItemKey => IsSetup ? "setupFunction" : "resultsFunction";
    public string FunctionName => (IsSetup ? Node.Editor!.SetupFunctionOverride : Node.Editor!.ResultsFunctionOverride).OverrideValue ?? "";
    public string LinkHint => SourceFolder is null ? $"Edit {FunctionName} on this module." : $"Open source module folder: {SourceFolder}";
    public string Location
    {
        get
        {
            if (SourceFolder is not null) return $"{SourceFolder} · {(IsSetup ? "Setup" : "Results")} function · source folder";
            var names = new Stack<string>();
            for (var item = Node; item is not null; item = item.Parent) names.Push(item.Name);
            return $"{string.Join(" / ", names)} · {(IsSetup ? "Setup" : "Results")} function";
        }
    }
}

public sealed partial class WorkspacePageViewModel
{
    public RelayCommand ReviewMoveFunctionConflictCommand { get; private set; } = null!;
    public IReadOnlyList<WorkspaceFunctionConflict> MoveFunctionConflicts
    {
        get
        {
            if (cutNode is not { Kind: WorkspaceEntityKind.Module } node || Selected is not { } destination
                || !destination.CanContain(node) || ReferenceEquals(destination, node.Parent)
                || node.DescendantsAndSelf().Contains(destination)) return [];

            return FindModuleFunctionConflicts(node, destination);
        }
    }
    private static IReadOnlyList<WorkspaceFunctionConflict> FindModuleFunctionConflicts(WorkspaceTreeNode node, WorkspaceTreeNode destination)
    {
        var ancestors = new Stack<WorkspaceTreeNode>();
        for (var current = destination; current is not null; current = current.Parent)
            if (current.Kind == WorkspaceEntityKind.Module) ancestors.Push(current);
        var conflicts = new List<WorkspaceFunctionConflict>();
        var seen = new HashSet<WorkspaceFunctionConflict>();
        foreach (var setup in node.IsBranch ? new[] { false, true } : new[] { false })
        {
            bool Defines(WorkspaceTreeNode module) => !string.IsNullOrWhiteSpace(
                (setup ? module.Editor!.SetupFunctionOverride : module.Editor!.ResultsFunctionOverride).OverrideValue);
            var definitions = ancestors.Where(Defines).ToList();
            void Visit(WorkspaceTreeNode module)
            {
                var local = Defines(module);
                if (local) definitions.Add(module);
                if (definitions.Count > 1)
                    foreach (var definition in definitions)
                    {
                        var conflict = new WorkspaceFunctionConflict(definition, setup);
                        if (seen.Add(conflict)) conflicts.Add(conflict);
                    }
                foreach (var child in module.Children.Where(child => child.Kind == WorkspaceEntityKind.Module)) Visit(child);
                if (local) definitions.RemoveAt(definitions.Count - 1);
            }
            Visit(node);
        }
        return conflicts;
    }
    public bool HasMoveFunctionConflicts => MoveFunctionConflicts.Count > 0;
    public string MoveFunctionConflictSummary => HasMoveFunctionConflicts
        ? $"Cannot move '{cutNode!.Name}' under '{Selected!.Name}': a setup or results function would be defined more than once along a module path. Review the definitions below."
        : "";
    private void InitializeMoveValidation()
    {
        ReviewMoveFunctionConflictCommand = new RelayCommand(value =>
        {
            if (value is not WorkspaceFunctionConflict conflict
                || !Roots.SelectMany(root => root.DescendantsAndSelf()).Contains(conflict.Node)) return;
            Selected = conflict.Node;
            SelectConfigurationItem(conflict.ItemKey);
        }, () => CanEdit);
    }
    private void NotifyMoveValidation()
    {
        OnPropertyChanged(nameof(MoveFunctionConflicts)); OnPropertyChanged(nameof(HasMoveFunctionConflicts));
        OnPropertyChanged(nameof(MoveFunctionConflictSummary));
        ReviewMoveFunctionConflictCommand.RaiseCanExecuteChanged();
    }
}
