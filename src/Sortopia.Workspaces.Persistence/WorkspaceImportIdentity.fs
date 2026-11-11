namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open Sortopia.Workspaces.Serialization

/// Allocate the entire imported subtree before remapping its internal project references.
type internal WorkspaceImportIdentity() =
    let replacements = Dictionary<Guid, Guid>()
    let replace id = match replacements.TryGetValue(id) with true, value -> value | _ -> id
    let allocate id =
        if not (replacements.TryAdd(id, Guid.NewGuid())) then invalidArg "id" $"Duplicate entity ID in import: {id}"
    let allocateProject (value: projectDto) =
        allocate value.id
        value.runs |> Array.iter (fun run -> allocate run.id)
    let rec allocateModule (value: moduleDto) =
        allocate value.id
        value.modules |> Array.iter allocateModule
        value.projects |> Array.iter allocateProject
    let settings entries =
        if isNull entries then null
        else entries |> Array.map (fun (entry: setupDataTypeConfigDto) -> { entry with sourceProjectId = replace entry.sourceProjectId })
    let sources entries =
        if isNull entries then null
        else entries |> Array.map (fun (entry: setupSourceConfigDto) -> { entry with sourceProjectId = replace entry.sourceProjectId })
    let rewriteProject (value: projectDto) =
        let id = replacements[value.id]
        { value with id = id
                     configuration = { value.configuration with setupDataTypes = settings value.configuration.setupDataTypes }
                     inheritedDefaults = { value.inheritedDefaults with setupDataTypes = settings value.inheritedDefaults.setupDataTypes }
                     localOverrides = { value.localOverrides with setupSources = sources value.localOverrides.setupSources }
                     runs = value.runs |> Array.map (fun run -> { run with id = replacements[run.id]; parentProjectId = id }) }
    let rec rewriteModule (value: moduleDto) =
        { value with id = replacements[value.id]
                     configuration = { value.configuration with setupDataTypes = settings value.configuration.setupDataTypes }
                     inheritedDefaults = { value.inheritedDefaults with setupDataTypes = settings value.inheritedDefaults.setupDataTypes }
                     localOverrides = { value.localOverrides with setupSources = sources value.localOverrides.setupSources }
                     modules = value.modules |> Array.map rewriteModule
                     projects = value.projects |> Array.map rewriteProject }
    member _.OriginalIds : IReadOnlyDictionary<Guid, Guid> =
        let ids = Dictionary<Guid, Guid>()
        for KeyValue(original, replacement) in replacements do ids.Add(replacement, original)
        ids
    member _.Reidentify(value: projectDto) = allocateProject value; rewriteProject value
    member _.Reidentify(value: moduleDto) = allocateModule value; rewriteModule value
