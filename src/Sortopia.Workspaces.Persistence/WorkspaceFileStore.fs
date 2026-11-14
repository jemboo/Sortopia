namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem

/// Transactional workspace saves and explicit entity imports. Unlisted folders and user data are kept.
[<AbstractClass; Sealed>]
type WorkspaceFileStore private () =
    static member LoadDefinition(rootFolder: string) = WorkspaceDisk.loadDefinition rootFolder
    static member LoadDocument(rootFolder: string) = WorkspaceDisk.loadDocument rootFolder
    static member private RuntimeResult(result: WorkspaceDocumentSaveResult) =
        WorkspaceSaveResult(WorkspaceDocument.toDomain result.Definition, result.CopiedFolders, result.RetainedFolders, result.MovedFolders)
    static member GetFolderName(rootFolder: string) = WorkspaceDisk.folderName rootFolder
    static member LoadModuleFolder(folder: string, token: CancellationToken) = WorkspaceDisk.loadModuleFolder folder token
    static member LoadProjectFolder(folder: string, token: CancellationToken) = WorkspaceDisk.loadProjectFolder folder token

    /// Explicit, transactional conversion of the previous container-based workspace layout.
    /// All identities and project contents are preserved; collisions fail before publication.
    static member ConvertFolderLayout(rootFolder: string, token: CancellationToken) =
        let root = normalizeRoot rootFolder
        let definition = WorkspaceDisk.loadDefinitionLayout true token root
        let moves = ResizeArray<WorkspaceFolderMove>()
        let containers = ResizeArray<string>()
        containers.Add(Path.Combine(root, "modules"))
        let rec visit oldParent newParent (value: workspaceModule) =
            let source = Path.Combine(oldParent, "modules", value.FolderName)
            let destination = Path.Combine(newParent, value.FolderName)
            moves.Add({ SourceFolder = source; DestinationFolder = destination })
            containers.Add(Path.Combine(destination, "modules")); containers.Add(Path.Combine(destination, "projects"))
            for child in value.Modules do visit source destination child
            for child in value.Projects do
                moves.Add({ SourceFolder = Path.Combine(source, "projects", child.FolderName); DestinationFolder = Path.Combine(destination, child.FolderName) })
        for child in definition.Modules do visit root root child
        let plan = WorkspaceSavePlan.prepareWithContainers root (WorkspaceDocument.fromDomain definition) token null moves (containers.ToArray())
        WorkspaceDisk.verifySavedFiles definition |> ignore
        let result = WorkspaceCommit.commit plan token |> WorkspaceFileStore.RuntimeResult
        // Only remove obsolete, empty containers. Unlisted folders and user files stay intact.
        for folder in containers |> Seq.sortByDescending _.Length do
            WorkspaceFolderMove.CheckPath(root, folder)
            if Directory.Exists(folder) && Seq.isEmpty (Directory.EnumerateFileSystemEntries(folder)) then
                try Directory.Delete(folder) with :? IOException | :? UnauthorizedAccessException -> ()
        WorkspaceDisk.rememberSavedFiles root result.Definition
        result

    static member CreateWorkspace(rootFolder: string, definition: workspace, token: CancellationToken,
                                  [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                  [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        let root = normalizeRoot rootFolder
        let requireNewWorkspace () =
            if not (isNull (WorkspaceDefinitionFiles.Find root "workspace")) then
                raise (IOException("A workspace file already exists at this location. Reopen the workspace to load it."))
        requireNewWorkspace ()
        let plan = WorkspaceSavePlan.prepare rootFolder (WorkspaceDocument.fromDomain definition) token copies moves
        requireNewWorkspace ()
        let result = WorkspaceCommit.commit plan token |> WorkspaceFileStore.RuntimeResult
        WorkspaceDisk.rememberSavedFiles root result.Definition
        result

    static member SaveWorkspace(rootFolder: string, definition: workspace, savedDefinition: workspace, token: CancellationToken,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        token.ThrowIfCancellationRequested()
        let verified = WorkspaceDisk.verifySavedFiles savedDefinition
        let plan = WorkspaceSavePlan.prepare rootFolder (WorkspaceDocument.fromDomain definition) token copies moves
        // Catch edits arriving between the initial baseline check and capture.
        if verified then WorkspaceDisk.verifySavedFiles savedDefinition |> ignore
        if not verified && not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
            let current = WorkspaceDisk.loadDefinition rootFolder
            if not (equalBytes (WorkspaceCodec.encodeWorkspace current) (WorkspaceCodec.encodeWorkspace savedDefinition)) then
                raise (IOException("The saved workspace changed since it was loaded. Reopen the workspace before saving again."))
        WorkspaceFileStore.ValidateSealedProjects(savedDefinition, WorkspaceDocument.toDomain plan.Definition)
        let result = WorkspaceCommit.commit plan token |> WorkspaceFileStore.RuntimeResult
        WorkspaceDisk.rememberSavedFiles (normalizeRoot rootFolder) result.Definition
        result

    static member CreateDocument(rootFolder: string, definition: WorkspaceDocument, token: CancellationToken,
                                 [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                 [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        let requireNew () =
            if not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
                raise (IOException("A workspace file already exists at this location. Reopen the workspace to load it."))
        requireNew ()
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        WorkspaceFileStore.ValidateDocuments(None, plan.Definition)
        requireNew ()
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedDocument (normalizeRoot rootFolder) result.Definition
        result

    static member SaveDocument(rootFolder: string, definition: WorkspaceDocument, savedDefinition: WorkspaceDocument, token: CancellationToken,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        token.ThrowIfCancellationRequested()
        let verified = WorkspaceDisk.verifySavedFiles savedDefinition
        // File DTOs are editable; don't rely on a caller leaving the baseline's fields untouched.
        let previous = WorkspaceDisk.documentBaseline savedDefinition
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        if verified then WorkspaceDisk.verifySavedFiles savedDefinition |> ignore
        elif not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
            let current = WorkspaceDisk.loadDocument rootFolder
            if not (equalBytes (MessagePackSetup.serialize current) (MessagePackSetup.serialize savedDefinition)) then
                raise (IOException("The saved workspace changed since it was loaded. Reopen the workspace before saving again."))
        WorkspaceFileStore.ValidateDocuments(Some previous, plan.Definition)
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedDocument (normalizeRoot rootFolder) result.Definition
        result

    static member private ValidateDocuments(previous: WorkspaceDocument option, next: WorkspaceDocument) =
        let rec projects (modules: ModuleDocument array) = seq {
            for m in modules do
                yield! m.Projects
                yield! projects m.Modules }
        let oldProjects = previous |> Option.map (fun value -> projects value.Modules |> Seq.map (fun p -> p.Definition.id, p) |> dict)
        let oldRuns = previous |> Option.map (fun value -> projects value.Modules |> Seq.collect _.Runs |> Seq.map (fun r -> r.id, r.parentProjectId) |> dict)
        for p in projects next.Modules do
            let dto = p.Definition
            let old = oldProjects |> Option.bind (fun values -> match values.TryGetValue dto.id with true, p -> Some p | _ -> None)
            match old with
            | Some original when original.Definition.isSealed ->
                let before = original.Definition
                if not dto.isSealed || dto.projectName <> before.projectName || dto.folderName <> before.folderName
                   || not (equalBytes (MessagePackSetup.serialize dto.configuration) (MessagePackSetup.serialize before.configuration)) then
                    invalidOp $"Project '{before.projectName}' is sealed. Create a new project version to change its settings, name, or folder."
            | _ when dto.isSealed -> WorkspaceDocument.projectToDomain None p |> ignore
            | _ -> ()
            for run in p.Runs do
                match oldRuns |> Option.bind (fun values -> match values.TryGetValue run.id with true, parent -> Some parent | _ -> None) with
                | Some parent when parent <> run.parentProjectId -> invalidOp "An existing run cannot change its parent project. Create a new run instead."
                | _ -> ()

    static member private ValidateSealedProjects(previous: workspace, next: workspace) =
        let oldProjects = previous.ProjectIndex
        let oldRuns = oldProjects.Values |> Seq.collect _.Runs |> Seq.map (fun run -> run.Id, run.ParentProjectId) |> dict
        for project in next.ProjectIndex.Values do
            match oldProjects.TryGetValue(project.Id) with
            | true, original when original.IsSealed
                                  && (not project.IsSealed || project.ProjectName <> original.ProjectName || project.FolderName <> original.FolderName || project.Configuration <> original.Configuration) ->
                raise (InvalidOperationException($"Project '{original.ProjectName}' is sealed. Create a new project version to change its settings, name, or folder."))
            | _ -> ()
            for run in project.Runs do
                match oldRuns.TryGetValue(run.Id) with
                | true, parent when parent <> run.ParentProjectId ->
                    raise (InvalidOperationException("An existing run cannot change its parent project. Create a new run instead."))
                | _ -> ()
