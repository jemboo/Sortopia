namespace Sortopia.Workspaces.Persistence

open System
open System.IO

module internal WorkspaceFileSystem =
    let maxDefinitionBytes = 16 * 1024 * 1024
    let maxEntities = 50_000
    let pathComparer = StringComparer.OrdinalIgnoreCase

    let readDefinition (path: string) =
        if File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) then
            raise (IOException($"Linked definition files are not supported: {path}"))
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length > int64 maxDefinitionBytes then
            raise (InvalidDataException($"Definition exceeds 16 MB: {path}"))
        let bytes = Array.zeroCreate<byte> (int stream.Length)
        stream.ReadExactly(bytes)
        bytes

    let equalBytes (left: byte array) (right: byte array) =
        if isNull left then isNull right
        else not (isNull right) && MemoryExtensions.SequenceEqual<byte>(ReadOnlySpan<byte>(left), ReadOnlySpan<byte>(right))

    let normalizeRoot (path: string) =
        if String.IsNullOrWhiteSpace(path) || not (Path.IsPathFullyQualified(path)) then
            raise (ArgumentException("Choose an absolute workspace root folder."))
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))

    let checkDirectory (path: string) =
        if File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint) then
            raise (IOException($"Linked folders are not supported in the workspace hierarchy: {path}"))

    let checkDepth depth =
        if depth > 64 then raise (InvalidDataException("Workspace nesting must not exceed 64 levels."))

    let checkContained (root: string) (path: string) =
        let prefix = if Path.EndsInDirectorySeparator(root) then root else root + string Path.DirectorySeparatorChar
        if not (pathComparer.Equals(root, path)) && not (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) then
            raise (IOException("Workspace path escapes the selected root folder."))

    let movePath source destination =
        if Directory.Exists(source) then Directory.Move(source, destination)
        else File.Move(source, destination)

    let relative (root: string) (path: string) = Path.GetRelativePath(root, path)

