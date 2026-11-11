namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Threading
open WorkspaceFileSystem
open WorkspaceDisk

type private StagedMove(move: WorkspaceFolderMove, staging: string) =
    member _.Move = move
    member _.Staging = staging
    member val Published = false with get, set

type private Publication =
    | Copy of WorkspaceFolderCopy * staging: string
    | Move of StagedMove
    member this.Destination = match this with Copy(copy, _) -> copy.DestinationFolder | Move item -> item.Move.DestinationFolder
    member this.Staging = match this with Copy(_, staging) -> staging | Move item -> item.Staging

module internal WorkspaceCommit =
    let commit (plan: SavePlan) (token: CancellationToken) =
        let root = plan.RootFolder
        if (capture root token "workspace").Fingerprint <> plan.Fingerprint then
            raise (IOException("The file system changed while preparing the save. Try saving again."))
        let staged = ResizeArray<WorkspaceFileUpdate * string>()
        let applied = ResizeArray<WorkspaceFileUpdate>()
        let stagedCopies = ResizeArray<WorkspaceFolderCopy * string>()
        let publishedCopies = ResizeArray<WorkspaceFolderCopy>()
        let stagedMoves = ResizeArray<StagedMove>()
        let createdFolders = ResizeArray<string>()
        let mutable committed = false
        let rec ensureDirectory path =
            checkContained root path
            if Directory.Exists(path) then checkDirectory path
            else
                if not (pathComparer.Equals(path, root)) then ensureDirectory (Path.GetDirectoryName(path))
                Directory.CreateDirectory(path) |> ignore
                createdFolders.Add(path)
        try
            try
                if plan.Copies.Length > 0 then ensureDirectory root
                let beforeStaging = if plan.Copies.Length > 0 then (capture root token "workspace").Fingerprint else null
                for copy in plan.Copies do
                    token.ThrowIfCancellationRequested()
                    stagedCopies.Add(copy, copy.Stage(root, token))
                // Staging is outside the typed containers and must not change the managed workspace.
                if plan.Copies.Length > 0 && (capture root token "workspace").Fingerprint <> beforeStaging then
                    raise (IOException("The file system changed while staging imports. Try saving again."))
                // Detach nested sources first, so each subtree is moved exactly once.
                for move in plan.Moves |> Array.sortByDescending (fun move -> move.SourceFolder.Length) do
                    token.ThrowIfCancellationRequested()
                    WorkspaceFolderMove.CheckPath(root, move.SourceFolder)
                    let staging = Path.Combine(root, ".sortopia-move-" + Guid.NewGuid().ToString("N"))
                    movePath move.SourceFolder staging
                    stagedMoves.Add(StagedMove(move, staging))
                let publications =
                    Seq.append (stagedCopies |> Seq.map Copy) (stagedMoves |> Seq.map Move)
                    |> Seq.sortBy (fun item -> item.Destination.Length)
                for publication in publications do
                    token.ThrowIfCancellationRequested()
                    WorkspaceFolderMove.CheckPath(root, publication.Destination)
                    if Directory.Exists(publication.Destination) || File.Exists(publication.Destination) then
                        raise (IOException($"The destination already exists: {publication.Destination}"))
                    ensureDirectory (Path.GetDirectoryName(publication.Destination))
                    movePath publication.Staging publication.Destination
                    match publication with
                    | Move moved -> moved.Published <- true
                    | Copy(copy, _) ->
                        publishedCopies.Add(copy)
                        createdFolders.AddRange(copy.Directories |> Array.map (fun relative -> Path.Combine(copy.DestinationFolder, relative)))
                for write in plan.Writes do
                    token.ThrowIfCancellationRequested()
                    ensureDirectory (Path.GetDirectoryName(write.Path))
                    let temporary = write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp"
                    staged.Add(write, temporary)
                    File.WriteAllBytes(temporary, write.Content)
                for write, temporary in staged do
                    token.ThrowIfCancellationRequested()
                    let current = if not (isNull write.OriginalPath) && File.Exists(write.OriginalPath) then readDefinition write.OriginalPath else null
                    if not (equalBytes current write.Original) then raise (IOException($"Definition changed while saving: {write.Path}"))
                    let samePath = pathComparer.Equals(write.Path, write.OriginalPath)
                    File.Move(temporary, write.Path, overwrite = samePath)
                    applied.Add(write)
                    if not samePath && not (isNull write.OriginalPath) then File.Delete(write.OriginalPath)
                committed <- true
                WorkspaceSaveResult(plan.Definition, plan.Copies.Length, plan.RetainedFolders, plan.Moves.Length)
            with failure ->
                let errors = ResizeArray<Exception>([| failure |])
                let recover action = try action () with rollback -> errors.Add(rollback)
                for write in applied |> Seq.rev do
                    recover (fun () ->
                        // Do not overwrite an external edit made after our write.
                        if not (File.Exists(write.Path)) || not (equalBytes (readDefinition write.Path) write.Content) then
                            raise (IOException($"Could not roll back externally changed definition: {write.Path}"))
                        if pathComparer.Equals(write.Path, write.OriginalPath) then File.WriteAllBytes(write.Path, write.Original)
                        else
                            if not (isNull write.OriginalPath) then
                                // A failed rename may have left the old file in place (for example, when locked).
                                if File.Exists(write.OriginalPath) then
                                    if not (equalBytes (readDefinition write.OriginalPath) write.Original) then
                                        raise (IOException($"Could not restore externally changed definition: {write.OriginalPath}"))
                                else
                                    use restored = new FileStream(write.OriginalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                                    restored.Write(write.Original, 0, write.Original.Length)
                            File.Delete(write.Path))
                // Remove temporary metadata before restoring paths; never carry it back into a source folder.
                for _, temporary in staged do recover (fun () -> if File.Exists(temporary) then File.Delete(temporary))
                for folder in createdFolders |> Seq.rev do
                    recover (fun () ->
                        checkContained root folder
                        if Directory.Exists(folder) && Seq.isEmpty (Directory.EnumerateFileSystemEntries(folder)) then
                            checkDirectory folder
                            Directory.Delete(folder))
                let rollbackFolders =
                    Seq.append
                        (stagedMoves |> Seq.filter _.Published |> Seq.map Move)
                        (publishedCopies |> Seq.map (fun copy -> Copy(copy, "")))
                    |> Seq.sortByDescending (fun item -> item.Destination.Length)
                for publication in rollbackFolders do
                    match publication with
                    | Move item ->
                        try
                            WorkspaceFolderMove.CheckPath(root, item.Move.DestinationFolder)
                            movePath item.Move.DestinationFolder item.Staging
                            item.Published <- false
                        with rollback -> errors.Add(IOException($"Could not recover moved folder at {item.Move.DestinationFolder}.", rollback))
                    | Copy(copy, _) ->
                        for file in copy.Files do
                            recover (fun () ->
                                let path = Path.Combine(copy.DestinationFolder, file.RelativePath)
                                checkContained root path
                                if File.Exists(path) then
                                    if not (WorkspaceFolderCopy.Matches(path, file)) then
                                        raise (IOException($"Could not remove an externally changed imported file: {path}"))
                                    File.Delete(path))
                        for relative in copy.Directories |> Array.rev do
                            recover (fun () ->
                                let folder = Path.Combine(copy.DestinationFolder, relative)
                                WorkspaceFolderMove.CheckPath(root, folder)
                                if Directory.Exists(folder) && Seq.isEmpty (Directory.EnumerateFileSystemEntries(folder)) then Directory.Delete(folder))
                // Restore parents before their separately moved descendants.
                for item in stagedMoves |> Seq.filter (fun item -> not item.Published) |> Seq.sortBy (fun item -> item.Move.SourceFolder.Length) do
                    try
                        WorkspaceFolderMove.CheckPath(root, item.Move.SourceFolder)
                        ensureDirectory (Path.GetDirectoryName(item.Move.SourceFolder))
                        movePath item.Staging item.Move.SourceFolder
                    with rollback -> errors.Add(IOException($"Could not restore {item.Move.SourceFolder}; its contents are retained at {item.Staging}.", rollback))
                if errors.Count > 1 then raise (AggregateException("Save failed; some files could not be restored.", errors))
                reraise ()
        finally
            for _, temporary in staged do if File.Exists(temporary) then File.Delete(temporary)
            for _, staging in stagedCopies do WorkspaceFolderCopy.DeleteStaging(root, staging)
            if not committed then
                for folder in createdFolders |> Seq.rev do
                    if not (plan.Moves |> Array.exists (fun move -> WorkspaceFolderCopy.Inside(move.SourceFolder, folder)))
                       && Directory.Exists(folder) && Seq.isEmpty (Directory.EnumerateFileSystemEntries(folder)) then Directory.Delete(folder)
