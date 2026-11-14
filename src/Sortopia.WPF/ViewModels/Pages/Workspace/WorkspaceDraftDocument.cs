using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceTreeNode
{
    internal string DraftSourceError => IsProject && !IsSealed && Children.Count > 0 ? SourceReadinessError([]) : "";
    private string SourceReadinessError(HashSet<Guid> visiting)
    {
        if (!visiting.Add(Id)) return "Source-project dependencies contain a cycle. Change the source project assignments before generating run parameters.";
        try
        {
            if (!IsBranch || IsSealed) return "";
            var root = this;
            while (root.Parent is not null) root = root.Parent;
            foreach (var entry in Editor!.ToProjectSettings().setupDataTypes.Where(entry => entry.sourceProjectId != Guid.Empty))
            {
                var source = root.DescendantsAndSelf().FirstOrDefault(node => node.IsProject && node.Id == entry.sourceProjectId);
                if (source is null) return "The source project is missing from this workspace. Choose an available source project.";
                if (source.IsSealed) continue;
                if (source.Children.Count == 0 || source.DescendantsAndSelf().Any(node => node.Editor?.ValidationIssues.Any() == true))
                    return $"Source project '{source.Name}' needs a complete run before this project can be sealed. You can save both projects as drafts and finish the source later.";
                var error = source.SourceReadinessError(visiting);
                if (error.Length > 0) return error;
            }
            return "";
        }
        finally { visiting.Remove(Id); }
    }
    internal static WorkspaceTreeNode FromDocument(WorkspaceDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, dto.workspaceName, dto.folderName, dto.id);
        foreach (var child in value.Modules) node.Children.Add(FromModuleDocument(child));
        node.RememberSavedChildren();
        return node;
    }

    private static WorkspaceTreeNode FromModuleDocument(ModuleDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, dto.moduleName, dto.folderName, dto.id, WorkspaceDefinitionCodec.kind(dto.moduleKind));
        node.Editor!.LoadModuleOverrides(dto.localOverrides);
        foreach (var child in value.Modules) node.Children.Add(FromModuleDocument(child));
        foreach (var child in value.Projects) node.Children.Add(FromProjectDocument(child));
        node.RememberSavedChildren();
        return node;
    }

    private static WorkspaceTreeNode FromProjectDocument(ProjectDocument value)
    {
        var dto = value.Definition;
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Project, dto.projectName, dto.folderName, dto.id, WorkspaceDefinitionCodec.kind(dto.projectKind));
        if (dto.isSealed) node.Editor!.LoadProject(dto.configuration);
        else node.Editor!.LoadOverrides(dto.localOverrides);
        foreach (var child in value.Runs)
        {
            var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, child.runName, id: child.id, family: node.Family);
            run.Editor!.LoadRun(child);
            run.savedParentProjectId = child.parentProjectId;
            node.Children.Add(run);
        }
        if (dto.isSealed)
        {
            node.sealedProject = WorkspaceDefinitionCodec.projectHeader(null!, dto);
            node.Editor!.Freeze(dto.configuration);
        }
        node.RememberSavedChildren();
        return node;
    }

    internal WorkspaceDocument ToDocument()
    {
        ValidateChildren();
        var modules = Children.Select(child => child.ToModuleDocument(1)).ToArray();
        return WorkspaceDocumentModule.validate(new(new workspaceDefinitionDto {
            schemaVersion = WorkspaceDefinitionCodec.schemaVersion, id = Id, workspaceName = Name, folderName = FolderName,
            modules = modules.Select(module => Reference(module.Definition.id, module.Definition.folderName)).ToArray()
        }, modules));
    }

    private static childFolderReferenceDto Reference(Guid id, string folder) => new() { id = id, folderName = folder };
    private inheritedFromDto? ParentOrigin => Parent is { IsWorkspace: false } ? new() { moduleId = Parent.Id, moduleName = Parent.Name } : null;
    private projectSettingsDto ParentSettings => Parent?.Editor?.ToProjectSettings() ?? SettingsDto.fromDomain(Defaults.configuration(IsBranch));

    private ModuleDocument ToModuleDocument(int depth)
    {
        CheckDepth(depth); ValidateChildren();
        var modules = Children.Where(child => child.Kind == WorkspaceEntityKind.Module).Select(child => child.ToModuleDocument(depth + 1)).ToArray();
        var projects = Children.Where(child => child.IsProject).Select(child => child.ToProjectDocument(depth + 1)).ToArray();
        return new(new moduleDefinitionDto {
            schemaVersion = WorkspaceDefinitionCodec.schemaVersion, id = Id, moduleName = Name, folderName = FolderName,
            moduleKind = IsBranch ? "branch" : "root", localOverrides = Editor!.ToModuleOverrides(),
            inheritedDefaults = ModuleSettingsDto.fromProjectSettings(ParentSettings), inheritedFrom = ParentOrigin,
            modules = modules.Select(module => Reference(module.Definition.id, module.Definition.folderName)).ToArray(),
            projects = projects.Select(project => Reference(project.Definition.id, project.Definition.folderName)).ToArray()
        }, modules, projects);
    }

    private ProjectDocument ToProjectDocument(int depth)
    {
        CheckDepth(depth); ValidateChildren();
        // Only a complete first run fixes its project's configuration. Draft runs
        // keep their inputs on disk without pretending to be executable snapshots.
        if (DraftSourceError.Length == 0 && !DescendantsAndSelf().Any(node => node.Editor?.ValidationIssues.Any() == true))
        {
            try { return WorkspaceDocumentModule.projectFromDomain(ParentOrigin, ToProject(depth)); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { }
        }
        var runs = Children.Select(child => {
            child.ValidateChildren();
            if (child.savedParentProjectId is { } parent && parent != Id) throw new ArgumentException("A saved run cannot change its parent project.");
            return child.Editor!.ToRunInputs(child.Id, child.Name, Id);
        }).ToArray();
        return new(new projectDefinitionDto {
            schemaVersion = WorkspaceDefinitionCodec.schemaVersion, id = Id, projectName = Name, folderName = FolderName,
            projectKind = IsBranch ? "branch" : "root", isSealed = IsSealed,
            configuration = IsSealed ? SettingsDto.fromDomain(sealedProject!.Configuration) : null,
            localOverrides = IsSealed ? null : Editor!.ToLocalOverrides(),
            inheritedDefaults = IsSealed ? null : ParentSettings, inheritedFrom = IsSealed ? null : ParentOrigin,
            runs = runs.Select(run => run.id).ToArray()
        }, runs);
    }
}
