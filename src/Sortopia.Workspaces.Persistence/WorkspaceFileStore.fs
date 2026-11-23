namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open System.Threading
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem

/// Transactional workspace saves, explicit removals and imports. Unlisted folders are kept.
[<AbstractClass; Sealed>]
type WorkspaceFileStore private () =
    static member private Commit(plan: SavePlan, token) =
        let result = WorkspaceCommit.commit plan token
        let saved = WorkspaceDisk.savedWorkspace plan.RootFolder result.Definition
        WorkspaceSaveResult(saved, result.CopiedFolders, result.RetainedFolders, result.MovedFolders, result.CleanupWarnings)

    static member LoadWorkspace(rootFolder: string) = WorkspaceDisk.loadWorkspace rootFolder
    static member LoadModuleDocument(folder: string, token: CancellationToken) = WorkspaceDisk.loadModuleDocument folder token
    static member LoadProjectDocument(folder: string, token: CancellationToken) = WorkspaceDisk.loadProjectDocument folder token
    /// Copy the complete saved workspace, preserving IDs and data, into an existing empty folder.
    static member ExportWorkspace(rootFolder: string, destinationFolder: string, token: CancellationToken) =
        WorkspaceExport.copy rootFolder destinationFolder token
    static member GetFolderName(rootFolder: string) = WorkspaceDisk.folderName rootFolder
    static member CreateDocument(rootFolder: string, definition: WorkspaceDocument, token: CancellationToken,
                                 [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                                 [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        let requireNew () =
            if not (isNull (WorkspaceDefinitionFiles.Find rootFolder "workspace")) then
                raise (IOException("A workspace file already exists at this location. Reopen the workspace to load it."))
        requireNew ()
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        WorkspaceFileStore.ValidateDocuments(None, plan.Definition, Set.empty)
        requireNew ()
        WorkspaceFileStore.Commit(plan, token)

    static member SaveDocument(definition: WorkspaceDocument, saved: SavedWorkspace, token: CancellationToken,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        WorkspaceFileStore.SaveDocumentCore(definition, saved, token, copies, moves, Set.empty, [||])

    static member private SaveDocumentCore(definition, saved: SavedWorkspace, token: CancellationToken, copies, moves, resumeProject, removedFiles: string array) =
        token.ThrowIfCancellationRequested()
        let rootFolder = saved.RootFolder
        WorkspaceDisk.verifySavedFiles saved
        let previous = saved.Baseline
        let plan = WorkspaceSavePlan.prepare rootFolder definition token copies moves
        WorkspaceDisk.verifySavedFiles saved
        WorkspaceFileStore.ValidateDocuments(Some previous, plan.Definition, resumeProject)
        let oldIndex, nextIndex = WorkspaceDocumentIndex previous, WorkspaceDocumentIndex plan.Definition
        let generated = WorkspaceGeneratedFiles.inspect (normalizeRoot rootFolder) oldIndex token
        WorkspaceProjectState.validateLockedModules generated oldIndex nextIndex
        WorkspaceProjectState.validateLockedRuns generated oldIndex nextIndex
        let plan = WorkspaceSavePlan.withRemovals previous plan token
        let removals = removedFiles |> Array.map (fun path ->
            WorkspaceFolderMove.CheckPath(plan.RootFolder, path)
            { Source = path; Destination = path })
        let plan = { plan with Removals = Array.append plan.Removals removals }
        WorkspaceFileStore.Commit(plan, token)

    static member private ApplyLifecycleChange(saved: SavedWorkspace, change, token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        WorkspaceDisk.verifySavedFiles saved
        let plan = WorkspaceLifecycle.plan saved.RootFolder saved.Baseline change token
        WorkspaceFileStore.SaveDocumentCore(plan.Document, saved, token, null, null, plan.ResumedProjectIds, plan.RemovedPaths)

    /// Empty when the project can safely return to editable configuration.
    static member ResumeProjectEditingReason(saved: SavedWorkspace, projectId: Guid) =
        WorkspaceProjectState.resumeReason saved.RootFolder saved.Baseline projectId

    /// Explicit recovery for an unused project sealed by an earlier save or failed generation.
    static member ResumeProjectEditing(saved: SavedWorkspace, projectId: Guid, token: CancellationToken) =
        WorkspaceFileStore.ApplyLifecycleChange(saved, WorkspaceLifecycleChange.ResumeEditing projectId, token)

    /// One read of generated work supplies project, ancestor-module and run locks.
    static member InspectGeneratedFiles(saved: SavedWorkspace, token: CancellationToken) =
        WorkspaceGeneratedFiles.inspect saved.RootFolder (WorkspaceDocumentIndex saved.Baseline) token

    /// Detached imports use the same definition of generated work as saved projects.
    static member HasRunParameterFiles(projectFolder: string, token: CancellationToken) =
        let root = normalizeRoot projectFolder
        WorkspaceGeneratedFiles.hasFiles root root token

    /// Release snapshots after the last parameter file was removed, clearing the invalid data cache.
    static member RefreshProjectStates(saved: SavedWorkspace, token: CancellationToken,
                                       [<Optional; DefaultParameterValue(null: Guid array)>] previouslyGenerated: Guid array) =
        let root = saved.RootFolder
        let observed = if isNull previouslyGenerated then Set.empty else Set.ofArray previouslyGenerated
        let index = WorkspaceDocumentIndex saved.Baseline
        let generated = WorkspaceGeneratedFiles.inspect root index token
        let locked = Set.ofArray generated.LockedProjectIds
        let ids = index.Projects
                  |> Seq.choose (fun entry ->
                      let p = entry.Document.Definition
                      // Explicit runtime snapshots have no retained editor; keep their manual resume action.
                      let resumable = not (isNull (box p.localSettings)) || Set.contains p.id observed
                      if p.isSealed && resumable && not (Set.contains p.id locked) then Some p.id else None)
                  |> Seq.toArray
        let updated = (saved, ids) ||> Array.fold (fun current id -> WorkspaceFileStore.ResumeProjectEditing(current, id, token).Workspace)
        { Workspace = updated; GeneratedFiles = generated }

    /// Remove a run's parameter files and data not referenced by remaining parameter files.
    static member RemoveRunParameterFiles(saved: SavedWorkspace, projectId: Guid, runId: Guid, token: CancellationToken) =
        WorkspaceFileStore.ApplyLifecycleChange(saved, WorkspaceLifecycleChange.RemoveRunFiles(projectId, runId), token).Workspace

    /// Reset a saved workspace, module, project or run. Definitions stay in place; unused projects become editable.
    static member ResetGeneratedFiles(saved: SavedWorkspace, kind: string, nodeId: Guid, token: CancellationToken) =
        WorkspaceFileStore.ApplyLifecycleChange(saved, WorkspaceLifecycleChange.ResetGenerated(kind, nodeId), token)

    static member private ValidateDocuments(previous: WorkspaceDocument option, next: WorkspaceDocument, resumeProjects: Set<Guid>) =
        let oldIndex = previous |> Option.map WorkspaceDocumentIndex
        for entry in (WorkspaceDocumentIndex next).Projects do
            let p = entry.Document
            let dto = p.Definition
            let old = oldIndex |> Option.bind (fun index -> index.TryFindProject dto.id) |> Option.map _.Document
            match old with
            | Some original when original.Definition.isSealed && not (Set.contains dto.id resumeProjects) ->
                let before = original.Definition
                if not dto.isSealed
                   || not (equalBytes (MessagePackSetup.serialize dto.configuration) (MessagePackSetup.serialize before.configuration)) then
                    invalidOp $"Project '{before.projectName}' is sealed. Create a new project version to change its execution settings."
            | _ when dto.isSealed -> WorkspaceDefinitionResolution.projectSnapshot dto |> ignore
            | _ -> ()
            for run in p.Runs do
                match oldIndex |> Option.bind (fun index -> index.TryFindRun run.id) |> Option.map _.Document.parentProjectId with
                | Some parent when parent <> run.parentProjectId -> invalidOp "An existing run cannot change its parent project. Create a new run instead."
                | _ -> ()
