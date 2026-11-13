namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem
open WorkspaceDisk

type private Transfer = { Source: string; Destination: string; Copy: WorkspaceFolderCopy option }

module internal WorkspaceSavePlan =
    let prepareWithContainers rootFolder definition (token: CancellationToken) (copies: IReadOnlyList<WorkspaceFolderCopy>) (moves: IReadOnlyList<WorkspaceFolderMove>) (emptyContainers: string array) =
        let root = normalizeRoot rootFolder
        let before = capture root token "workspace"
        let definition = rebase definition root
        let expected = HashSet<string>(pathComparer)
        let writes = ResizeArray<WorkspaceFileUpdate>()
        let retainedFolders = ResizeArray<string>()
        let imports = if isNull copies then [||] else copies |> Seq.sortBy (fun copy -> copy.DestinationFolder.Length) |> Seq.toArray
        let relocations = if isNull moves then [||] else moves |> Seq.filter (fun move -> not (String.Equals(move.SourceFolder, move.DestinationFolder, StringComparison.Ordinal))) |> Seq.toArray
        let transfers =
            Array.append
                (imports |> Array.map (fun copy -> { Source = copy.SourceFolder; Destination = copy.DestinationFolder; Copy = Some copy }))
                (relocations |> Array.map (fun move -> { Source = move.SourceFolder; Destination = move.DestinationFolder; Copy = None }))
            |> Array.sortBy (fun transfer -> transfer.Destination.Length)
        if HashSet<string>(transfers |> Array.map _.Destination, pathComparer).Count <> transfers.Length then
            raise (IOException("Multiple entities target the same destination folder."))
        if HashSet<string>(relocations |> Array.map _.SourceFolder, pathComparer).Count <> relocations.Length then
            raise (IOException("A folder cannot be moved to more than one destination."))
        for move in relocations do
            WorkspaceFolderMove.CheckPath(root, move.SourceFolder)
            WorkspaceFolderMove.CheckPath(root, move.DestinationFolder)
            if not (Directory.Exists(move.SourceFolder) || File.Exists(move.SourceFolder)) then
                raise (DirectoryNotFoundException($"The folder to move is missing: {move.SourceFolder}"))
        for copy in imports do
            copy.ValidateDestination(root)
            copy.ValidateSource(token)
        for transfer in transfers do
            let enclosing = transfers |> Array.tryFindBack (fun other -> other <> transfer && WorkspaceFolderCopy.Inside(other.Destination, transfer.Destination))
            let existing =
                match enclosing with
                | None -> transfer.Destination
                | Some parent -> Path.Combine(parent.Source, Path.GetRelativePath(parent.Destination, transfer.Destination))
            let vacated =
                (relocations |> Array.exists (fun move -> WorkspaceFolderCopy.Inside(move.SourceFolder, existing)))
                || (enclosing |> Option.bind _.Copy |> Option.exists (fun copy -> not (copy.IncludesSourcePath(existing))))
                || (emptyContainers |> Array.exists (fun path -> pathComparer.Equals(path, transfer.Destination)))
            if not vacated && (Directory.Exists(existing) || File.Exists(existing)) then
                raise (IOException($"The destination already exists. Choose a different parent or folder name: {transfer.Destination}"))

        let transferFor path = transfers |> Array.tryFindBack (fun transfer -> WorkspaceFolderCopy.Inside(transfer.Destination, path))
        let sourceIds = Dictionary<Guid, Guid>()
        let copiedIds = Dictionary<struct (Guid * Guid), Guid>()
        let copyId id path =
            match transferFor path |> Option.bind _.Copy with
            | None -> id
            | Some copy ->
                match copy.OriginalIds.TryGetValue(id) with
                | true, original -> sourceIds[id] <- original; id
                | _ ->
                    let key = struct (copy.Id, id)
                    match copiedIds.TryGetValue(key) with
                    | true, replacement -> replacement
                    | _ ->
                        let replacement = Guid.NewGuid()
                        copiedIds.Add(key, replacement)
                        sourceIds[replacement] <- id
                        replacement
        let rec allocateModule (value: workspaceModule) folder =
            copyId (UMX.untag value.Id) folder |> ignore
            for value in value.Modules do allocateModule value (child folder value.FolderName)
            for value in value.Projects do copyId (UMX.untag value.Id) (child folder value.FolderName) |> ignore
        for value in definition.Modules do allocateModule value (child root value.FolderName)
        let remapSource folder id =
            match transferFor folder |> Option.bind _.Copy with
            | None -> id
            | Some copy -> copiedIds.GetValueOrDefault(struct (copy.Id, id), id)
        let remapSources folder (entries: setupDataTypeConfigDto array) =
            if isNull entries then null else entries |> Array.map (fun entry -> { entry with sourceProjectId = remapSource folder entry.sourceProjectId })
        let remapAssignments folder (entries: setupSourceConfigDto array) =
            if isNull entries then null else entries |> Array.map (fun entry -> { entry with sourceProjectId = remapSource folder entry.sourceProjectId })
        let identifyProject (value: project) folder =
            let settings = SettingsDto.fromDomain (if value.IsSealed then value.Configuration else value.InheritedDefaults)
            let local = OverridesDto.fromDomain value.LocalOverrides
            let settings = { settings with setupDataTypes = remapSources folder settings.setupDataTypes }
            let local = { local with setupSources = remapAssignments folder local.setupSources }
            let parent = project.restore (copyId (UMX.untag value.Id) folder |> UMX.tag) value.ProjectName value.FolderName
                             (SettingsDto.toDomain value.Kind settings) (OverridesDto.toDomain value.Kind local) value.IsSealed [||]
            let runs = value.Runs |> Array.map (fun run ->
                let dto = RunDto.fromDomain run
                { dto with id = copyId dto.id (Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name dto.id)); parentProjectId = UMX.untag parent.Id }
                |> RunDto.toDomain parent)
            project.withRuns parent runs
        let rec identifyModule (value: workspaceModule) folder =
            let settings = ModuleSettingsDto.fromDomain value.InheritedDefaults
            let local = ModuleOverridesDto.fromDomain value.LocalOverrides
            let settings = { settings with setupDataTypes = remapSources folder settings.setupDataTypes }
            let local = { local with setupSources = remapAssignments folder local.setupSources }
            workspaceModule.createInherited (copyId (UMX.untag value.Id) folder |> UMX.tag) value.ModuleName value.FolderName
                (ModuleSettingsDto.toDomain value.Kind settings) (ModuleOverridesDto.toDomain value.Kind local)
                (value.Modules |> Array.map (fun value -> identifyModule value (child folder value.FolderName)))
                (value.Projects |> Array.map (fun value -> identifyProject value (child folder value.FolderName)))
        let definition = workspace.create definition.Id definition.WorkspaceName definition.FolderName
                             (definition.Modules |> Array.map (fun value -> identifyModule value (child root value.FolderName)))

        let findRun projectFolder id =
            let path = Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name id)
            if File.Exists path then path else null
        let add folder kind id (bytes: byte array) =
            token.ThrowIfCancellationRequested()
            if bytes.Length > maxDefinitionBytes then raise (InvalidDataException($"Definition is too large: {folder}"))
            let path = Path.Combine(folder, WorkspaceDefinitionFiles.Name id)
            expected.Add(if kind = "run" then path else folder) |> ignore
            if expected.Count > maxEntities then raise (InvalidDataException("Workspace contains too many entities."))
            let sourceId = sourceIds.GetValueOrDefault(id, id)
            let oldPath = Path.Combine(folder, WorkspaceDefinitionFiles.Name sourceId)
            let transfer = transferFor (if kind = "run" then oldPath else folder)
            let sourceFolder =
                match transfer with
                | None -> folder
                | Some transfer ->
                    let sourceFolder = Path.Combine(transfer.Source, Path.GetRelativePath(transfer.Destination, folder))
                    // A child cut out of a moved parent is no longer part of that parent's physical contents.
                    if relocations |> Array.exists (fun move ->
                        not (pathComparer.Equals(move.SourceFolder, transfer.Source))
                        && WorkspaceFolderCopy.Inside(transfer.Source, move.SourceFolder)
                        && WorkspaceFolderCopy.Inside(move.SourceFolder, sourceFolder)) then folder
                    else sourceFolder
            let copy = transfer |> Option.bind _.Copy
            let sourcePath =
                if kind = "run" then
                    let sourcePath =
                        match transfer with
                        | Some transfer when File.Exists(transfer.Source) -> transfer.Source
                        | _ -> findRun (Path.GetDirectoryName(sourceFolder)) sourceId
                    if not (isNull sourcePath) && (copy |> Option.exists (fun copy -> not (copy.IncludesSourcePath(sourcePath)))) then null else sourcePath
                elif copy |> Option.exists (fun copy -> not (copy.IncludesSourcePath(sourceFolder))) then null
                else WorkspaceDefinitionFiles.Find sourceFolder kind
            if kind <> "run" then
                match WorkspaceDefinitionFiles.TryKind sourceFolder with
                | null -> ()
                | actual when actual = kind -> ()
                | actual -> raise (IOException($"A {actual} already occupies the requested {kind} folder: {folder}."))
            let original = if isNull sourcePath then null else readDefinition sourcePath
            let originalPath =
                if isNull sourcePath then null
                else
                    match transfer with
                    | None -> sourcePath
                    // File transfers publish to a path, whereas folder transfers publish a subtree.
                    | Some transfer when File.Exists(transfer.Source) -> transfer.Destination
                    | Some transfer -> Path.GetFullPath(Path.Combine(transfer.Destination, Path.GetRelativePath(transfer.Source, sourcePath)))
            if not (pathComparer.Equals(path, originalPath) && not (isNull original) && equalBytes original bytes) then
                writes.Add({ Path = path; Content = bytes; OriginalPath = originalPath; Original = original })
        let addProject inheritedFrom (value: project) folder depth =
            checkDepth depth
            for run in value.Runs do add (Path.Combine(folder, "runs")) "run" (UMX.untag run.Id) (WorkspaceCodec.encodeRun run)
            add folder "project" (UMX.untag value.Id) (WorkspaceDefinitionCodec.projectWithOrigin inheritedFrom value |> MessagePackSetup.serialize)
        let rec addModule inheritedFrom (value: workspaceModule) folder depth =
            checkDepth depth
            let origin = { moduleId = UMX.untag value.Id; moduleName = string value.ModuleName }
            for value in value.Modules do addModule origin value (child folder value.FolderName) (depth + 1)
            for value in value.Projects do addProject origin value (child folder value.FolderName) (depth + 1)
            add folder "module" (UMX.untag value.Id) (WorkspaceDefinitionCodec.moduleWithOrigin inheritedFrom value |> MessagePackSetup.serialize)
        for value in definition.Modules do addModule Unchecked.defaultof<_> value (child root value.FolderName) 1
        add root "workspace" (UMX.untag definition.Id) (WorkspaceDefinitionCodec.encodeWorkspace definition) // Root references commit last.
        let hasOriginal path = writes |> Seq.exists (fun write -> pathComparer.Equals(write.OriginalPath, path))
        if transfers |> Array.exists (fun transfer -> not (expected.Contains(transfer.Destination)) && not (File.Exists(transfer.Source) && hasOriginal transfer.Destination)) then
            raise (ArgumentException("Every moved or imported folder must belong to an entity in the workspace definition."))
        for copy in imports do
            if not (File.Exists(copy.SourceFolder)) then
                let kind = WorkspaceDefinitionFiles.TryKind copy.SourceFolder
                if kind <> "module" && kind <> "project" then invalidOp $"Cannot determine imported folder type: {copy.SourceFolder}."
                for sourceEntity in (capture copy.SourceFolder token kind).Entities do
                    if copy.IncludesSourcePath(sourceEntity) then
                        let target = Path.GetFullPath(Path.Combine(copy.DestinationFolder, relative copy.SourceFolder sourceEntity))
                        if not (expected.Contains(target) || hasOriginal target) then retainedFolders.Add(relative root target)
        for existing in before.Entities do
            let move = relocations |> Array.filter (fun move -> WorkspaceFolderCopy.Inside(move.SourceFolder, existing)) |> Array.sortByDescending (fun move -> move.SourceFolder.Length) |> Array.tryHead
            let target =
                match move with
                | None -> existing
                | Some move -> Path.GetFullPath(Path.Combine(move.DestinationFolder, relative move.SourceFolder existing))
            if not (expected.Contains(target) || hasOriginal target) then retainedFolders.Add(relative root target)
        if before.Fingerprint <> (capture root token "workspace").Fingerprint then
            raise (IOException("Folders changed while preparing the save. Try saving again."))
        { RootFolder = root; Definition = definition; Fingerprint = before.Fingerprint; Writes = writes.ToArray()
          RetainedFolders = retainedFolders.ToArray(); Copies = imports; Moves = relocations; EmptyContainers = emptyContainers }

    let prepare rootFolder definition token copies moves = prepareWithContainers rootFolder definition token copies moves [||]
