namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open WorkspaceFileSystem

/// Transactional workspace saves and explicit entity imports. Unlisted folders and user data are kept.
[<AbstractClass; Sealed>]
type WorkspaceFileStore private () =
    static member LoadDefinition(rootFolder: string) = WorkspaceDisk.loadDefinition rootFolder
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
        let plan = WorkspaceSavePlan.prepareWithContainers root definition token null moves (containers.ToArray())
        WorkspaceDisk.verifySavedFiles definition |> ignore
        let result = WorkspaceCommit.commit plan token
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
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        requireNewWorkspace ()
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedFiles root result.Definition
        result

    static member SaveWorkspace(rootFolder: string, definition: workspace, savedDefinition: workspace, token: CancellationToken,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        token.ThrowIfCancellationRequested()
        let verified = WorkspaceDisk.verifySavedFiles savedDefinition
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        // Catch edits arriving between the initial baseline check and capture.
        if verified then WorkspaceDisk.verifySavedFiles savedDefinition |> ignore
        if not verified && not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
            let current = WorkspaceDisk.loadDefinition rootFolder
            if not (equalBytes (WorkspaceCodec.encodeWorkspace current) (WorkspaceCodec.encodeWorkspace savedDefinition)) then
                raise (IOException("The saved workspace changed since it was loaded. Reopen the workspace before saving again."))
        WorkspaceFileStore.ValidateSealedProjects(savedDefinition, plan.Definition)
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedFiles (normalizeRoot rootFolder) result.Definition
        result

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
