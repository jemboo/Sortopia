namespace Sortopia.Workspaces.Serialization

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces

/// Resolve a single saved run without parsing unrelated configuration or sibling runs.
module WorkspaceRunDocument =
    let private validateFunctions (path: ModuleDefinition list) (project: ProjectDefinition) =
        // Existing generated work uses its sealed snapshot, independently of later module edits.
        if not project.isSealed then
            let requireOne phase select =
                let names = path |> List.filter (fun m -> not (System.String.IsNullOrWhiteSpace(select m.localOverrides))) |> List.map _.moduleName
                if names.Length <> 1 then
                    let definitions = System.String.Join(", ", names)
                    let detail = if names.IsEmpty then "No module defines it." else $"It is defined by: {definitions}."
                    invalidOp $"Project '{project.projectName}' needs exactly one {phase}-function definition along its module path. {detail}"
            requireOne "results" (fun local -> local.resultsFunctionName)
            if project.projectKind = "branch" then requireOne "setup" (fun local -> local.setupFunctionName)

    let private resolveFrom (index: WorkspaceDocumentIndex) (projectId: System.Guid) (runId: System.Guid) =
        let find id = index.TryFindProject id |> Option.defaultWith (fun () -> invalidOp $"Required project '{id}' was not found in the workspace.")
        let entry = find projectId
        let saved, folder = entry.Document, entry.RelativeFolder
        validateFunctions (Array.toList entry.ModulePath) saved.Definition
        let requireSealed (p: ProjectDocument) =
            if not p.Definition.isSealed then invalidOp $"Source project '{p.Definition.projectName}' is a draft. Generate its run parameters and execute its results before using it as a source."
        // Resolve drafts into a runtime snapshot without changing the saved document.
        // The generation coordinator persists this snapshot immediately before writes.
        let parent = WorkspaceDefinitionResolution.projectSnapshot saved.Definition
        let inputs = saved.Runs |> Array.tryFind (fun r -> r.id = runId) |> Option.defaultWith (fun () -> invalidOp "Save this run before generating its parameter files.")
        let selected = RunDto.toDomain parent inputs
        let sources = RunExecutionScope.sourceIds selected |> List.map (fun id ->
            let entry = find (UMX.untag id)
            let source, folder = entry.Document, entry.RelativeFolder
            requireSealed source
            try
                { Snapshot = { ProjectId = id; ProjectName = UMX.tag source.Definition.projectName
                               Results = SettingsDto.resultsToDomain source.Definition.configuration }
                  RelativeFolder = folder }
            with error -> invalidOp $"Source project '{source.Definition.projectName}': {error.Message}")
        RunExecutionScope.create selected ({ Snapshot = parent.ResultsSnapshot; RelativeFolder = folder } :: sources |> List.toArray)

    let resolve (value: WorkspaceDocument) projectId runId = resolveFrom (WorkspaceDocumentIndex value) projectId runId

    let resolveByRunId (value: WorkspaceDocument) (runId: System.Guid) =
        let index = WorkspaceDocumentIndex value
        let run = index.TryFindRun runId |> Option.defaultWith (fun () -> invalidOp "The saved run was not found in this workspace.")
        resolveFrom index run.Parent.Document.Definition.id runId
