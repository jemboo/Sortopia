namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Runtime.CompilerServices
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem

module internal WorkspaceDisk =
    // In-memory optimistic concurrency baseline; never a tracking file. Comparing
    // only resolved settings would miss edits to captured defaults hidden by a parent.
    let private loadedFiles = ConditionalWeakTable<obj, (string * string * string) array>()
    let private loadedDocuments = ConditionalWeakTable<WorkspaceDocument, WorkspaceDocument>()
    let private digest bytes = SHA256.HashData(bytes: byte array) |> Convert.ToHexString
    let private remember (value: obj) files =
        loadedFiles.Remove value |> ignore
        loadedFiles.Add(value, files)
        match value with
        | :? WorkspaceDocument as document ->
            loadedDocuments.Remove document |> ignore
            loadedDocuments.Add(document, MessagePackSetup.serialize document |> MessagePackSetup.deserialize<WorkspaceDocument>)
        | _ -> ()
    let documentBaseline value =
        match loadedDocuments.TryGetValue value with true, snapshot -> snapshot | _ -> value
    let verifySavedFiles (value: obj) =
        match loadedFiles.TryGetValue value with
        | true, files ->
            for path, kind, hash in files do
                // The editor explicitly supports restoring missing definitions
                // from its loaded tree. Existing files must still be unchanged.
                let replaced =
                    if File.Exists path then digest (readDefinition path) <> hash
                    elif kind = "run" || not (Directory.Exists(Path.GetDirectoryName path)) then false
                    else
                        ["workspace"; "module"; "project"] |> List.exists (fun kind ->
                            not (isNull (WorkspaceDefinitionFiles.Find (Path.GetDirectoryName path) kind)))
                if replaced then
                    raise (IOException($"The saved workspace changed since it was loaded: {path}. Reopen the workspace before saving again."))
            true
        | _ -> false

    let folderName rootFolder =
        match Path.GetFileName(normalizeRoot rootFolder) with
        | "" -> "Workspace"
        | name -> name

    let rebase (value: workspace) root = workspace.create value.Id value.WorkspaceName (folderName root) value.Modules
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
        member _.Files = files |> Seq.map (fun (path, hash) -> path, kinds[path], hash) |> Seq.toArray
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

    let private readProjectFolder folder depth (reader: Reader) parent expected =
        let path, bytes = definition folder "project" depth reader
        let dto = WorkspaceDefinitionCodec.decodeProject bytes
        reader.Identity(path, "project", dto.id, expected)
        validateFolderName folder "project" dto.projectName
        if not dto.isSealed && dto.runs.Length > 0 then invalidOp $"Project '{dto.projectName}' is a saved draft. Complete its configuration and run inputs before generating run parameters."
        let saved = WorkspaceDefinitionCodec.projectHeader parent dto
        let runs = dto.runs |> Array.map (fun id ->
            let file = Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name id)
            if not (File.Exists file) then raise (FileNotFoundException($"Missing referenced run {id} in project '{dto.projectName}'.", file))
            let value = WorkspaceCodec.decodeRun saved (reader.Read file)
            reader.Identity(file, "run", UMX.untag value.Id, Some id)
            value)
        project.restore saved.Id saved.ProjectName
            (if saved.IsSealed then saved.Configuration else saved.InheritedDefaults) saved.LocalOverrides saved.IsSealed runs

    let rec private readModuleFolder folder depth (reader: Reader) parent expected fromWorkspace =
        let path, bytes = definition folder "module" depth reader
        let dto = WorkspaceDefinitionCodec.decodeModule bytes
        reader.Identity(path, "module", dto.id, expected)
        validateFolderName folder "module" dto.moduleName
        let parent = if fromWorkspace then Some (ModuleDefaults.configuration (WorkspaceDefinitionCodec.kind dto.moduleKind = hierarchyKind.Branch)) else parent
        let saved = WorkspaceDefinitionCodec.moduleHeader parent dto
        let modules = dto.modules |> Array.map (fun reference ->
            readModuleFolder (referenceFolder folder "module" reference) (depth + 1) reader (Some saved.Configuration) (Some reference.id) false)
        let projects = dto.projects |> Array.map (fun reference ->
            readProjectFolder (referenceFolder folder "project" reference) (depth + 1) reader (Some (ModuleDefaults.toProject saved.Configuration)) (Some reference.id))
        workspaceModule.createInherited saved.Id saved.ModuleName saved.InheritedDefaults saved.LocalOverrides modules projects

    let private readEntityFolder folder token read =
        let root = normalizeRoot folder
        if not (Directory.Exists(root)) then raise (DirectoryNotFoundException($"Entity folder does not exist: {root}"))
        let reader = Reader(token)
        let result = read root reader
        reader.Verify()
        result

    let loadModuleFolder folder token = readEntityFolder folder token (fun root reader -> readModuleFolder root 0 reader None None false)
    let loadProjectFolder folder token = readEntityFolder folder token (fun root reader -> readProjectFolder root 0 reader None None)
    let loadDefinition rootFolder = readEntityFolder rootFolder CancellationToken.None (fun root reader ->
        let path, bytes = definition root "workspace" 0 reader
        let dto = WorkspaceDefinitionCodec.decodeWorkspace bytes
        reader.Identity(path, "workspace", dto.id, None)
        let modules = dto.modules |> Array.map (fun reference ->
            // The workspace supplies no shared defaults. Top-level modules begin inheritance.
            readModuleFolder (referenceFolder root "module" reference) 1 reader None (Some reference.id) true)
        let value = workspace.create (UMX.tag dto.id) (UMX.tag dto.workspaceName) (folderName root) modules
        remember value reader.Files
        value)

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
        { Definition = dto; Runs = runs }

    let rec private readModuleDocument folder depth (reader: Reader) expected : ModuleDocument =
        let path, bytes = definition folder "module" depth reader
        let dto = WorkspaceDefinitionCodec.decodeModule bytes
        reader.Identity(path, "module", dto.id, expected)
        validateFolderName folder "module" dto.moduleName
        { Definition = dto
          Modules = dto.modules |> Array.map (fun reference -> readModuleDocument (referenceFolder folder "module" reference) (depth + 1) reader (Some reference.id))
          Projects = dto.projects |> Array.map (fun reference -> readProjectDocument (referenceFolder folder "project" reference) (depth + 1) reader (Some reference.id)) }

    let loadDocument rootFolder =
        let root = normalizeRoot rootFolder
        let reader = Reader(CancellationToken.None)
        let path, bytes = definition root "workspace" 0 reader
        let dto = WorkspaceDefinitionCodec.decodeWorkspace bytes
        reader.Identity(path, "workspace", dto.id, None)
        let value = WorkspaceDocument.validate {
            Definition = { dto with folderName = folderName root }
            Modules = dto.modules |> Array.map (fun reference -> readModuleDocument (referenceFolder root "module" reference) 1 reader (Some reference.id)) }
        reader.Verify()
        remember value reader.Files
        value

    let rememberSavedDocument root (value: WorkspaceDocument) =
        let files = ResizeArray<string * string * string>()
        let add folder kind id value = files.Add(Path.Combine(folder, WorkspaceDefinitionFiles.Name id), kind, digest (MessagePackSetup.serialize value))
        let rec visit folder (value: ModuleDocument) =
            add folder "module" value.Definition.id value.Definition
            for m in value.Modules do visit (child folder m.Definition.moduleName) m
            for p in value.Projects do
                let projectFolder = child folder p.Definition.projectName
                add projectFolder "project" p.Definition.id p.Definition
                for r in p.Runs do add (Path.Combine(projectFolder, "runs")) "run" r.id r
        add root "workspace" value.Definition.id value.Definition
        for m in value.Modules do visit (child root m.Definition.moduleName) m
        remember value (files.ToArray())

    let rememberSavedFiles root (value: workspace) =
        let files = ResizeArray<string * string * string>()
        let add folder kind id bytes =
            let path = Path.Combine(folder, WorkspaceDefinitionFiles.Name id)
            files.Add(path, kind, digest bytes)
        let rec visit folder inheritedFrom (value: workspaceModule) =
            add folder "module" (UMX.untag value.Id) (WorkspaceDefinitionCodec.moduleWithOrigin inheritedFrom value |> MessagePackSetup.serialize)
            let origin = { moduleId = UMX.untag value.Id; moduleName = string value.ModuleName }
            for m in value.Modules do visit (child folder (UMX.untag m.ModuleName)) origin m
            for p in value.Projects do
                let projectFolder = child folder (UMX.untag p.ProjectName)
                add projectFolder "project" (UMX.untag p.Id) (WorkspaceDefinitionCodec.projectWithOrigin origin p |> MessagePackSetup.serialize)
                for r in p.Runs do add (Path.Combine(projectFolder, "runs")) "run" (UMX.untag r.Id) (WorkspaceCodec.encodeRun r)
        add root "workspace" (UMX.untag value.Id) (WorkspaceDefinitionCodec.encodeWorkspace value)
        for m in value.Modules do visit (child root (UMX.untag m.ModuleName)) Unchecked.defaultof<_> m
        remember value (files.ToArray())
