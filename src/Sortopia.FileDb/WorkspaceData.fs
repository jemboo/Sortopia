namespace Sortopia.FileDb

open System
open System.IO
open System.Threading
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces

module WorkspaceData =
    /// Resolve identity using the workspace API; use the hierarchy only for its path.
    let projectFolder (workspace: workspace) workspaceRoot projectId =
        let project = workspace.FindProject projectId
                      |> Option.defaultWith (fun () -> invalidOp $"Project '{projectId}' was not found in the workspace.")
        let rec find parent (items: workspaceModule array) =
            items |> Array.tryPick (fun item ->
                let folder = Path.Combine(parent, item.FolderName)
                if item.Projects |> Array.exists (fun value -> value.Id = project.Id) then
                    Some (Path.Combine(folder, project.FolderName))
                else find folder item.Modules)
        find (Path.GetFullPath workspaceRoot) workspace.Modules |> Option.defaultWith (fun () -> invalidOp "Project folder was not found.")

    let openProject workspace workspaceRoot projectId =
        SortopiaFileDb(projectFolder workspace workspaceRoot projectId, projectId) :> ISortopiaDb

    /// Resolve upstream results using the producer's configuration, not the
    /// consumer's setup projection. Multiple dependency variants are ambiguous.
    let loadSource (workspace: workspace) workspaceRoot (entry: setupDataTypeConfig) parameters token =
        let source = workspace.FindProject entry.SourceProjectId
                     |> Option.defaultWith (fun () -> invalidOp $"Source project '{entry.SourceProjectId}' was not found.")
        if not source.IsSealed then invalidOp $"Source project '{source.ProjectName}' must be sealed."
        if not (List.contains entry.DataType source.ResultsSortopiaDataTypes) then
            invalidOp $"Source project '{source.ProjectName}' does not provide '{entry.DataType}'."
        let settings = source.Snapshot.Results
        let version = settings.FunctionVersion
        let key = DataKey.create source.Id (string settings.DatabaseName) false (string settings.FunctionName) version
                    entry.DataType settings.DataTypes[entry.DataType] parameters []
        let store = openProject workspace workspaceRoot source.Id
        match store.FindResults(key, token) with
        | Error message -> invalidOp message
        | Ok [value] -> value
        | Ok [] -> invalidOp $"Source data '{entry.DataType}' is missing from project '{source.ProjectName}' for the requested parameters. Execute the source run first."
        | Ok _ -> invalidOp $"Source data '{entry.DataType}' is ambiguous in project '{source.ProjectName}': different setup inputs produced matching results. Use more specific results query keys."
