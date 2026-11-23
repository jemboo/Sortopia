namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.FileDb
open Sortopia.Workspaces.Serialization

type GeneratedProjectFiles = { ProjectId: Guid; HasFiles: bool; RunIds: Guid array; ReadError: string }
type WorkspaceGeneratedFiles = { Projects: GeneratedProjectFiles array; LockedProjectIds: Guid array; LockedModuleIds: Guid array; LockedRunIds: Guid array }
type WorkspaceProjectStateResult = { Workspace: SavedWorkspace; GeneratedFiles: WorkspaceGeneratedFiles }

module internal WorkspaceGeneratedFiles =
    let hasFiles root projectFolder (token: CancellationToken) =
        let rec visit path =
            token.ThrowIfCancellationRequested()
            WorkspaceFolderMove.CheckPath(root, path)
            File.Exists path || (Directory.Exists path && Directory.EnumerateFileSystemEntries(path) |> Seq.exists visit)
        visit (Path.Combine(projectFolder, "runParameters"))

    let inspect root (index: WorkspaceDocumentIndex) (token: CancellationToken) =
        let lockedModules = Collections.Generic.HashSet<Guid>()
        let projects = index.Projects |> Array.map (fun entry ->
            token.ThrowIfCancellationRequested()
            let project = entry.Document
            let folder = Path.Combine(root, entry.RelativeFolder)
            let hasFiles = hasFiles root folder token
            let runs, error =
                if not hasFiles then [||], ""
                else
                    for m in entry.ModulePath do lockedModules.Add m.id |> ignore
                    try
                        let path = Path.Combine(folder, "runParameters")
                        if File.Exists path then raise (IOException($"Run parameter folder is occupied by a file: {path}"))
                        RunParameterFiles.listWorkItems path token |> Array.map (fun item -> UMX.untag item.RunId) |> Array.distinct, ""
                    with
                    | :? OperationCanceledException -> reraise ()
                    | error -> project.Runs |> Array.map _.id, error.Message
            { ProjectId = project.Definition.id; HasFiles = hasFiles; RunIds = runs; ReadError = error })
        { Projects = projects
          LockedProjectIds = projects |> Array.filter _.HasFiles |> Array.map _.ProjectId
          LockedModuleIds = lockedModules |> Seq.toArray
          LockedRunIds = projects |> Array.collect _.RunIds |> Array.distinct }
