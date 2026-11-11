namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Security.Cryptography
open System.Threading

type internal CopyEntry = { RelativePath: string; Length: int64; Hash: string; LastWriteTimeUtc: DateTime }
type private CopySnapshot = { Directories: string array; Files: CopyEntry array }

/// A folder or run-file snapshot, copied only when its workspace transaction is applied.
[<Sealed; AllowNullLiteral>]
type WorkspaceFolderCopy private (source: string, destination: string, snapshot: CopySnapshot, id: Guid,
                                  excludedFolders: string array, originalIds: IReadOnlyDictionary<Guid, Guid>) =
    member _.SourceFolder = source
    member _.DestinationFolder = destination
    member _.Id = id
    /// Original entity IDs indexed by the IDs allocated for this import.
    member _.OriginalIds = originalIds
    member internal _.WithOriginalIds(ids) = WorkspaceFolderCopy(source, destination, snapshot, id, excludedFolders, ids)
    member private _.IncludesRelativePath(path) = excludedFolders |> Array.exists (fun excluded -> WorkspaceFolderCopy.Inside(excluded, path)) |> not
    member internal this.IncludesSourcePath(path) = this.IncludesRelativePath(Path.GetRelativePath(source, path))
    member internal this.Directories = snapshot.Directories |> Array.filter this.IncludesRelativePath
    member internal this.Files = snapshot.Files |> Array.filter (fun file -> this.IncludesRelativePath(file.RelativePath))

    member _.WithExclusions(folders: IEnumerable<string>) =
        let exclusions = System.Linq.Enumerable.Distinct(folders, StringComparer.OrdinalIgnoreCase) |> Seq.toArray
        for relative in exclusions do
            if String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative = "."
               || not (WorkspaceFolderCopy.Inside(source, Path.GetFullPath(Path.Combine(source, relative)))) then
                raise (IOException("Invalid imported subtree location."))
        WorkspaceFolderCopy(source, destination, snapshot, id, exclusions, originalIds)

    static member Prepare(workspaceRoot: string, sourceFolder: string, destinationFolder: string, token: CancellationToken) =
        let source = WorkspaceFolderCopy.Normalize(sourceFolder)
        let destination = WorkspaceFolderCopy.Normalize(destinationFolder)
        WorkspaceFolderCopy.CheckAncestors(source)
        WorkspaceFolderCopy.CheckDestination(workspaceRoot, source, destination)
        // Already in the correct container: attach in place.
        if WorkspaceFolderCopy.Same(source, destination) then null
        else WorkspaceFolderCopy(source, destination, WorkspaceFolderCopy.Capture(source, token), Guid.NewGuid(), [||], Dictionary<Guid, Guid>())

    member _.WithDestination(workspaceRoot: string, destinationFolder: string) =
        let destination = WorkspaceFolderCopy.Normalize(destinationFolder)
        if WorkspaceFolderCopy.Same(source, destination) then
            raise (IOException("A pending copy cannot replace its source folder. Choose another folder name."))
        WorkspaceFolderCopy.CheckDestination(workspaceRoot, source, destination)
        WorkspaceFolderCopy(source, destination, snapshot, id, excludedFolders, originalIds)

    member _.ValidateSource(token: CancellationToken) =
        if WorkspaceFolderCopy.Capture(source, token) <> snapshot then
            raise (IOException($"The import source changed. Remove it from the draft and add it again: {source}"))

    /// Splits a child out of this pending import while keeping its already allocated IDs.
    member this.PrepareChild(workspaceRoot: string, childSource: string, childDestination: string, token: CancellationToken) =
        let childSource = WorkspaceFolderCopy.Normalize(childSource)
        if WorkspaceFolderCopy.Same(source, childSource) || not (WorkspaceFolderCopy.Inside(source, childSource)) then
            raise (IOException("A child import must come from inside its parent import."))
        this.ValidateSource(token)
        let child = WorkspaceFolderCopy.Prepare(workspaceRoot, childSource, childDestination, token)
        if isNull child then raise (IOException("A pending import cannot be moved back into its source folder."))
        child.WithOriginalIds(originalIds)

    member internal _.ValidateDestination(workspaceRoot) = WorkspaceFolderCopy.CheckDestination(workspaceRoot, source, destination)

    member internal this.Stage(workspaceRoot: string, token: CancellationToken) =
        this.ValidateDestination(workspaceRoot)
        WorkspaceFolderCopy.CheckAncestors(source)
        let staging = Path.Combine(WorkspaceFolderCopy.Normalize(workspaceRoot), ".sortopia-import-" + Guid.NewGuid().ToString("N"))
        if this.Directories.Length > 0 then Directory.CreateDirectory(staging) |> ignore
        try
            for directory in this.Directories do
                token.ThrowIfCancellationRequested()
                Directory.CreateDirectory(Path.Combine(staging, directory)) |> ignore
            for file in this.Files do
                token.ThrowIfCancellationRequested()
                let sourceFile = Path.Combine(source, file.RelativePath)
                WorkspaceFolderCopy.CheckAncestors(sourceFile)
                let target = Path.Combine(staging, file.RelativePath)
                do
                    use input = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read)
                    use output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    let hash = WorkspaceFolderCopy.Hash(input, token, Some output)
                    if input.Length <> file.Length || hash <> file.Hash then
                        raise (IOException($"The import source changed while copying: {sourceFile}"))
                File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc)
            this.ValidateSource(token) // Also detects added/removed files and empty directories.
            staging
        with _ ->
            WorkspaceFolderCopy.DeleteStaging(workspaceRoot, staging)
            reraise ()

    static member internal DeleteStaging(workspaceRoot: string, staging: string) =
        let root = WorkspaceFolderCopy.Normalize(workspaceRoot)
        let path = WorkspaceFolderCopy.Normalize(staging)
        if not (WorkspaceFolderCopy.Same(Path.GetDirectoryName(path), root))
           || not (Path.GetFileName(path).StartsWith(".sortopia-import-", StringComparison.Ordinal)) then
            raise (IOException("Invalid import staging path."))
        if File.Exists(path) then
            WorkspaceFolderCopy.CheckAncestors(path)
            File.Delete(path)
        elif Directory.Exists(path) then
            // Delete only regular files/directories in this transaction's private staging folder.
            WorkspaceFolderCopy.CheckAncestors(path)
            WorkspaceFolderCopy.DeleteRegularTree(path)

    static member private DeleteRegularTree(path: string) =
        WorkspaceFolderCopy.CheckLink(path)
        for entry in Directory.GetFileSystemEntries(path) do
            WorkspaceFolderCopy.CheckLink(entry)
            if Directory.Exists(entry) then WorkspaceFolderCopy.DeleteRegularTree(entry)
            else File.Delete(entry)
        Directory.Delete(path)

    static member internal Matches(path: string, expected: CopyEntry) =
        WorkspaceFolderCopy.CheckAncestors(path)
        use input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        input.Length = expected.Length && WorkspaceFolderCopy.Hash(input, CancellationToken.None, None) = expected.Hash

    static member private Capture(source: string, token: CancellationToken) =
        WorkspaceFolderCopy.CheckAncestors(source)
        if File.Exists(source) then
            use input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read)
            { Directories = [||]
              Files = [| { RelativePath = ""; Length = input.Length; Hash = WorkspaceFolderCopy.Hash(input, token, None); LastWriteTimeUtc = File.GetLastWriteTimeUtc(source) } |] }
        else
            if not (Directory.Exists(source)) then raise (DirectoryNotFoundException($"Import source is unavailable: {source}"))
            let directories = ResizeArray<string>([| "" |])
            let files = ResizeArray<CopyEntry>()
            let pending = Stack<string>()
            pending.Push(source)
            while pending.Count > 0 do
                let directory = pending.Pop()
                token.ThrowIfCancellationRequested()
                WorkspaceFolderCopy.CheckLink(directory)
                for path in Directory.GetFileSystemEntries(directory) |> Array.sortWith (fun a b -> StringComparer.OrdinalIgnoreCase.Compare(a, b)) do
                    token.ThrowIfCancellationRequested()
                    WorkspaceFolderCopy.CheckLink(path)
                    let relative = Path.GetRelativePath(source, path)
                    if Directory.Exists(path) then
                        directories.Add(relative)
                        pending.Push(path)
                    else
                        use input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                        files.Add({ RelativePath = relative; Length = input.Length; Hash = WorkspaceFolderCopy.Hash(input, token, None); LastWriteTimeUtc = File.GetLastWriteTimeUtc(path) })
            { Directories = directories |> Seq.sortWith (fun left right ->
                  let length = compare left.Length right.Length
                  if length <> 0 then length else StringComparer.OrdinalIgnoreCase.Compare(left, right)) |> Seq.toArray
              Files = files |> Seq.sortWith (fun left right -> StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath)) |> Seq.toArray }

    static member private Hash(input: Stream, token: CancellationToken, output: Stream option) =
        use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
        let buffer = Array.zeroCreate<byte> (128 * 1024)
        let mutable count = input.Read(buffer, 0, buffer.Length)
        while count > 0 do
            token.ThrowIfCancellationRequested()
            hash.AppendData(buffer, 0, count)
            output |> Option.iter (fun stream -> stream.Write(buffer, 0, count))
            count <- input.Read(buffer, 0, buffer.Length)
        Convert.ToHexString(hash.GetHashAndReset())

    static member private CheckDestination(workspaceRoot: string, source: string, destination: string) =
        let root = WorkspaceFolderCopy.Normalize(workspaceRoot)
        if not (WorkspaceFolderCopy.Inside(root, destination)) || WorkspaceFolderCopy.Same(root, destination) then
            raise (IOException("Import destination must be inside the selected workspace."))
        WorkspaceFolderCopy.CheckAncestors(destination)
        if not (WorkspaceFolderCopy.Same(source, destination)) then
            if WorkspaceFolderCopy.Inside(source, destination) || WorkspaceFolderCopy.Inside(destination, source) then
                raise (IOException("An import source and destination cannot contain one another."))
            if Directory.Exists(destination) || File.Exists(destination) then
                raise (IOException($"The import destination already exists. Choose a different folder name: {destination}"))

    static member internal Inside(parent: string, path: string) =
        WorkspaceFolderCopy.Same(parent, path)
        || path.StartsWith((if Path.EndsInDirectorySeparator(parent) then parent else parent + string Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
    static member private Same(left: string, right: string) = String.Equals(left, right, StringComparison.OrdinalIgnoreCase)
    static member private Normalize(path: string) =
        if String.IsNullOrWhiteSpace(path) || not (Path.IsPathFullyQualified(path)) then raise (ArgumentException("Choose an absolute folder path."))
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
    static member private CheckAncestors(path: string) =
        let mutable current = path
        while not (isNull current) do
            if File.Exists(current) || Directory.Exists(current) then WorkspaceFolderCopy.CheckLink(current)
            current <- Path.GetDirectoryName(current)
    static member private CheckLink(path: string) =
        if File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) then
            raise (IOException($"Linked files or folders cannot be copied into a workspace: {path}"))
