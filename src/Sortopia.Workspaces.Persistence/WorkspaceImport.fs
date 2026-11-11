namespace Sortopia.Workspaces.Persistence

open System.Threading
open Sortopia.Workspaces.Serialization

/// A loaded import and its deferred copy. A null copy means attaching in place.
type WorkspaceImport<'T> = { Definition: 'T; Copy: WorkspaceFolderCopy }

/// Prepares imports without writing files, including new IDs for copied subtrees.
[<AbstractClass; Sealed>]
type WorkspaceImport private () =
    static member private Prepare(workspaceRoot: string, source: string, destination: string, token: CancellationToken, load, reidentify) =
        let copy = WorkspaceFolderCopy.Prepare(workspaceRoot, source, destination, token)
        let definition = load source token
        let result =
            if isNull copy then { Definition = definition; Copy = null }
            else
                let identities = WorkspaceImportIdentity()
                let definition = reidentify definition identities
                let copy = copy.WithOriginalIds(identities.OriginalIds)
                copy.ValidateSource(token)
                { Definition = definition; Copy = copy }
        token.ThrowIfCancellationRequested()
        result

    static member PrepareModule(workspaceRoot: string, source: string, destination: string, token: CancellationToken) =
        WorkspaceImport.Prepare(workspaceRoot, source, destination, token, WorkspaceDisk.loadModuleFolder,
            fun value ids -> ModuleDto.fromDomain value |> ids.Reidentify |> ModuleDto.toDomain)

    static member PrepareProject(workspaceRoot: string, source: string, destination: string, token: CancellationToken) =
        WorkspaceImport.Prepare(workspaceRoot, source, destination, token, WorkspaceDisk.loadProjectFolder,
            fun value ids -> ProjectDto.fromDomain value |> ids.Reidentify |> ProjectDto.toDomain)
