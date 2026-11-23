namespace Sortopia.Workspaces

open System
open System.IO
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// Only the saved results contract and location are needed to read source data.
type runProjectLocation = { Snapshot: sourceProjectSnapshot; RelativeFolder: string }

type RunExecutionScope = private {
    selected: run
    locations: Map<Guid<projectId>, runProjectLocation>
} with
    member this.Run = this.selected
    member this.Parent = this.selected.Parent
    member this.ProjectLocations = this.locations |> Map.toArray |> Array.map snd
    member this.FindProject id = this.locations |> Map.tryFind id

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RunExecutionScope =
    let sourceIds (value: run) =
        match value.Setup with
        | None -> []
        | Some setup ->
            setup.Outputs
            |> List.filter (fun entry -> FunctionConfiguration.requiresSource setup.FunctionName entry.DataType)
            |> List.map _.SourceProjectId |> List.distinct

    let create (value: run) (locations: runProjectLocation array) =
        let parent = value.Parent
        let sources = sourceIds value
        if List.contains parent.Id sources then invalidArg "sourceProjectId" "A project cannot use itself as a source."
        let ids = parent.Id :: sources |> Set.ofList
        let locations = locations |> Array.filter (fun item -> Set.contains item.Snapshot.ProjectId ids)
        EntityId.unique "projects" (locations |> Seq.map (fun item -> UMX.untag item.Snapshot.ProjectId))
        for item in locations do
            if Path.IsPathRooted item.RelativeFolder then invalidArg "folder" "A project location must be relative to the workspace."
            item.RelativeFolder.Split([|'/'; '\\'|]) |> Array.iter (FolderName.validate >> ignore)
            ProjectConfiguration.validateResults item.Snapshot.Results
        let index = locations |> Seq.map (fun item -> item.Snapshot.ProjectId, item) |> Map.ofSeq
        for id in ids do
            if not (Map.containsKey id index) then invalidOp $"Required project '{id}' was not found in the workspace."
        if index[parent.Id].Snapshot <> parent.ResultsSnapshot then invalidArg "project" "The parent results snapshot does not match the saved project."
        let selected =
            match value.Setup with
            | None -> value
            | Some setup ->
                RunSources.resolveFor (FunctionConfiguration.requiresSource setup.FunctionName)
                    (fun id -> Map.tryFind id index |> Option.map _.Snapshot) value
        FunctionConfiguration.validateRunInputs selected
        { selected = selected; locations = index }
