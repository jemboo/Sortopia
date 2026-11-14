namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

/// Editable documents preserve unfinished values. Runtime conversion remains strict.
type ProjectDocument = { Definition: projectDefinitionDto; Runs: runDto array }
type ModuleDocument = { Definition: moduleDefinitionDto; Modules: ModuleDocument array; Projects: ProjectDocument array }
type WorkspaceDocument = { Definition: workspaceDefinitionDto; Modules: ModuleDocument array }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WorkspaceDocument =
    let rec moduleFromDomain origin (value: workspaceModule) : ModuleDocument =
        let own = { moduleId = UMX.untag value.Id; moduleName = string value.ModuleName }
        { Definition = WorkspaceDefinitionCodec.moduleWithOrigin origin value
          Modules = value.Modules |> Array.map (moduleFromDomain own)
          Projects = value.Projects |> Array.map (projectFromDomain own) }
    and projectFromDomain origin (value: project) : ProjectDocument =
        { Definition = WorkspaceDefinitionCodec.projectWithOrigin origin value; Runs = value.Runs |> Array.map RunDto.fromDomain }
    let fromDomain (value: workspace) : WorkspaceDocument =
        { Definition = WorkspaceDefinitionCodec.workspace value; Modules = value.Modules |> Array.map (moduleFromDomain Unchecked.defaultof<_>) }

    let withOrigins (value: WorkspaceDocument) =
        let rec update origin (value: ModuleDocument) =
            let own = { moduleId = value.Definition.id; moduleName = value.Definition.moduleName }
            { Definition = { value.Definition with inheritedFrom = origin }
              Modules = value.Modules |> Array.map (update own)
              Projects = value.Projects |> Array.map (fun p -> { p with Definition = { p.Definition with inheritedFrom = if p.Definition.isSealed then Unchecked.defaultof<_> else own } }) }
        { value with Modules = value.Modules |> Array.map (update Unchecked.defaultof<_>) }

    let projectToDomain parent (value: ProjectDocument) =
        if not value.Definition.isSealed && value.Runs.Length > 0 then
            invalidOp $"Project '{value.Definition.projectName}' is a saved draft. Complete its configuration and run inputs before generating run parameters."
        let header = WorkspaceDefinitionCodec.projectHeader parent value.Definition
        project.withRuns header (value.Runs |> Array.map (RunDto.toDomain header))
    let rec moduleToDomain parent (value: ModuleDocument) =
        let header = WorkspaceDefinitionCodec.moduleHeader parent value.Definition
        workspaceModule.createInherited header.Id header.ModuleName header.FolderName header.InheritedDefaults header.LocalOverrides
            (value.Modules |> Array.map (moduleToDomain (Some header.Configuration)))
            (value.Projects |> Array.map (projectToDomain (Some (ModuleDefaults.toProject header.Configuration))))
    let toDomain (value: WorkspaceDocument) =
        let modules = value.Modules |> Array.map (fun child ->
            let defaults = ModuleDefaults.configuration (child.Definition.moduleKind = "branch")
            moduleToDomain (Some defaults) child)
        workspace.create (UMX.tag value.Definition.id) (UMX.tag value.Definition.workspaceName) value.Definition.folderName modules

    /// Validate structure and references without parsing editable configuration values.
    let validate (value: WorkspaceDocument) =
        let uniqueNames label names =
            let seen = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            for name in names do
                EntityName.validate name |> ignore
                if not (seen.Add name) then invalidArg label "Duplicate entity name."
        let ids = Collections.Generic.HashSet<struct (string * Guid)>()
        let identity kind id = if not (ids.Add(struct (kind, id))) then invalidArg "id" $"Duplicate {kind} ID: {id}."
        let children (refs: childFolderReferenceDto array) (actual: (Guid * string) array) =
            if (refs |> Array.map (fun r -> r.id, r.folderName)) <> actual then invalidArg "children" "Child references do not match their documents."
        let depth value = if value > 64 then invalidArg "hierarchy" "Workspace nesting must not exceed 64 levels."
        let project family (value: ProjectDocument) =
            let dto = MessagePackSetup.serialize value.Definition |> WorkspaceDefinitionCodec.decodeProject
            identity "project" dto.id
            if dto.projectKind <> family then invalidArg "projectKind" "A module can contain projects only of its own kind."
            if dto.runs <> (value.Runs |> Array.map _.id) then invalidArg "runs" "Run references do not match their documents."
            uniqueNames "runs" (value.Runs |> Seq.map _.runName)
            for run in value.Runs do
                RunDto.readId run |> ignore
                identity "run" run.id
                if run.parentProjectId <> dto.id || run.runKind <> family then invalidArg "run" "A run must belong to its containing project and kind."
                if isNull run.resultsParameterSpans || isNull run.setupParameterSpans then invalidArg "spans" "Run span collections are missing."
        let rec modules family level (value: ModuleDocument) =
            depth level
            let dto = MessagePackSetup.serialize value.Definition |> WorkspaceDefinitionCodec.decodeModule
            identity "module" dto.id
            if family |> Option.exists ((<>) dto.moduleKind) then invalidArg "moduleKind" "A module can contain modules only of its own kind."
            children dto.modules (value.Modules |> Array.map (fun m -> m.Definition.id, m.Definition.folderName))
            children dto.projects (value.Projects |> Array.map (fun p -> p.Definition.id, p.Definition.folderName))
            uniqueNames "modules" (value.Modules |> Seq.map _.Definition.moduleName)
            uniqueNames "projects" (value.Projects |> Seq.map _.Definition.projectName)
            for child in value.Modules do modules (Some dto.moduleKind) (level + 1) child
            for child in value.Projects do depth (level + 1); project dto.moduleKind child
        let dto = MessagePackSetup.serialize value.Definition |> WorkspaceDefinitionCodec.decodeWorkspace
        children dto.modules (value.Modules |> Array.map (fun m -> m.Definition.id, m.Definition.folderName))
        uniqueNames "modules" (value.Modules |> Seq.map _.Definition.moduleName)
        for child in value.Modules do modules None 1 child
        value
