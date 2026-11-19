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
open Sortopia.FileDb
open WorkspaceFileSystem

/// Transactional workspace saves, explicit removals and imports. Unlisted folders are kept.
[<AbstractClass; Sealed>]
type WorkspaceFileStore private () =
    static member LoadDefinition(rootFolder: string) = WorkspaceDisk.loadDefinition rootFolder
    static member LoadDocument(rootFolder: string) = WorkspaceDisk.loadDocument rootFolder
    static member private RuntimeResult(result: WorkspaceDocumentSaveResult) =
        WorkspaceSaveResult(WorkspaceDocument.toDomain result.Definition, result.CopiedFolders, result.RetainedFolders, result.MovedFolders, result.CleanupWarnings)
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
        WorkspaceProjectState.validateLockedModules (normalizeRoot rootFolder) (WorkspaceDocument.fromDomain savedDefinition) plan.Definition
        let plan = WorkspaceSavePlan.withRemovals (WorkspaceDocument.fromDomain savedDefinition) plan token
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
        WorkspaceFileStore.ValidateDocuments(None, plan.Definition, Set.empty)
        requireNew ()
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedDocument (normalizeRoot rootFolder) result.Definition
        result

    static member SaveDocument(rootFolder: string, definition: WorkspaceDocument, savedDefinition: WorkspaceDocument, token: CancellationToken,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderCopy>)>] copies: IReadOnlyList<WorkspaceFolderCopy>,
                               [<Optional; DefaultParameterValue(null: IReadOnlyList<WorkspaceFolderMove>)>] moves: IReadOnlyList<WorkspaceFolderMove>) =
        WorkspaceFileStore.SaveDocumentCore(rootFolder, definition, savedDefinition, token, copies, moves, Set.empty, [||])

    static member private SaveDocumentCore(rootFolder, definition, savedDefinition, token: CancellationToken, copies, moves, resumeProject, removedFiles: string array) =
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
        WorkspaceFileStore.ValidateDocuments(Some previous, plan.Definition, resumeProject)
        WorkspaceProjectState.validateLockedModules (normalizeRoot rootFolder) previous plan.Definition
        let plan = WorkspaceSavePlan.withRemovals previous plan token
        let removals = removedFiles |> Array.map (fun path ->
            WorkspaceFolderMove.CheckPath(plan.RootFolder, path)
            { Source = path; Destination = path })
        let plan = { plan with Removals = Array.append plan.Removals removals }
        let result = WorkspaceCommit.commit plan token
        WorkspaceDisk.rememberSavedDocument (normalizeRoot rootFolder) result.Definition
        result

    /// Empty when the project can safely return to editable configuration.
    static member ResumeProjectEditingReason(rootFolder: string, savedDefinition: WorkspaceDocument, projectId: Guid) =
        WorkspaceProjectState.resumeReason (normalizeRoot rootFolder) (WorkspaceDisk.documentBaseline savedDefinition) projectId

    /// Explicit recovery for an unused project sealed by an earlier save or failed generation.
    static member ResumeProjectEditing(rootFolder: string, savedDefinition: WorkspaceDocument, projectId: Guid, token: CancellationToken) =
        let previous = WorkspaceDisk.documentBaseline savedDefinition
        let reason = WorkspaceProjectState.resumeReason (normalizeRoot rootFolder) previous projectId
        if reason <> "" then invalidOp reason
        let next = WorkspaceProjectState.resume previous projectId
        let _, folder = WorkspaceProjectState.find projectId previous
        let data = Path.Combine(normalizeRoot rootFolder, folder, "data")
        let removed = if Directory.Exists data then [| data |] else [||]
        WorkspaceFileStore.SaveDocumentCore(rootFolder, next, savedDefinition, token, null, null, Set.singleton projectId, removed)

    /// Files, including unreadable ones, keep a project's ancestors locked.
    static member ProjectsWithRunParameters(rootFolder: string, savedDefinition: WorkspaceDocument) =
        let root = normalizeRoot rootFolder
        WorkspaceProjectState.projects "" (WorkspaceDisk.documentBaseline savedDefinition).Modules
        |> Seq.choose (fun (p, folder) -> if WorkspaceProjectState.hasRunParameters root folder then Some p.Definition.id else None)
        |> Seq.toArray

    /// Release snapshots after the last parameter file was removed, clearing the invalid data cache.
    static member ReleaseUnusedProjects(rootFolder: string, savedDefinition: WorkspaceDocument, token: CancellationToken,
                                       [<Optional; DefaultParameterValue(null: Guid array)>] previouslyGenerated: Guid array) =
        let root = normalizeRoot rootFolder
        let observed = if isNull previouslyGenerated then Set.empty else Set.ofArray previouslyGenerated
        let ids = WorkspaceProjectState.projects "" (WorkspaceDisk.documentBaseline savedDefinition).Modules
                  |> Seq.choose (fun (p, folder) ->
                      // Old explicitly sealed snapshots may have no module definitions to resume from.
                      // Keep their explicit Resume editing action until generated files are removed.
                      let generated = not (isNull (box p.Definition.localOverrides)) || Set.contains p.Definition.id observed
                      if p.Definition.isSealed && generated && not (WorkspaceProjectState.hasRunParameters root folder) then Some p.Definition.id else None)
                  |> Seq.toArray
        (savedDefinition, ids) ||> Array.fold (fun current id -> WorkspaceFileStore.ResumeProjectEditing(root, current, id, token).Definition)

    /// Remove a run's parameter files and data not referenced by remaining parameter files.
    static member RemoveRunParameterFiles(rootFolder: string, savedDefinition: WorkspaceDocument, projectId: Guid, runId: Guid, token: CancellationToken) =
        let root = normalizeRoot rootFolder
        WorkspaceDisk.verifySavedFiles savedDefinition |> ignore
        let previous = WorkspaceDisk.documentBaseline savedDefinition
        let selected, relative = WorkspaceProjectState.find projectId previous
        if not (selected.Runs |> Array.exists (fun r -> r.id = runId)) then invalidOp "Save the run before removing its parameter files."
        let folder = Path.Combine(root, relative, "runParameters")
        WorkspaceFolderMove.CheckPath(root, folder)
        let paths = WorkspaceRemoval.runFiles (Path.Combine(root, relative)) selected (Set.singleton (UMX.tag runId)) token
        let deleted = HashSet<string>(paths, StringComparer.OrdinalIgnoreCase)
        let remaining = Directory.Exists folder && Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) |> Seq.exists (fun path -> not (deleted.Contains path))
        let resume = selected.Definition.isSealed && not remaining
        let next = if resume then WorkspaceProjectState.resume previous projectId else previous
        let data = Path.Combine(root, relative, "data")
        let paths = if resume && Directory.Exists data then Array.append (paths |> Array.filter (fun path -> not (WorkspaceFolderCopy.Inside(data, path)))) [| data |] else paths
        WorkspaceFileStore.SaveDocumentCore(root, next, savedDefinition, token, null, null, (if resume then Set.singleton projectId else Set.empty), paths).Definition

    /// Reset a saved workspace, module, project or run. Definitions stay in place; unused projects become editable.
    static member ResetGeneratedFiles(rootFolder: string, savedDefinition: WorkspaceDocument, kind: string, nodeId: Guid, token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        WorkspaceDisk.verifySavedFiles savedDefinition |> ignore
        let root = normalizeRoot rootFolder
        let previous = WorkspaceDisk.documentBaseline savedDefinition
        let next, resumed, paths = WorkspaceReset.plan root previous kind nodeId token
        WorkspaceFileStore.SaveDocumentCore(root, next, savedDefinition, token, null, null, resumed, paths)

    static member private ValidateDocuments(previous: WorkspaceDocument option, next: WorkspaceDocument, resumeProjects: Set<Guid>) =
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
            | Some original when original.Definition.isSealed && not (Set.contains dto.id resumeProjects) ->
                let before = original.Definition
                if not dto.isSealed
                   || not (equalBytes (MessagePackSetup.serialize dto.configuration) (MessagePackSetup.serialize before.configuration)) then
                    invalidOp $"Project '{before.projectName}' is sealed. Create a new project version to change its execution settings."
            | _ when dto.isSealed -> WorkspaceDefinitionCodec.projectHeader None dto |> ignore
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
                                  && (not project.IsSealed || project.Configuration <> original.Configuration) ->
                raise (InvalidOperationException($"Project '{original.ProjectName}' is sealed. Create a new project version to change its execution settings."))
            | _ -> ()
            for run in project.Runs do
                match oldRuns.TryGetValue(run.Id) with
                | true, parent when parent <> run.ParentProjectId ->
                    raise (InvalidOperationException("An existing run cannot change its parent project. Create a new run instead."))
                | _ -> ()
