namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem

module internal WorkspaceDisk =
    let private digest bytes = SHA256.HashData(bytes: byte array) |> Convert.ToHexString
    let verifySavedFiles (saved: SavedWorkspace) =
        for file in saved.Files do
            // Missing definitions can be restored from the saved revision. Existing
            // files, including captured values hidden by inheritance, must be unchanged.
            let replaced =
                if File.Exists file.Path then digest (readDefinition file.Path) <> file.Hash
                elif file.Kind = "run" || not (Directory.Exists(Path.GetDirectoryName file.Path)) then false
                else
                    ["workspace"; "module"; "project"] |> List.exists (fun kind ->
                        not (isNull (WorkspaceDefinitionFiles.Find (Path.GetDirectoryName file.Path) kind)))
            if replaced then
                raise (IOException($"The saved workspace changed since it was loaded: {file.Path}. Reopen the workspace before saving again."))

    let folderName rootFolder =
        match Path.GetFileName(normalizeRoot rootFolder) with
        | "" -> "Workspace"
        | name -> name

    let containers = function
        | "project" -> [| "runs" |]
        | _ -> [||]

    let children folder container =
        let path = Path.Combine(folder, container)
        if not (Directory.Exists(path)) then [||]
        else
            checkDirectory path
            Directory.GetDirectories(path) |> Array.sortWith (fun a b -> pathComparer.Compare(a, b))

    let child folder name = Path.Combine(folder, FolderName.validate name)
    let runFiles projectFolder = WorkspaceDefinitionFiles.All (Path.Combine(projectFolder, "runs")) "run"

    let capture root (token: CancellationToken) rootKind =
        token.ThrowIfCancellationRequested()
        let entries = ResizeArray<string>()
        let entities = ResizeArray<string>()
        let addEntity path =
            entities.Add(path)
            if entities.Count > maxEntities then raise (InvalidDataException("Workspace contains too many entities."))
        let addFile path =
            let hash = readDefinition path |> SHA256.HashData |> Convert.ToHexString
            entries.Add("F:" + relative root path + ":" + hash)
        let rec visit folder kind depth =
            token.ThrowIfCancellationRequested()
            checkDepth depth
            checkDirectory folder
            addEntity folder
            entries.Add("D:" + relative root folder)
            // Fingerprint physical files without adopting or decoding unlisted
            // entities. Membership is determined solely by child references.
            for file in Directory.GetFiles(folder) |> Array.filter (Path.GetFileName >> WorkspaceDefinitionFiles.HasGuid) |> Array.sort do
                addFile file
            for container in containers kind do
                let path = Path.Combine(folder, container)
                if File.Exists(path) then raise (IOException($"A file occupies an entity container: {path}"))
                if Directory.Exists(path) then
                    checkDirectory path
                    entries.Add("C:" + relative root path)
                    if container = "runs" then
                        for file in Directory.GetFiles(path) |> Array.filter (Path.GetFileName >> WorkspaceDefinitionFiles.HasGuid) |> Array.sort do
                            addFile file
                            addEntity file
                    else
                        for child in children folder container do visit child (container.Substring(0, container.Length - 1)) (depth + 1)
            if kind = "workspace" || kind = "module" then
                for folder in Directory.GetDirectories(folder) |> Array.sort do
                    if not (Path.GetFileName(folder).StartsWith(".sortopia-", StringComparison.OrdinalIgnoreCase)) then
                        checkDirectory folder
                        match WorkspaceDefinitionFiles.TryKind folder with
                        | "module" -> visit folder "module" (depth + 1)
                        | "project" -> visit folder "project" (depth + 1)
                        | _ -> entries.Add("D:" + relative root folder)
        if File.Exists(root) then raise (IOException("Workspace root must be a folder."))
        if Directory.Exists(root) then visit root rootKind 0
        { Fingerprint = String.Join('\n', entries) |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
          Entities = entities.ToArray() }

    let private validateEntityFolder folder kind =
        checkDirectory folder
        for other in [| "workspace"; "module"; "project"; "run" |] do
            if other <> kind && not (isNull (WorkspaceDefinitionFiles.Find folder other)) then
                raise (InvalidDataException($"Expected a {kind} folder, but found a {other} definition in {folder}."))
        if kind = "project" then
            for child in Directory.GetDirectories folder |> Array.filter (fun path -> not (List.contains (Path.GetFileName(path).ToLowerInvariant()) ["data"; "runs"; "runparameters"])) do
                match WorkspaceDefinitionFiles.TryKind child with
                | "module" | "project" -> raise (InvalidDataException($"A project cannot contain another module or project: {child}."))
                | _ -> ()

    type private Reader(token: CancellationToken) =
        let ids = Collections.Generic.HashSet<struct (string * Guid)>()
        let files = ResizeArray<string * string>()
        let kinds = Collections.Generic.Dictionary<string, string>(pathComparer)
        member _.Files = files |> Seq.map (fun (path, hash) -> { Path = path; Kind = kinds[path]; Hash = hash }) |> Seq.toArray
        member _.Token = token
        member _.Read(path) =
            token.ThrowIfCancellationRequested()
            let bytes = readDefinition path
            files.Add(path, digest bytes)
            if files.Count > maxEntities then raise (InvalidDataException("Workspace contains too many entities."))
            bytes
        member _.Identity(path: string, kind: string, id: Guid, expected: Guid option) =
            kinds[path] <- kind
            EntityId.validate id
            if not (ids.Add(struct (kind, id))) then raise (InvalidDataException($"Duplicate {kind} ID: {id}"))
            if Path.GetFileName(path) <> WorkspaceDefinitionFiles.Name id then
                raise (InvalidDataException($"Definition ID does not match its filename: {path}"))
            if expected |> Option.exists ((<>) id) then
                raise (InvalidDataException($"Child reference does not match the definition ID in {path}."))
        member _.Verify() =
            for path, hash in files do
                token.ThrowIfCancellationRequested()
                if digest (readDefinition path) <> hash then
                    raise (IOException($"Entity definition changed while being read: {path}. Reopen the folder."))

    let private referenceFolder folder kind (reference: childReferenceDto) =
        checkDirectory folder
        let path = child folder reference.name
        if not (Directory.Exists path) then raise (DirectoryNotFoundException($"Missing referenced {kind} folder '{reference.name}' ({reference.id}) in {folder}."))
        path

    let private validateFolderName (folder: string) kind name =
        if Path.GetFileName(folder) <> name then
            raise (InvalidDataException($"The {kind} name '{name}' does not match its folder '{folder}'. Rename the folder to '{name}' before opening or importing it."))

    let private definition folder kind depth (reader: Reader) =
        reader.Token.ThrowIfCancellationRequested()
        checkDepth depth
        validateEntityFolder folder kind
        for container in containers kind do
            let path = Path.Combine(folder, container)
            if File.Exists path then raise (IOException($"A file occupies an entity container: {path}"))
            if Directory.Exists path then checkDirectory path
        let path = WorkspaceDefinitionFiles.Require folder kind
        path, reader.Read path

    let private readProjectDocument folder depth (reader: Reader) expected : ProjectDocument =
        let path, bytes = definition folder "project" depth reader
        let dto = WorkspaceDefinitionCodec.decodeProject bytes
        reader.Identity(path, "project", dto.id, expected)
        validateFolderName folder "project" dto.projectName
        let runs = dto.runs |> Array.map (fun id ->
            let file = Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name id)
            if not (File.Exists file) then raise (FileNotFoundException($"Missing referenced run {id} in project '{dto.projectName}'.", file))
            let run = reader.Read file |> MessagePackSetup.deserialize<runDto>
            RunDto.readId run |> ignore
            reader.Identity(file, "run", run.id, Some id)
            run)
        { Definition = WorkspaceDefinitionCodec.projectDefinition dto; Runs = runs }

    let rec private readModuleDocument folder depth (reader: Reader) expected : ModuleDocument =
        let path, bytes = definition folder "module" depth reader
        let dto = WorkspaceDefinitionCodec.decodeModule bytes
        reader.Identity(path, "module", dto.id, expected)
        validateFolderName folder "module" dto.moduleName
        { Definition = WorkspaceDefinitionCodec.moduleDefinition dto
          Modules = dto.modules |> Array.map (fun reference -> readModuleDocument (referenceFolder folder "module" reference) (depth + 1) reader (Some reference.id))
          Projects = dto.projects |> Array.map (fun reference -> readProjectDocument (referenceFolder folder "project" reference) (depth + 1) reader (Some reference.id)) }

    let private readEntityDocument folder token read validate =
        let root = normalizeRoot folder
        if not (Directory.Exists(root)) then raise (DirectoryNotFoundException($"Entity folder does not exist: {root}"))
        let reader = Reader(token)
        let result = read root 0 reader None |> validate
        reader.Verify()
        result

    let loadModuleDocument folder token = readEntityDocument folder token readModuleDocument WorkspaceDocument.validateModule
    let loadProjectDocument folder token = readEntityDocument folder token readProjectDocument WorkspaceDocument.validateProject
    let loadWorkspaceWithFolderName rootFolder name =
        let root = normalizeRoot rootFolder
        let reader = Reader(CancellationToken.None)
        let path, bytes = definition root "workspace" 0 reader
        let dto = WorkspaceDefinitionCodec.decodeWorkspace bytes
        reader.Identity(path, "workspace", dto.id, None)
        let value = WorkspaceDocument.validate {
            Definition = { WorkspaceDefinitionCodec.workspaceDefinition dto with folderName = name }
            Modules = dto.modules |> Array.map (fun reference -> readModuleDocument (referenceFolder root "module" reference) 1 reader (Some reference.id)) }
        reader.Verify()
        SavedWorkspace(root, value, reader.Files)

    let loadWorkspace rootFolder = loadWorkspaceWithFolderName rootFolder (folderName rootFolder)

    let savedWorkspace root (value: WorkspaceDocument) =
        let files = ResizeArray<SavedDefinitionFile>()
        let add folder kind id value = files.Add { Path = Path.Combine(folder, WorkspaceDefinitionFiles.Name id); Kind = kind; Hash = digest (MessagePackSetup.serialize value) }
        let rec visit folder (value: ModuleDocument) =
            add folder "module" value.Definition.id (WorkspaceDocument.moduleFile value)
            for m in value.Modules do visit (child folder m.Definition.moduleName) m
            for p in value.Projects do
                let projectFolder = child folder p.Definition.projectName
                add projectFolder "project" p.Definition.id (WorkspaceDocument.projectFile p)
                for r in p.Runs do add (Path.Combine(projectFolder, "runs")) "run" r.id r
        add root "workspace" value.Definition.id (WorkspaceDocument.workspaceFile value)
        for m in value.Modules do visit (child root m.Definition.moduleName) m
        SavedWorkspace(root, value, files.ToArray())
