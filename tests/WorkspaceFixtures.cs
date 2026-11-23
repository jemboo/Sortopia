global using Sortopia.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.FSharp.Core;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.Tests;

// Fixtures own documents only. Typed settings and runs are explicit, temporary projections.
internal static class WorkspaceFixtures
{
    internal static hierarchyKind Family(this ProjectDocument value) => WorkspaceDefinitionCodec.kind(value.Definition.projectKind);
    internal static hierarchyKind Family(this ModuleDocument value) => WorkspaceDefinitionCodec.kind(value.Definition.moduleKind);
    internal static projectConfiguration Configuration(this ProjectDocument value) => WorkspaceDefinitionResolution.projectConfiguration(null!, value.Definition);
    internal static moduleConfiguration Configuration(this ModuleDocument value) => WorkspaceDefinitionResolution.moduleConfiguration(null!, value.Definition);
    internal static projectSnapshot Snapshot(this ProjectDocument value) => WorkspaceDefinitionResolution.projectSnapshot(value.Definition);
    internal static sourceProjectSnapshot ResultsSnapshot(this ProjectDocument value) => new(value.Definition.id, value.Definition.projectName, value.Configuration().Results);
    internal static projectLocalSettings LocalSettings(this ProjectDocument value) => value.Definition.localSettings is { } local
        ? ProjectLocalSettingsDto.toDomain(value.Configuration(), local) : ProjectResolution.local(value.Configuration());
    internal static projectConfiguration InheritedDefaults(this ProjectDocument value) => value.Definition.inheritedDefaults is { } inherited
        ? SettingsDto.toDomain(value.Family(), inherited) : value.Configuration();
    internal static moduleConfiguration InheritedDefaults(this ModuleDocument value) => ModuleSettingsDto.toDomain(value.Family(), value.Definition.inheritedDefaults);
    internal static moduleOverrides LocalOverrides(this ModuleDocument value) => ModuleOverridesDto.toDomain(value.Family(), value.Definition.localOverrides);
    internal static run[] ResolvedRuns(this ProjectDocument value) => value.Runs.Select(input => RunDto.toDomain(value.Snapshot(), input)).ToArray();
    internal static Dictionary<Guid, ProjectDocument> ProjectIndex(this WorkspaceDocument value) => new WorkspaceDocumentIndex(value).Projects.ToDictionary(entry => entry.Document.Definition.id, entry => entry.Document);

    internal static ProjectDocument Project(Guid id, string name, projectConfiguration configuration, run[] runs) =>
        runs.Length == 0 ? DraftProject(id, name, configuration, ProjectResolution.local(configuration), runs)
            : RestoreProject(id, name, configuration, ProjectResolution.local(configuration), true, runs);

    internal static ProjectDocument DraftProject(Guid id, string name, projectConfiguration inherited, projectLocalSettings local, run[] runs)
    {
        var value = new ProjectDocument(new ProjectDefinition {
            id = id, projectName = name,
            projectKind = inherited.HasSetup ? "branch" : "root",
            state = projectStateDto.NewDraft(ProjectLocalSettingsDto.fromDomain(local), SettingsDto.fromDomain(inherited), null!)
        }, []);
        _ = value.Configuration();
        return runs.Length == 0 ? WorkspaceDocumentModule.validateProject(value) : WithRuns(value, runs);
    }

    internal static ProjectDocument RestoreProject(Guid id, string name, projectConfiguration configuration, projectLocalSettings local, bool sealedProject, run[] runs)
    {
        if (configuration.HasSetup != (local.Setup is not null)) throw new ArgumentException("Project settings must match the setup phase.", "configuration");
        if (!sealedProject) return DraftProject(id, name, configuration, local, runs);
        var value = new ProjectDocument(new ProjectDefinition {
            id = id, projectName = name,
            projectKind = configuration.HasSetup ? "branch" : "root",
            state = projectStateDto.NewSealed(SettingsDto.fromDomain(configuration), null)
        }, []);
        _ = value.Configuration();
        return WithRuns(value, runs);
    }

    internal static ProjectDocument WithRuns(ProjectDocument value, run[] runs)
    {
        var configuration = value.Configuration();
        foreach (var run in runs)
        {
            if (run.ParentProjectId != value.Definition.id) throw new ArgumentException("A run must reference its containing project ID.", "runs");
            if (!Equals(run.ProjectConfiguration, configuration)) throw new ArgumentException("Existing runs require their original project settings.", "configuration");
        }
        var result = WorkspaceDocumentModule.cloneProject(value);
        if (runs.Length > 0 && !result.Definition.isSealed)
            result.Definition.state = projectStateDto.NewSealed(SettingsDto.fromDomain(configuration), null);
        return WorkspaceDocumentModule.validateProject(WorkspaceDocumentModule.withRuns(result, runs.Select(RunDto.fromDomain).ToArray()));
    }

    internal static ProjectDocument Seal(ProjectDocument value)
    {
        var result = WorkspaceDocumentModule.cloneProject(value);
        // An explicit fixture snapshot has no saved editor, matching programmatic snapshots.
        result.Definition.state = projectStateDto.NewSealed(SettingsDto.fromDomain(value.Configuration()), null);
        return result;
    }

    internal static ProjectDocument RebindProject(projectConfiguration inherited, ProjectDocument value)
    {
        if (inherited.HasSetup != (value.Family() == hierarchyKind.Branch)) throw new ArgumentException("Project setup phase must match its parent.", "parent");
        if (value.Definition.isSealed) return value;
        var result = WorkspaceDocumentModule.cloneProject(value);
        result.Definition.state = projectStateDto.NewDraft(result.Definition.localSettings, SettingsDto.fromDomain(inherited), result.Definition.inheritedFrom);
        _ = result.Configuration();
        return result;
    }

    internal static ModuleDocument Module(hierarchyKind kind, Guid id, string name, ModuleDocument[] modules, ProjectDocument[] projects) =>
        InheritedModule(id, name, ModuleDefaults.configuration(kind == hierarchyKind.Branch), ModuleDefaults.inherited(kind == hierarchyKind.Branch), modules, projects);

    internal static ModuleDocument InheritedModule(Guid id, string name, moduleConfiguration inherited, moduleOverrides local, ModuleDocument[] modules, ProjectDocument[] projects)
    {
        var configuration = ModuleDefaults.resolve(inherited, local);
        var value = new ModuleDocument(new ModuleDefinition {
            id = id, moduleName = name,
            moduleKind = configuration.HasSetup ? "branch" : "root",
            localOverrides = ModuleOverridesDto.fromDomain(local), inheritedDefaults = ModuleSettingsDto.fromDomain(inherited),
            inheritedFrom = null!
        }, [], []);
        return WorkspaceDocumentModule.validateModule(WorkspaceDocumentModule.withModuleChildren(value,
            modules.Select(child => RebindModule(configuration, child)).ToArray(),
            projects.Select(child => RebindProject(ModuleDefaults.toProject(configuration), child)).ToArray()));
    }

    internal static ModuleDocument RebindModule(moduleConfiguration inherited, ModuleDocument value)
    {
        if (inherited.HasSetup != (value.Family() == hierarchyKind.Branch)) throw new ArgumentException("Module setup phase must match its parent.", "parent");
        return InheritedModule(value.Definition.id, value.Definition.moduleName, inherited, value.LocalOverrides(), value.Modules, value.Projects);
    }

    internal static WorkspaceDocument Workspace(Guid id, string name, string folder, ModuleDocument[] modules)
    {
        var value = new WorkspaceDocument(new WorkspaceDefinition {
            id = id, workspaceName = name, folderName = folder
        }, []);
        return WorkspaceDocumentModule.validate(WorkspaceDocumentModule.withOrigins(WorkspaceDocumentModule.withModules(value,
            modules.Select(module => RebindModule(ModuleDefaults.configuration(module.Family() == hierarchyKind.Branch), module)).ToArray())));
    }
}
