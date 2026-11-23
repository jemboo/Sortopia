namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open Sortopia.Workspaces.Serialization

/// Allocate the imported subtree before remapping references, without parsing draft values.
type internal WorkspaceImportIdentity() =
    let replacements = Dictionary<Guid, Guid>()
    let replace id = match replacements.TryGetValue(id) with true, value -> value | _ -> id
    let allocate id =
        if not (replacements.TryAdd(id, Guid.NewGuid())) then invalidArg "id" $"Duplicate entity ID in import: {id}"
    let allocateProject (value: ProjectDocument) =
        allocate value.Definition.id
        value.Runs |> Array.iter (fun run -> allocate run.id)
    let rec allocateModule (value: ModuleDocument) =
        allocate value.Definition.id
        value.Modules |> Array.iter allocateModule
        value.Projects |> Array.iter allocateProject
    let mapNullable map value = if isNull (box value) then value else map value
    let origin (value: inheritedFromDto) = mapNullable (fun value -> { value with moduleId = replace value.moduleId }) value
    let sources entries =
        if isNull entries then null
        else entries |> Array.map (fun (entry: setupSourceConfigDto) -> { entry with sourceProjectId = replace entry.sourceProjectId })
    let settings (value: projectSettingsDto) = mapNullable (fun value ->
        { value with setupDataTypes = value.setupDataTypes |> mapNullable (Array.map (fun entry -> { entry with sourceProjectId = replace entry.sourceProjectId })) }) value
    let rewriteProject (value: ProjectDocument) =
        let dto = value.Definition
        let id = replacements[dto.id]
        { Definition = { dto with id = id
                                  state = ProjectLifecycle.map settings (fun local -> { local with setupSources = sources local.setupSources }) origin dto.state }
          Runs = value.Runs |> Array.map (fun run -> { run with id = replacements[run.id]; parentProjectId = id }) }
    let rec rewriteModule (value: ModuleDocument) =
        let dto = value.Definition
        { Definition = { dto with id = replacements[dto.id]; inheritedFrom = origin dto.inheritedFrom
                                  inheritedDefaults = { dto.inheritedDefaults with setupSources = sources dto.inheritedDefaults.setupSources }
                                  localOverrides = { dto.localOverrides with setupSources = sources dto.localOverrides.setupSources } }
          Modules = value.Modules |> Array.map rewriteModule
          Projects = value.Projects |> Array.map rewriteProject }
    member _.OriginalIds : IReadOnlyDictionary<Guid, Guid> =
        let ids = Dictionary<Guid, Guid>()
        for KeyValue(original, replacement) in replacements do ids.Add(replacement, original)
        ids
    member _.Reidentify(value: ProjectDocument) =
        WorkspaceDocument.validateProject value |> ignore
        allocateProject value
        rewriteProject value |> WorkspaceDocument.validateProject
    member _.Reidentify(value: ModuleDocument) =
        WorkspaceDocument.validateModule value |> ignore
        allocateModule value
        rewriteModule value |> WorkspaceDocument.validateModule
