namespace Sortopia.Workspaces.Serialization

open System
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

/// Editable documents preserve unfinished values; execution resolves only the selected run.
type ProjectDocument = { Definition: ProjectDefinition; Runs: runDto array }
type ModuleDocument = { Definition: ModuleDefinition; Modules: ModuleDocument array; Projects: ProjectDocument array }
type WorkspaceDocument = { Definition: WorkspaceDefinition; Modules: ModuleDocument array }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WorkspaceDocument =
    /// Child collections are the sole membership and ordering authority in memory.
    let withRuns (value: ProjectDocument) runs = { value with Runs = runs }
    let withModuleChildren (value: ModuleDocument) modules projects =
        { value with Modules = modules; Projects = projects }
    let withModules (value: WorkspaceDocument) modules = { value with Modules = modules }

    /// Shallow file references are derived only at the persistence boundary.
    let workspaceFile (value: WorkspaceDocument) : workspaceDefinitionDto =
        { schemaVersion = WorkspaceDefinitionCodec.schemaVersion; id = value.Definition.id
          workspaceName = value.Definition.workspaceName; folderName = value.Definition.folderName
          modules = value.Modules |> Array.map (fun m -> { id = m.Definition.id; name = m.Definition.moduleName }) }
    let moduleFile (value: ModuleDocument) : moduleDefinitionDto =
        { schemaVersion = WorkspaceDefinitionCodec.schemaVersion; id = value.Definition.id
          moduleKind = value.Definition.moduleKind; moduleName = value.Definition.moduleName
          localOverrides = value.Definition.localOverrides; inheritedDefaults = value.Definition.inheritedDefaults; inheritedFrom = value.Definition.inheritedFrom
          modules = value.Modules |> Array.map (fun m -> { id = m.Definition.id; name = m.Definition.moduleName })
          projects = value.Projects |> Array.map (fun p -> { id = p.Definition.id; name = p.Definition.projectName }) }
    let projectFile (value: ProjectDocument) : projectDefinitionDto =
        { schemaVersion = WorkspaceDefinitionCodec.schemaVersion; id = value.Definition.id
          projectKind = value.Definition.projectKind; projectName = value.Definition.projectName; state = value.Definition.state
          runs = value.Runs |> Array.map _.id }

    let withOrigins (value: WorkspaceDocument) =
        let rec update origin (value: ModuleDocument) =
            let own = { moduleId = value.Definition.id; moduleName = value.Definition.moduleName }
            { Definition = { value.Definition with inheritedFrom = origin }
              Modules = value.Modules |> Array.map (update own)
              Projects = value.Projects |> Array.map (fun p -> { p with Definition = { p.Definition with state = ProjectLifecycle.withOrigin own p.Definition.state } }) }
        { value with Modules = value.Modules |> Array.map (update Unchecked.defaultof<_>) }

    /// Validate structure and references without parsing editable configuration values.
    let private validation () =
        let uniqueNames label names =
            let seen = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            for name in names do
                EntityName.validate name |> ignore
                if not (seen.Add name) then invalidArg label $"Duplicate name in {label}: {name}"
        let ids = Collections.Generic.HashSet<struct (string * Guid)>()
        let identity kind id = if not (ids.Add(struct (kind, id))) then invalidArg "id" $"Duplicate {kind} ID: {id}."
        let depth value = if value > 64 then invalidArg "hierarchy" "Workspace nesting must not exceed 64 levels."
        let project family (value: ProjectDocument) =
            let dto = WorkspaceDefinitionValidation.project value.Definition
            identity "project" dto.id
            if dto.projectKind <> family then invalidArg "projectKind" "A module can contain projects only of its own kind."
            uniqueNames "runs" (value.Runs |> Seq.map _.runName)
            for run in value.Runs do
                RunDto.readId run |> ignore
                identity "run" run.id
                if run.parentProjectId <> dto.id || run.runKind <> family then invalidArg "run" "A run must belong to its containing project and kind."
                if isNull run.resultsParameterSpans || isNull run.setupParameterSpans then invalidArg "spans" "Run span collections are missing."
        let rec modules family level (value: ModuleDocument) =
            depth level
            let dto = WorkspaceDefinitionValidation.moduleDefinition value.Definition
            identity "module" dto.id
            if family |> Option.exists ((<>) dto.moduleKind) then invalidArg "moduleKind" "A module can contain modules only of its own kind."
            FolderName.unique "module and project children" (Seq.append (value.Modules |> Seq.map _.Definition.moduleName) (value.Projects |> Seq.map _.Definition.projectName))
            uniqueNames "modules" (value.Modules |> Seq.map _.Definition.moduleName)
            uniqueNames "projects" (value.Projects |> Seq.map _.Definition.projectName)
            for child in value.Modules do modules (Some dto.moduleKind) (level + 1) child
            for child in value.Projects do depth (level + 1); project dto.moduleKind child
        let workspace (value: WorkspaceDocument) =
            WorkspaceDefinitionValidation.workspace value.Definition |> ignore
            uniqueNames "modules" (value.Modules |> Seq.map _.Definition.moduleName)
            for child in value.Modules do modules None 1 child
        workspace, modules, project

    let validate (value: WorkspaceDocument) =
        let workspace, _, _ = validation ()
        workspace value
        value
    let validateModule (value: ModuleDocument) =
        let _, modules, _ = validation ()
        modules None 0 value
        value
    let validateProject (value: ProjectDocument) =
        let _, _, project = validation ()
        project value.Definition.projectKind value
        value

    /// Deep copies preserve raw draft text and the editing settings retained by sealed projects.
    let clone (value: WorkspaceDocument) = MessagePackSetup.serialize value |> MessagePackSetup.deserialize<WorkspaceDocument>
    let cloneModule (value: ModuleDocument) = MessagePackSetup.serialize value |> MessagePackSetup.deserialize<ModuleDocument>
    let cloneProject (value: ProjectDocument) = MessagePackSetup.serialize value |> MessagePackSetup.deserialize<ProjectDocument>
    // Nullable DTO fields describe absent editing/snapshot state. Compare their
    // complete stored content, including raw values hidden by inherited settings.
    let sameContent (left: WorkspaceDocument) (right: WorkspaceDocument) = MessagePackSetup.serialize left = MessagePackSetup.serialize right
    let sameModuleContent (left: ModuleDocument) (right: ModuleDocument) = MessagePackSetup.serialize left = MessagePackSetup.serialize right
    let sameProjectContent (left: ProjectDocument) (right: ProjectDocument) = MessagePackSetup.serialize left = MessagePackSetup.serialize right
