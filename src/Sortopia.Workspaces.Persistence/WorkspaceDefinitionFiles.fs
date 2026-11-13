namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open Sortopia.Core.Serialization

/// Locates GUID-named entity definitions.
module WorkspaceDefinitionFiles =
    [<CLIMutable>]
    type Header = { schemaVersion: int; workspaceName: string; moduleName: string; projectName: string; runName: string }

    let Name (id: Guid) = $"{id:D}.msgpack"

    let HasGuid (name: string) =
        name.EndsWith(".msgpack", StringComparison.OrdinalIgnoreCase)
        && (Guid.TryParseExact(name.Substring(0, name.Length - 8), "D") |> fst)

    let private checkFile (path: string) =
        let attributes = File.GetAttributes(path)
        if attributes.HasFlag(FileAttributes.ReparsePoint) then
            raise (IOException($"Linked definition files are not supported: {path}"))
        if attributes.HasFlag(FileAttributes.Directory) then
            raise (IOException($"A folder occupies a definition file path: {path}"))

    let private matches (path: string) kind =
        if not (HasGuid(Path.GetFileName(path))) then false
        else
            checkFile path
            let header =
                try WorkspaceFileSystem.readDefinition path |> MessagePackSetup.deserialize<Header>
                with :? MessagePack.MessagePackSerializationException as error ->
                    raise (InvalidDataException($"Unreadable entity definition: {path}", error))
            if isNull (box header) then raise (InvalidDataException($"Missing entity definition: {path}"))
            let actual =
                if not (isNull header.runName) then "run"
                elif not (isNull header.projectName) then "project"
                elif not (isNull header.moduleName) then "module"
                elif not (isNull header.workspaceName) then "workspace"
                else ""
            if actual.Length = 0 then raise (InvalidDataException($"Unrecognized entity definition: {path}"))
            actual = kind

    let All folder kind =
        if not (Directory.Exists(folder)) then [||]
        else
            if File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint) then
                raise (IOException($"Linked entity folders are not supported: {folder}"))
            let paths =
                Directory.EnumerateFileSystemEntries(folder)
                |> Seq.filter (fun path -> matches path kind)
                |> Seq.sortWith (fun a b -> StringComparer.OrdinalIgnoreCase.Compare(a, b))
                |> Seq.toArray
            paths |> Array.iter checkFile
            paths

    let Find folder kind =
        match All folder kind with
        | [||] -> null
        | [| path |] -> path
        | _ -> raise (IOException($"Multiple {kind} definition files found in {folder}. Keep one definition in this folder."))

    let Require folder kind =
        match Find folder kind with
        | null -> raise (FileNotFoundException($"Missing {kind} definition in {folder}."))
        | path -> path

    let Version folder kind =
        Require folder kind |> WorkspaceFileSystem.readDefinition |> MessagePackSetup.deserialize<Header> |> _.schemaVersion

    /// Identify an immediate child folder without treating arbitrary data files as definitions.
    let TryKind folder =
        if not (Directory.Exists folder) then null
        else
            WorkspaceFileSystem.checkDirectory folder
            let kinds =
                Directory.GetFiles(folder, "*.msgpack") |> Array.filter (Path.GetFileName >> HasGuid)
                |> Array.choose (fun path ->
                    checkFile path
                    try
                        let header = WorkspaceFileSystem.readDefinition path |> MessagePackSetup.deserialize<Header>
                        if isNull (box header) then None
                        elif not (isNull header.workspaceName) then Some "workspace"
                        elif not (isNull header.moduleName) then Some "module"
                        elif not (isNull header.projectName) then Some "project"
                        elif not (isNull header.runName) then Some "run"
                        else None
                    with :? MessagePack.MessagePackSerializationException -> None)
                |> Array.distinct
            match kinds with
            | [||] -> null
            | [| kind |] -> kind
            | _ -> raise (InvalidDataException($"Different entity types share one folder: {folder}."))
