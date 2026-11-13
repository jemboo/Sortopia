namespace Sortopia.Workspaces.Persistence

open System.Collections.Generic
open System.Runtime.InteropServices
open Sortopia.Workspaces

[<Sealed>]
type WorkspaceSaveResult(definition: workspace, copiedFolders: int, retainedFolders: IReadOnlyList<string>,
                         [<Optional; DefaultParameterValue(0)>] movedFolders: int) =
    member _.Definition = definition
    member _.CopiedFolders = copiedFolders
    member _.RetainedFolders = retainedFolders
    member _.MovedFolders = movedFolders

type internal DiskState = { Fingerprint: string; Entities: string array }
type internal WorkspaceFileUpdate = { Path: string; Content: byte array; OriginalPath: string; Original: byte array }
type internal SavePlan =
    { RootFolder: string
      Definition: workspace
      Fingerprint: string
      Writes: WorkspaceFileUpdate array
      RetainedFolders: string array
      Copies: WorkspaceFolderCopy array
      Moves: WorkspaceFolderMove array
      EmptyContainers: string array }

