namespace Sortopia.Workspaces

open System
open System.IO
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// Only the saved results contract and location are needed to read source data.
type runProjectLocation = { Snapshot: sourceProjectSnapshot; RelativeFolder: string }

type runWorkspace = private {
    selected: run
    parent: project
    locations: Map<Guid<projectId>, runProjectLocation>
} with
    member this.Run = this.selected
    member this.Parent = this.parent
    member this.ProjectLocations = this.locations |> Map.toArray |> Array.map snd
    member this.FindProject id = this.locations |> Map.tryFind id

module RunWorkspace =
    let sourceIds (value: run) =
        match value with
        | run.RootRun _ -> []
        | run.BranchRun branch ->
            branch.SetupDataTypes
            |> List.filter (fun entry -> FunctionConfiguration.requiresSource branch.SetupFunctionName entry.DataType)
            |> List.map _.SourceProjectId |> List.distinct

    let create (parent: project) (value: run) (locations: runProjectLocation array) =
        if not parent.IsSealed then invalidOp $"Project '{parent.ProjectName}' is a draft. Complete and save its configuration and run first."
        if value.ParentProjectId <> parent.Id then invalidArg "run" "The run belongs to a different project."
        let ids = parent.Id :: sourceIds value |> Set.ofList
        let locations = locations |> Array.filter (fun item -> Set.contains item.Snapshot.ProjectId ids)
        EntityId.unique "projects" (locations |> Seq.map (fun item -> UMX.untag item.Snapshot.ProjectId))
        for item in locations do
            if Path.IsPathRooted item.RelativeFolder then invalidArg "folder" "A project location must be relative to the workspace."
            item.RelativeFolder.Split([|'/'; '\\'|]) |> Array.iter (FolderName.validate >> ignore)
            if not item.Snapshot.IsSealed then invalidOp $"Source project '{item.Snapshot.ProjectName}' must be sealed."
            ProjectConfiguration.validateRoot { Description = ""; Results = item.Snapshot.Results }
        let index = locations |> Seq.map (fun item -> item.Snapshot.ProjectId, item) |> Map.ofSeq
        for id in ids do
            if not (Map.containsKey id index) then invalidOp $"Required project '{id}' was not found in the workspace."
        if index[parent.Id].Snapshot <> parent.Snapshot then invalidArg "project" "The parent results snapshot does not match the saved project."
        let selected =
            match value with
            | run.RootRun _ -> value
            | run.BranchRun branch ->
                BranchRunSources.resolveFor (FunctionConfiguration.requiresSource branch.SetupFunctionName)
                    (fun id -> Map.tryFind id index |> Option.map _.Snapshot) branch |> run.BranchRun
        FunctionConfiguration.validateRunInputs selected
        { selected = selected; parent = project.withRuns parent [|selected|]; locations = index }

    /// Existing callers can resolve the same scope from a validated workspace.
    let fromWorkspace (world: workspace) (value: run) =
        let parent = world.FindProject value.ParentProjectId |> Option.defaultWith (fun () -> invalidOp "The run's parent project was not found.")
        if not (parent.Runs |> Array.exists ((=) value)) then invalidOp "Execute requires a saved run from the supplied workspace."
        let needed = parent.Id :: sourceIds value |> Set.ofList
        let rec visit folder (modules: workspaceModule array) = seq {
            for m in modules do
                let folder = Path.Combine(folder, m.FolderName)
                for p in m.Projects do
                    if Set.contains p.Id needed then yield { Snapshot = p.Snapshot; RelativeFolder = Path.Combine(folder, p.FolderName) }
                yield! visit folder m.Modules }
        create parent value (visit "" world.Modules |> Seq.toArray)
