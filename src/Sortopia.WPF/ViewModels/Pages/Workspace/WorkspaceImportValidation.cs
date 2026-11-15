using System.IO;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspacePageViewModel
{
    private sealed record RejectedModuleImport(WorkspaceTreeNode Module, Guid DestinationId, string SourceFolder);
    private RejectedModuleImport? rejectedModuleImport;
    private WorkspaceTreeNode? ImportDestination => rejectedModuleImport is { } import
        ? Roots.SelectMany(root => root.DescendantsAndSelf()).FirstOrDefault(node => node.Id == import.DestinationId) : null;
    public RelayCommand ReviewImportFunctionConflictCommand { get; private set; } = null!;
    public bool HasRejectedModuleImport => rejectedModuleImport is not null;
    public IReadOnlyList<WorkspaceFunctionConflict> ImportFunctionConflicts
    {
        get
        {
            if (rejectedModuleImport is not { } import || ImportDestination is not { } destination) return [];
            var incoming = import.Module.DescendantsAndSelf().ToHashSet();
            return FindModuleFunctionConflicts(import.Module, destination).Select(conflict => incoming.Contains(conflict.Node)
                ? conflict with { SourceFolder = conflict.Node.GetFolderPath(import.SourceFolder) } : conflict).ToArray();
        }
    }
    public bool HasImportFunctionConflicts => ImportFunctionConflicts.Count > 0;
    public string ImportFunctionConflictSummary => rejectedModuleImport is not { } import ? ""
        : ImportDestination is not { } destination ? "The import destination is no longer in this workspace. Select a destination and add the module again."
        : HasImportFunctionConflicts
            ? $"Cannot import '{import.Module.Name}' under '{destination.Name}': a setup or results function would be defined more than once along a module path. Review the definitions below, then add the module again. Source-folder links open the original modules in File Explorer."
            : $"Function conflicts cleared. '{import.Module.Name}' has not been imported. Select '{destination.Name}' and add the module again to retry.";
    private void InitializeImportValidation()
    {
        ReviewImportFunctionConflictCommand = new RelayCommand(value =>
        {
            if (value is not WorkspaceFunctionConflict conflict) return;
            if (conflict.SourceFolder is null)
            {
                ReviewMoveFunctionConflictCommand.Execute(conflict);
                return;
            }
            try
            {
                if (!Directory.Exists(conflict.SourceFolder)) throw new DirectoryNotFoundException("The source module folder no longer exists.");
                if (openFolder is null) throw new InvalidOperationException("Opening source module folders is unavailable.");
                openFolder(conflict.SourceFolder);
                Error = "";
            }
            catch (Exception exception) when (IsFolderError(exception) || exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            { Error = $"Could not open source module folder '{conflict.SourceFolder}'. {exception.Message}"; }
        }, () => CanEdit);
    }
    private void NotifyImportValidation()
    {
        OnPropertyChanged(nameof(ImportFunctionConflicts)); OnPropertyChanged(nameof(HasImportFunctionConflicts));
        OnPropertyChanged(nameof(HasRejectedModuleImport)); OnPropertyChanged(nameof(ImportFunctionConflictSummary));
        OnPropertyChanged(nameof(Error));
        ReviewImportFunctionConflictCommand.RaiseCanExecuteChanged();
    }
}
