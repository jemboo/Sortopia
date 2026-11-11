namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open WorkspaceFileSystem

module internal WorkspaceDisk =
    let folderName rootFolder =
        match Path.GetFileName(normalizeRoot rootFolder) with
        | "" -> "Workspace"
        | name -> name

    let rebase (value: workspace) root = workspace.create value.Id value.WorkspaceName (folderName root) value.Modules
    let loadDefinition rootFolder =
        let root = normalizeRoot rootFolder
        checkDirectory root
        let saved = WorkspaceDefinitionFiles.Require root "workspace" |> readDefinition |> WorkspaceCodec.decodeWorkspace
        rebase saved root

    let containers = function
        | "workspace" -> [| "modules" |]
        | "module" -> [| "modules"; "projects" |]
        | "project" -> [| "runs" |]
        | _ -> [||]

    let children folder container =
        let path = Path.Combine(folder, container)
        if not (Directory.Exists(path)) then [||]
        else
            checkDirectory path
            Directory.GetDirectories(path) |> Array.sortWith (fun a b -> pathComparer.Compare(a, b))

    let child folder container name = Path.Combine(folder, container, FolderName.validate name)
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
            let marker = WorkspaceDefinitionFiles.Find folder kind
            if not (isNull marker) then addFile marker
            for container in containers kind do
                let path = Path.Combine(folder, container)
                if File.Exists(path) then raise (IOException($"A file occupies an entity container: {path}"))
                if Directory.Exists(path) then
                    checkDirectory path
                    entries.Add("C:" + relative root path)
                    if container = "runs" then
                        for file in WorkspaceDefinitionFiles.All path "run" do
                            addFile file
                            addEntity file
                    else
                        for child in children folder container do visit child (container.Substring(0, container.Length - 1)) (depth + 1)
        if File.Exists(root) then raise (IOException("Workspace root must be a folder."))
        if Directory.Exists(root) then visit root rootKind 0
        { Fingerprint = String.Join('\n', entries) |> Encoding.UTF8.GetBytes |> SHA256.HashData |> Convert.ToHexString
          Entities = entities.ToArray() }

    let private validateEntityFolder folder kind =
        checkDirectory folder
        for other in [| "workspace"; "module"; "project"; "run" |] do
            if other <> kind && not (isNull (WorkspaceDefinitionFiles.Find folder other)) then
                raise (InvalidDataException($"Expected a {kind} folder, but found a {other} definition in {folder}."))
        for container in [| "modules"; "projects"; "runs" |] do
            if not (Array.contains container (containers kind)) && Directory.Exists(Path.Combine(folder, container)) then
                raise (InvalidDataException($"A {kind} cannot contain {container}: {folder}."))

    let private readProjectFolder folder depth (token: CancellationToken) parent =
        token.ThrowIfCancellationRequested()
        checkDepth depth
        validateEntityFolder folder "project"
        let bytes = WorkspaceDefinitionFiles.Require folder "project" |> readDefinition
        let saved =
            match parent with
            | None -> WorkspaceCodec.decodeProject bytes
            | Some configuration -> WorkspaceCodec.decodeProjectInherited configuration bytes
        let runs = runFiles folder |> Array.map (fun file ->
            token.ThrowIfCancellationRequested()
            WorkspaceCodec.decodeRun saved (readDefinition file))
        project.restore saved.Id saved.ProjectName (Path.GetFileName(folder)) saved.Configuration saved.LocalOverrides saved.IsSealed runs

    let rec private readModuleFolder folder depth (token: CancellationToken) parent =
        token.ThrowIfCancellationRequested()
        checkDepth depth
        validateEntityFolder folder "module"
        let bytes = WorkspaceDefinitionFiles.Require folder "module" |> readDefinition
        let saved =
            match parent with
            | None -> WorkspaceCodec.decodeModule bytes
            | Some configuration -> WorkspaceCodec.decodeModuleInherited configuration bytes
        workspaceModule.createInherited saved.Id saved.ModuleName (Path.GetFileName(folder)) saved.Configuration saved.LocalOverrides
            (children folder "modules" |> Array.map (fun path -> readModuleFolder path (depth + 1) token (Some saved.Configuration)))
            (children folder "projects" |> Array.map (fun path -> readProjectFolder path (depth + 1) token (Some (ModuleDefaults.toProject saved.Configuration))))

    let private readEntityFolder folder kind token read =
        let root = normalizeRoot folder
        if not (Directory.Exists(root)) then raise (DirectoryNotFoundException("Choose an existing entity folder."))
        let before = capture root token kind
        let result = read root
        if before.Fingerprint <> (capture root token kind).Fingerprint then
            raise (IOException("The entity folders changed while being read. Add the folder again."))
        result

    let loadModuleFolder folder token = readEntityFolder folder "module" token (fun root -> readModuleFolder root 0 token None)
    let loadProjectFolder folder token = readEntityFolder folder "project" token (fun root -> readProjectFolder root 0 token None)
