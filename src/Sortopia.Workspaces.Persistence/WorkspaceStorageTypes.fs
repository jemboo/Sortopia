namespace Sortopia.Workspaces.Persistence

open System.Collections.Generic
open Sortopia.Core.Serialization
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization

type internal DiskState = { Fingerprint: string; Entities: string array }
type internal WorkspaceFileUpdate = { Path: string; Content: byte array; OriginalPath: string; Original: byte array }
type internal SavedDefinitionFile = { Path: string; Kind: string; Hash: string }

/// A saved revision owns its folder, protected baseline and definition-file fingerprints.
/// Editable copies and focused reads never expose the protected baseline.
[<Sealed>]
type SavedWorkspace internal (rootFolder: string, definition: WorkspaceDocument, files: SavedDefinitionFile array) =
    let baseline = WorkspaceDocument.clone definition
    let index = lazy (WorkspaceDocumentIndex baseline)
    member _.RootFolder = rootFolder
    /// Start an independent edit of this revision, including unfinished text.
    member _.CreateEditDocument() = WorkspaceDocument.clone baseline
    /// Resolve only the selected run and its required source contracts.
    member _.ResolveRun(runId) = WorkspaceRunDocument.resolveByRunId baseline runId
    member _.ResolveRun(projectId, runId) = WorkspaceRunDocument.resolve baseline projectId runId
    /// Read a detached lifecycle state without copying runs or unrelated projects.
    member _.TryReadProjectState(projectId) =
        index.Value.TryFindProject projectId
        |> Option.map (fun entry -> MessagePackSetup.serialize entry.Document.Definition.state |> MessagePackSetup.deserialize<projectStateDto>)
    /// Snapshot controls remain readable even when other run inputs are unfinished.
    member _.TryReadRunSnapshotControls(runId) =
        index.Value.TryFindRun runId
        |> Option.bind (fun entry -> entry.Document.sgd)
        |> Option.map (fun value -> { value with SnapshotInterval = value.SnapshotInterval })
    member internal _.Baseline = baseline
    member internal _.Files = files

type internal SavePlan =
    { RootFolder: string
      Definition: WorkspaceDocument
      Fingerprint: string
      Writes: WorkspaceFileUpdate array
      RetainedFolders: string array
      Copies: WorkspaceFolderCopy array
      Moves: WorkspaceFolderMove array
      Removals: RemovedPath array }

type internal WorkspaceCommitResult = {
    Definition: WorkspaceDocument
    CopiedFolders: int
    RetainedFolders: IReadOnlyList<string>
    MovedFolders: int
    CleanupWarnings: IReadOnlyList<string>
}

[<Sealed>]
type WorkspaceSaveResult internal (workspace: SavedWorkspace, copiedFolders: int, retainedFolders: IReadOnlyList<string>, movedFolders: int,
                                   cleanupWarnings: IReadOnlyList<string>) =
    member _.Workspace = workspace
    member _.CopiedFolders = copiedFolders
    member _.RetainedFolders = retainedFolders
    member _.MovedFolders = movedFolders
    member _.CleanupWarnings = if isNull cleanupWarnings then [||] :> IReadOnlyList<string> else cleanupWarnings

