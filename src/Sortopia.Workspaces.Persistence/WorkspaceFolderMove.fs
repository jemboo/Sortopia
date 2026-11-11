namespace Sortopia.Workspaces.Persistence

open System
open System.IO

/// A folder or run-file relocation within one workspace, committed with its definitions.
type WorkspaceFolderMove =
    { SourceFolder: string; DestinationFolder: string }
    member this.ContainsSourcePath(path) = WorkspaceFolderCopy.Inside(this.SourceFolder, path)

    static member internal CheckPath(root: string, path: string) =
        if not (Path.IsPathFullyQualified(path)) then raise (IOException("A moved entity requires an absolute folder path."))
        let root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))
        let path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        if not (WorkspaceFolderCopy.Inside(root, path)) || String.Equals(root, path, StringComparison.OrdinalIgnoreCase) then
            raise (IOException("A moved entity must stay inside its workspace root."))
        let mutable current = path
        while not (isNull current) do
            if (Directory.Exists(current) || File.Exists(current)) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint) then
                raise (IOException($"Linked paths cannot be moved: {current}"))
            current <- if String.Equals(current, root, StringComparison.OrdinalIgnoreCase) then null else Path.GetDirectoryName(current)
