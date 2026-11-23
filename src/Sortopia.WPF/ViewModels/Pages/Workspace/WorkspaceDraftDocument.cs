using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceTreeNode
{
    internal string DraftSourceError => IsProject && !IsSealed && Children.Count > 0 ? SourceReadinessError() : "";
    private string SourceReadinessError()
    {
        if (!IsBranch || IsSealed) return "";
        var root = this;
        while (root.Parent is not null) root = root.Parent;
        foreach (var entry in Editor!.ToProjectSettings().setupDataTypes.Where(entry => entry.sourceProjectId != Guid.Empty))
        {
            var source = root.DescendantsAndSelf().FirstOrDefault(node => node.IsProject && node.Id == entry.sourceProjectId);
            if (source is null) return "The source project is missing from this workspace. Choose an available source project.";
            if (source.IsSealed) continue;
            return $"Source project '{source.Name}' is a draft. Generate its run parameters and execute its results before using it as a source. You can save this project and finish the source later.";
        }
        return "";
    }
    internal static WorkspaceTreeNode FromDocument(WorkspaceDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, dto.workspaceName, dto.id, workspaceFolderName: dto.folderName);
        foreach (var child in value.Modules) node.Children.Add(FromModuleDocument(child));
        node.RememberSavedChildren();
        return node;
    }

    internal static WorkspaceTreeNode FromModuleDocument(ModuleDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, dto.moduleName, dto.id, WorkspaceDefinitionCodec.kind(dto.moduleKind));
        node.Editor!.LoadModuleOverrides(dto.localOverrides);
        foreach (var child in value.Modules) node.Children.Add(FromModuleDocument(child));
        foreach (var child in value.Projects) node.Children.Add(FromProjectDocument(child));
        node.RememberSavedChildren();
        return node;
    }

    internal static WorkspaceTreeNode FromProjectDocument(ProjectDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Project, dto.projectName, dto.id, WorkspaceDefinitionCodec.kind(dto.projectKind));
        if (dto.isSealed) _ = WorkspaceDefinitionResolution.projectSnapshot(dto);
        node.Editor!.ApplyProjectState(dto.state);
        foreach (var child in value.Runs)
        {
            var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, child.runName, id: child.id, family: node.Family);
            run.Editor!.LoadRun(child);
            run.savedParentProjectId = child.parentProjectId;
            node.Children.Add(run);
        }
        node.RememberSavedChildren();
        return node;
    }

    internal WorkspaceDocument ToDocument()
    {
        ValidateChildren();
        var modules = Children.Select(child => child.ToModuleDocument(1)).ToArray();
        return WorkspaceDocumentModule.validate(new(new WorkspaceDefinition {
            id = Id, workspaceName = Name, folderName = workspaceFolderName
        }, modules));
    }

    private inheritedFromDto? ParentOrigin => Parent is { IsWorkspace: false } ? new() { moduleId = Parent.Id, moduleName = Parent.Name } : null;
    private projectSettingsDto ParentSettings => Parent?.Editor?.ToProjectSettings() ?? SettingsDto.fromDomain(ProjectResolution.configuration(IsBranch));
    private moduleSettingsDto ParentModuleSettings => Parent?.Editor?.ToModuleSettings() ?? ModuleSettingsDto.fromDomain(ModuleDefaults.configuration(IsBranch));

    private ModuleDocument ToModuleDocument(int depth)
    {
        CheckDepth(depth); ValidateChildren();
        var modules = Children.Where(child => child.Kind == WorkspaceEntityKind.Module).Select(child => child.ToModuleDocument(depth + 1)).ToArray();
        var projects = Children.Where(child => child.IsProject).Select(child => child.ToProjectDocument(depth + 1)).ToArray();
        return new(new ModuleDefinition {
            id = Id, moduleName = Name,
            moduleKind = IsBranch ? "branch" : "root", localOverrides = Editor!.ToModuleOverrides(),
            inheritedDefaults = ParentModuleSettings, inheritedFrom = ParentOrigin
        }, modules, projects);
    }

    private ProjectDocument ToProjectDocument(int depth)
    {
        CheckDepth(depth); ValidateChildren();
        // Saving preserves editable project settings. Generation seals the snapshot.
        var runs = Children.Select(child => {
            child.ValidateChildren();
            if (child.savedParentProjectId is { } parent && parent != Id) throw new ArgumentException("A saved run cannot change its parent project.");
            return child.Editor!.ToRunInputs(child.Id, child.Name, Id);
        }).ToArray();
        return new(new ProjectDefinition {
            id = Id, projectName = Name,
            projectKind = IsBranch ? "branch" : "root", state = Editor!.ToProjectState(ParentSettings, ParentOrigin)
        }, runs);
    }
}
