namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.FileDb
open Sortopia.Workspaces.Serialization
open WorkspaceFileSystem

type internal RemovedPath = { Source: string; Destination: string }

module internal WorkspaceRemoval =
    let private projects root (value: WorkspaceDocument) =
        let rec visit parent (modules: ModuleDocument array) = seq {
            for m in modules do
                let folder = Path.Combine(parent, m.Definition.folderName)
                for p in m.Projects do yield p, Path.Combine(folder, p.Definition.folderName)
                yield! visit folder m.Modules }
        visit root value.Modules |> Seq.toArray

    let private entities root (value: WorkspaceDocument) =
        let rec visit parent (modules: ModuleDocument array) = seq {
            for m in modules do
                let folder = Path.Combine(parent, m.Definition.folderName)
                yield ("module", m.Definition.id), folder
                for p in m.Projects do
                    let folder = Path.Combine(folder, p.Definition.folderName)
                    yield ("project", p.Definition.id), folder
                    for r in p.Runs do yield ("run", r.id), Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name r.id)
                yield! visit folder m.Modules }
        visit root value.Modules |> Map.ofSeq

    // Query keys are explicit spans. Incomplete surviving drafts conservatively
    // protect their output type; deleting another run must not discard their data.
    let private retainedQueries (project: ProjectDocument) (token: CancellationToken) =
        let queries = Collections.Generic.HashSet<Guid<sortopiaDataId>>()
        let unknown = Collections.Generic.HashSet<bool * sortopiaDataType>()
        let settings = project.Definition.configuration
        if not project.Definition.isSealed then fun _ _ -> true
        else
            let add setup database name version (dataType: sortopiaDataTypeDto) keys (spans: parameterSpanDto array) =
                let dataType = SortopiaDataTypeDto.toDomain dataType
                try
                    let selected = keys |> Array.map (fun key ->
                        let span = spans |> Array.filter (fun span -> span.keyName = key)
                        if span.Length <> 1 || span[0].values.Length = 0 then invalidOp "Incomplete query span."
                        let span = span[0]
                        let kind = if span.valueType.HasValue then enum<parameterValueType> span.valueType.Value else ParamKeys.valueType (UMX.tag key)
                        ParameterSpan.fromTexts (UMX.tag key) kind (Array.toList span.values)) |> Array.toList
                    for parameters in ParameterSets.create [] selected do
                        token.ThrowIfCancellationRequested()
                        let key = DataKey.create (UMX.tag project.Definition.id) database setup name version dataType
                                      (keys |> Array.map UMX.tag |> Array.toList) parameters []
                        queries.Add key.QueryId |> ignore
                with
                | :? OperationCanceledException -> reraise ()
                | :? ArgumentException | :? InvalidOperationException | :? FormatException | :? OverflowException -> unknown.Add(setup, dataType) |> ignore
            let perRun = project.Runs |> Array.map (fun r ->
                queries.Clear(); unknown.Clear()
                for output in settings.resultsDataTypes do
                    add false settings.resultsDatabaseName settings.resultsFunctionName settings.resultsFunctionVersion output.dataType output.queryParameterKeys r.resultsParameterSpans
                for output in settings.setupDataTypes do
                    add true settings.setupDatabaseName settings.setupFunctionName settings.setupFunctionVersion output.dataType output.queryParameterKeys r.setupParameterSpans
                Set.ofSeq queries, Set.ofSeq unknown)
            fun (reference: storedDataReference) (dependencies: storedDataReference list) ->
                perRun |> Array.exists (fun (queries, unknown) ->
                    let matches (value: storedDataReference) = Set.contains value.QueryId queries || Set.contains (value.IsSetup, value.DataType) unknown
                    matches reference && (reference.IsSetup || dependencies |> List.forall (fun input -> input.ProjectId = reference.ProjectId && input.IsSetup && matches input)))

    let plan root (previous: WorkspaceDocument) (next: WorkspaceDocument) (moves: WorkspaceFolderMove array) token =
        let oldEntities = entities root previous
        let currentEntities = entities root next
        let destination source =
            moves |> Array.filter (fun move -> WorkspaceFolderCopy.Inside(move.SourceFolder, source))
                  |> Array.sortByDescending (fun move -> move.SourceFolder.Length) |> Array.tryHead
                  |> Option.map (fun move -> Path.GetFullPath(Path.Combine(move.DestinationFolder, Path.GetRelativePath(move.SourceFolder, source))))
                  |> Option.defaultValue source
        let paths = ResizeArray<RemovedPath>()
        for KeyValue(id, path) in oldEntities do
            if not (Map.containsKey id currentEntities) && (File.Exists path || Directory.Exists path) then
                paths.Add { Source = path; Destination = destination path }
        for oldProject, folder in projects root previous do
            match projects root next |> Array.tryFind (fun (p, _) -> p.Definition.id = oldProject.Definition.id) with
            | None -> () // Entire project folder is already included.
            | Some (kept, _) ->
                let remaining = kept.Runs |> Array.map _.id |> Set.ofArray
                let removed = oldProject.Runs |> Array.map _.id |> Array.filter (fun id -> not (Set.contains id remaining)) |> Array.map UMX.tag<runId> |> Set.ofArray
                if not removed.IsEmpty && Directory.Exists folder then
                    for path in RunDataCleanup.plan folder (UMX.tag oldProject.Definition.id) removed (retainedQueries kept token) token do
                        paths.Add { Source = path; Destination = destination path }
        // Delete only explicitly removed members, never unlisted folders discovered on disk.
        let roots = paths |> Seq.distinctBy _.Destination |> Seq.filter (fun path ->
            not (paths |> Seq.exists (fun parent -> parent.Destination <> path.Destination && WorkspaceFolderCopy.Inside(parent.Destination, path.Destination)))) |> Seq.toArray
        for removed in roots do
            WorkspaceFolderMove.CheckPath(root, removed.Source)
            WorkspaceFolderMove.CheckPath(root, removed.Destination)
            if currentEntities |> Map.exists (fun _ path -> WorkspaceFolderCopy.Inside(removed.Destination, path)) then
                invalidOp $"Save removal before reusing its folder, or move retained children out first: {removed.Destination}"
            for KeyValue(id, oldPath) in oldEntities do
                match Map.tryFind id currentEntities with
                | Some keptPath when WorkspaceFolderCopy.Inside(removed.Source, oldPath) && not (pathComparer.Equals(destination oldPath, keptPath)) ->
                    invalidOp $"Move retained child '{oldPath}' with its contents before removing its parent."
                | _ -> ()
        roots

    let checkTree root path (token: CancellationToken) =
        WorkspaceFolderMove.CheckPath(root, path)
        let rec check (path: string) =
            token.ThrowIfCancellationRequested()
            if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then invalidOp $"Cannot delete linked files or folders: {path}"
            if Directory.Exists path then
                for entry in Directory.GetFileSystemEntries path do check entry
            else
                // Detect locked/read-only files before publishing any removal.
                use unlocked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
                ()
        check path

    let deleteStaging root (path: string) =
        WorkspaceFolderMove.CheckPath(root, path)
        if not (pathComparer.Equals(Path.GetDirectoryName path, root)) || not (Path.GetFileName(path).StartsWith(".sortopia-delete-", StringComparison.Ordinal)) then
            invalidOp "Invalid removal staging path."
        let rec remove (path: string) =
            if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then invalidOp $"Cannot delete linked files or folders: {path}"
            if Directory.Exists path then
                for entry in Directory.GetFileSystemEntries path do remove entry
                Directory.Delete path
            else File.Delete path
        remove path
