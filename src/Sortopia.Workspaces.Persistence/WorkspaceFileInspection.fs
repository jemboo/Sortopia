namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Text.Json
open System.Threading
open MessagePack
open Sortopia.Core.Serialization

type inspectionEntry = {
    Name: string; FullPath: string; IsDirectory: bool; IsLink: bool
    Length: int64; ModifiedUtc: DateTime
}
type inspectionListing = { Entries: inspectionEntry array; HasMore: bool }
type inspectionReference = { Label: string; FullPath: string; Exists: bool }
type configurationInspection = {
    Kind: string; Description: string; Json: string; References: inspectionReference array
}

/// Read-only browsing of physical files, independent of loading a valid workspace.
module WorkspaceFileInspection =
    let pageSize = 200
    let maxConfigurationBytes = 4 * 1024 * 1024
    let private options = MessagePackSetup.options.WithSecurity(
                            MessagePackSetup.options.Security.WithMaximumObjectGraphDepth(64)
                                .WithMaximumDecompressedSize(maxConfigurationBytes))

    let checkedPath root path =
        let root = WorkspaceFileSystem.normalizeRoot root
        let path = Path.GetFullPath path
        WorkspaceFileSystem.checkContained root path
        // Check ancestors of the selected root too: do not silently traverse a junction.
        let mutable current = path
        while not (String.IsNullOrEmpty current) do
            if File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint) then
                invalidOp $"Filesystem links are not followed by the inspector: {current}"
            current <- Path.GetDirectoryName current
        path

    let entry (path: string) =
        let attributes = File.GetAttributes path
        let directory = attributes.HasFlag FileAttributes.Directory
        { Name = Path.GetFileName path; FullPath = path; IsDirectory = directory
          IsLink = attributes.HasFlag FileAttributes.ReparsePoint
          Length = if directory then 0L else FileInfo(path).Length
          ModifiedUtc = File.GetLastWriteTimeUtc path }

    let listFolder root folder offset filter (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        if offset < 0 then invalidArg "offset" "Invalid page offset."
        let folder = checkedPath root folder
        // Only enumerate this directory; never walk a data tree or decode files to list it.
        let paths = Seq.append (Directory.EnumerateDirectories folder) (Directory.EnumerateFiles folder)
        let entries =
            paths
            |> Seq.filter (fun path ->
                token.ThrowIfCancellationRequested()
                String.IsNullOrEmpty filter || Path.GetFileName(path).Contains(filter, StringComparison.OrdinalIgnoreCase))
            |> Seq.skipWhile (let mutable skipped = 0 in fun _ -> skipped <- skipped + 1; skipped <= offset)
            |> Seq.choose (fun path ->
                try Some(entry path) with :? FileNotFoundException | :? DirectoryNotFoundException -> None)
            |> Seq.truncate (pageSize + 1) |> Seq.toArray
        { Entries = entries |> Array.truncate pageSize; HasMore = entries.Length > pageSize }

    let inspectConfiguration root path (token: CancellationToken) =
        let path = checkedPath root path
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length > int64 maxConfigurationBytes then
            { Kind = "Large configuration file"; Description = "Detailed decoding is limited to 4 MiB. File metadata is shown; the contents were not loaded."
              Json = ""; References = [||] }
        else
            let bytes = Array.zeroCreate<byte> (int stream.Length)
            stream.ReadExactly bytes
            token.ThrowIfCancellationRequested()
            let json = MessagePackSerializer.ConvertToJson(ReadOnlyMemory<byte>(bytes), options, token)
            use document = JsonDocument.Parse(json, JsonDocumentOptions(MaxDepth = 64))
            let value = document.RootElement
            let has (name: string) = value.ValueKind = JsonValueKind.Object && (value.TryGetProperty(name) |> fst)
            let kind, description =
                if has "workspaceName" then "Workspace definition", "Own workspace settings and ordered module references."
                elif has "moduleName" then "Module definition", "Local overrides, captured inherited defaults, and ordered child references."
                elif has "projectName" then "Project definition", "Draft projects capture inherited defaults; sealed projects contain fixed configuration. Runs are referenced by ID."
                elif has "runName" then "Run definition", "Local run inputs and parent project ID. Fixed functions and defaults belong to the parent project."
                else "MessagePack file", "Decoded MessagePack content. This file is not a recognized workspace definition."
            let folder = Path.GetDirectoryName path
            let references = ResizeArray<inspectionReference>()
            let add label relative =
                let full = Path.GetFullPath(Path.Combine(folder, relative))
                WorkspaceFileSystem.checkContained (WorkspaceFileSystem.normalizeRoot root) full
                references.Add { Label = label; FullPath = full; Exists = File.Exists full }
            for collection in ["modules"; "projects"] do
                if has collection then
                    for child in value.GetProperty(collection).EnumerateArray() do
                        let name = child.GetProperty("folderName").GetString()
                        let id = child.GetProperty("id").GetGuid()
                        Sortopia.Runs.FolderName.validate name |> ignore
                        let oldLayout = has "schemaVersion" && value.GetProperty("schemaVersion").GetInt32() = 15
                        add name (if oldLayout then Path.Combine(collection, name, string id + ".msgpack") else Path.Combine(name, string id + ".msgpack"))
            if has "runs" then
                for child in value.GetProperty("runs").EnumerateArray() do
                    let id = child.GetGuid()
                    add (string id) (Path.Combine("runs", string id + ".msgpack"))
            if kind = "Run definition" && has "parentProjectId" then
                let parent = Path.Combine(folder, "..", value.GetProperty("parentProjectId").GetGuid().ToString() + ".msgpack") |> Path.GetFullPath
                let root = WorkspaceFileSystem.normalizeRoot root
                if parent.StartsWith(root + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) then
                    add "Parent project" parent
            let versionNote =
                if has "schemaVersion" then
                    let version = value.GetProperty("schemaVersion").GetInt32()
                    let expected = if kind = "Run definition" then 14 else 16
                    if version <> expected then $" Stored version {version} is not the current version ({expected})." else ""
                else ""
            { Kind = kind; Description = description + versionNote + " This is a view of saved bytes, not a validation of the complete workspace."
              Json = json; References = references.ToArray() }
