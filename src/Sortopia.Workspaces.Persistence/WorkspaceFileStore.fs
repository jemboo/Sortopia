namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
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
        WorkspaceCommit.commit plan token

    static member SaveWorkspace(rootFolder: string, definition: workspace, savedDefinition: workspace, token: CancellationToken,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        if not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
            let current = WorkspaceDisk.loadDefinition rootFolder
            if not (equalBytes (WorkspaceCodec.encodeWorkspace current) (WorkspaceCodec.encodeWorkspace savedDefinition)) then
                raise (IOException("The saved workspace changed since it was loaded. Reopen the workspace before saving again."))
        WorkspaceFileStore.ValidateSealedProjects(savedDefinition, plan.Definition)
        WorkspaceCommit.commit plan token

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
