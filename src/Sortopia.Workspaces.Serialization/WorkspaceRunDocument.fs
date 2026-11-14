namespace Sortopia.Workspaces.Serialization

open System.IO
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces

/// Resolve a single saved run without parsing unrelated configuration or sibling runs.
module WorkspaceRunDocument =
    let rec private projects folder (modules: ModuleDocument array) = seq {
        for m in modules do
            let folder = Path.Combine(folder, m.Definition.folderName)
            for p in m.Projects do yield p.Definition.id, (p, Path.Combine(folder, p.Definition.folderName))
            yield! projects folder m.Modules }

    let resolve (value: WorkspaceDocument) (projectId: System.Guid) (runId: System.Guid) =
        let index = projects "" value.Modules |> Map.ofSeq
        let find id = index |> Map.tryFind id |> Option.defaultWith (fun () -> invalidOp $"Required project '{id}' was not found in the workspace.")
        let saved, folder = find projectId
        let requireSealed (p: ProjectDocument) =
            if not p.Definition.isSealed then invalidOp $"Source project '{p.Definition.projectName}' is a draft. Generate its run parameters and execute its results before using it as a source."
        // Resolve drafts into a runtime snapshot without changing the saved document.
        // The generation coordinator persists this snapshot immediately before writes.
        let parent = WorkspaceDefinitionCodec.projectHeader None saved.Definition |> project.seal
        let inputs = saved.Runs |> Array.tryFind (fun r -> r.id = runId) |> Option.defaultWith (fun () -> invalidOp "Save this run before generating its parameter files.")
        let selected = RunDto.toDomain parent inputs
        let sources = RunWorkspace.sourceIds selected |> List.map (fun id ->
            let source, folder = find (UMX.untag id)
            requireSealed source
            try
                { Snapshot = { ProjectId = id; ProjectName = UMX.tag source.Definition.projectName; IsSealed = true
                               Results = SettingsDto.resultsToDomain source.Definition.configuration }
                  RelativeFolder = folder }
            with error -> invalidOp $"Source project '{source.Definition.projectName}': {error.Message}")
        RunWorkspace.create parent selected ({ Snapshot = parent.Snapshot; RelativeFolder = folder } :: sources |> List.toArray)

    let resolveByRunId (value: WorkspaceDocument) (runId: System.Guid) =
        let projectId = projects "" value.Modules |> Seq.tryPick (fun (id, (project, _)) ->
            if project.Runs |> Array.exists (fun r -> r.id = runId) then Some id else None)
                        |> Option.defaultWith (fun () -> invalidOp "The saved run was not found in this workspace.")
        resolve value projectId runId
