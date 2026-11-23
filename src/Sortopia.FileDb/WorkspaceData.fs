namespace Sortopia.FileDb

open System.IO
open System.Threading
open Sortopia.Runs
open Sortopia.Workspaces

module WorkspaceData =
    /// Resolve upstream results using the producer's configuration, not the
    /// consumer's setup projection. Multiple dependency variants are ambiguous.
    let private loadSourceAt folder (source: sourceProjectSnapshot) (entry: setupDataTypeConfig) parameters token =
        if not (Map.containsKey entry.DataType source.Results.DataTypes) then
            invalidOp $"Source project '{source.ProjectName}' does not provide '{entry.DataType}'."
        let settings = source.Results
        let version = settings.FunctionVersion
        let key = DataKey.create source.ProjectId (string settings.DatabaseName) false (string settings.FunctionName) version
                    entry.DataType settings.DataTypes[entry.DataType] parameters []
        let store = SortopiaFileDb(folder, source.ProjectId) :> ISortopiaDb
        match store.FindResults(key, token) with
        | Error message -> invalidOp message
        | Ok [value] -> value
        | Ok [] -> invalidOp $"Source data '{entry.DataType}' is missing from project '{source.ProjectName}' for the requested parameters. Execute the source run first."
        | Ok _ -> invalidOp $"Source data '{entry.DataType}' is ambiguous in project '{source.ProjectName}': different setup inputs produced matching results. Use more specific results query keys."

    let scopedProjectFolder (scope: RunExecutionScope) workspaceRoot projectId =
        let location = scope.FindProject projectId
                       |> Option.defaultWith (fun () -> invalidOp $"Required project '{projectId}' was not found.")
        Path.Combine(Path.GetFullPath workspaceRoot, location.RelativeFolder)

    let loadScopedSource (scope: RunExecutionScope) workspaceRoot (entry: setupDataTypeConfig) parameters token =
        let source = scope.FindProject entry.SourceProjectId
                     |> Option.defaultWith (fun () -> invalidOp $"Source project '{entry.SourceProjectId}' was not found.")
        loadSourceAt (scopedProjectFolder scope workspaceRoot entry.SourceProjectId) source.Snapshot entry parameters token
