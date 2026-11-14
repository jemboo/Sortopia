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
    let prepareWithContainers rootFolder (definition: WorkspaceDocument) (token: CancellationToken) (copies: IReadOnlyList<WorkspaceFolderCopy>) (moves: IReadOnlyList<WorkspaceFolderMove>) (emptyContainers: string array) =
        let root = normalizeRoot rootFolder
        let before = capture root token "workspace"
        let definition = WorkspaceDocument.validate { definition with Definition = { definition.Definition with folderName = folderName root } }
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
        let rec allocateModule (value: ModuleDocument) folder =
            copyId value.Definition.id folder |> ignore
            for value in value.Modules do allocateModule value (child folder value.Definition.folderName)
            for value in value.Projects do copyId value.Definition.id (child folder value.Definition.folderName) |> ignore
        for value in definition.Modules do allocateModule value (child root value.Definition.folderName)
        let remapSource folder id =
            match transferFor folder |> Option.bind _.Copy with
            | None -> id
            | Some copy -> copiedIds.GetValueOrDefault(struct (copy.Id, id), id)
        let remapSources folder (entries: setupDataTypeConfigDto array) =
            if isNull entries then null else entries |> Array.map (fun entry -> { entry with sourceProjectId = remapSource folder entry.sourceProjectId })
        let remapAssignments folder (entries: setupSourceConfigDto array) =
            if isNull entries then null else entries |> Array.map (fun entry -> { entry with sourceProjectId = remapSource folder entry.sourceProjectId })
        let settings folder (value: projectSettingsDto) =
            if isNull (box value) then value else { value with setupDataTypes = remapSources folder value.setupDataTypes }
        let identifyProject (value: ProjectDocument) folder : ProjectDocument =
            let dto = value.Definition
            let id = copyId dto.id folder
            let runs = value.Runs |> Array.map (fun run ->
                { run with id = copyId run.id (Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name run.id)); parentProjectId = id })
            let local = dto.localOverrides
            { Definition = { dto with id = id; runs = runs |> Array.map _.id
                                      configuration = settings folder dto.configuration; inheritedDefaults = settings folder dto.inheritedDefaults
                                      localOverrides = if isNull (box local) then local else { local with setupSources = remapAssignments folder local.setupSources } }
              Runs = runs }
        let rec identifyModule (value: ModuleDocument) folder : ModuleDocument =
            let dto = value.Definition
            let modules = value.Modules |> Array.map (fun value -> identifyModule value (child folder value.Definition.folderName))
            let projects = value.Projects |> Array.map (fun value -> identifyProject value (child folder value.Definition.folderName))
            { Definition = { dto with id = copyId dto.id folder
                                      inheritedDefaults = { dto.inheritedDefaults with setupDataTypes = remapSources folder dto.inheritedDefaults.setupDataTypes }
                                      localOverrides = { dto.localOverrides with setupSources = remapAssignments folder dto.localOverrides.setupSources }
                                      modules = modules |> Array.map (fun m -> { id = m.Definition.id; folderName = m.Definition.folderName })
                                      projects = projects |> Array.map (fun p -> { id = p.Definition.id; folderName = p.Definition.folderName }) }
              Modules = modules; Projects = projects }
        let modules = definition.Modules |> Array.map (fun value -> identifyModule value (child root value.Definition.folderName))
        let definition = { definition with Modules = modules; Definition = { definition.Definition with modules = modules |> Array.map (fun m -> { id = m.Definition.id; folderName = m.Definition.folderName }) } } |> WorkspaceDocument.withOrigins
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
        let addProject inheritedFrom (value: ProjectDocument) folder depth =
            checkDepth depth
            for run in value.Runs do add (Path.Combine(folder, "runs")) "run" run.id (MessagePackSetup.serialize run)
            let dto = { value.Definition with inheritedFrom = if value.Definition.isSealed then Unchecked.defaultof<_> else inheritedFrom }
            add folder "project" dto.id (MessagePackSetup.serialize dto)
        let rec addModule inheritedFrom (value: ModuleDocument) folder depth =
            checkDepth depth
            let origin = { moduleId = value.Definition.id; moduleName = value.Definition.moduleName }
            for value in value.Modules do addModule origin value (child folder value.Definition.folderName) (depth + 1)
            for value in value.Projects do addProject origin value (child folder value.Definition.folderName) (depth + 1)
            add folder "module" value.Definition.id (MessagePackSetup.serialize { value.Definition with inheritedFrom = inheritedFrom })
        for value in definition.Modules do addModule Unchecked.defaultof<_> value (child root value.Definition.folderName) 1
        add root "workspace" definition.Definition.id (MessagePackSetup.serialize definition.Definition) // Root references commit last.
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
          RetainedFolders = retainedFolders.ToArray(); Copies = imports; Moves = relocations; EmptyContainers = emptyContainers; Removals = [||] }

    let prepare rootFolder definition token copies moves = prepareWithContainers rootFolder definition token copies moves [||]

    let withRemovals previous (plan: SavePlan) token =
        let removed = WorkspaceRemoval.plan plan.RootFolder previous plan.Definition plan.Moves token
        { plan with Removals = removed
                    RetainedFolders = plan.RetainedFolders |> Array.filter (fun path ->
                        not (removed |> Array.exists (fun item -> WorkspaceFolderCopy.Inside(item.Destination, Path.Combine(plan.RootFolder, path))))) }
