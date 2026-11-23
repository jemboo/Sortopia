namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Workspaces.Serialization
open WorkspaceFileSystem

[<RequireQualifiedAccess>]
type internal WorkspaceLifecycleChange =
    | ResumeEditing of projectId: Guid
    | RemoveRunFiles of projectId: Guid * runId: Guid
    | ResetGenerated of kind: string * id: Guid

type internal WorkspaceLifecyclePlan = {
    Document: WorkspaceDocument
    ResumedProjectIds: Set<Guid>
    RemovedPaths: string array
}

/// Plan lifecycle transitions and their cleanup together; all disk changes belong to the save transaction.
module internal WorkspaceLifecycle =
    let plan root (document: WorkspaceDocument) change (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let index = WorkspaceDocumentIndex document
        let removed = ResizeArray<string>()
        let mutable resumed = Set.empty
        let mutable next = document
        let folder (entry: ProjectDocumentEntry) = Path.Combine(root, entry.RelativeFolder)
        let addExisting path =
            WorkspaceFolderMove.CheckPath(root, path)
            if Directory.Exists path || File.Exists path then removed.Add path
        let resume (entry: ProjectDocumentEntry) =
            token.ThrowIfCancellationRequested()
            if entry.Document.Definition.isSealed then
                let id = entry.Document.Definition.id
                next <- WorkspaceProjectState.resume next id
                resumed <- Set.add id resumed
                // Every resumed snapshot invalidates its entire data cache.
                let data = Path.Combine(folder entry, "data")
                if Directory.Exists data then addExisting data
        let clearGenerated entry =
            addExisting (Path.Combine(folder entry, "runParameters"))
            addExisting (Path.Combine(folder entry, "data"))
            resume entry
        let runFiles entry (runId: Guid) =
            token.ThrowIfCancellationRequested()
            let parameters = Path.Combine(folder entry, "runParameters")
            WorkspaceFolderMove.CheckPath(root, parameters)
            let paths = WorkspaceRemoval.runFiles (folder entry) entry.Document (Set.singleton (UMX.tag runId)) token
            let deleted = HashSet<string>(paths, pathComparer)
            let remaining = Directory.Exists parameters &&
                            (Directory.EnumerateFiles(parameters, "*", SearchOption.AllDirectories)
                             |> Seq.exists (fun path -> token.ThrowIfCancellationRequested(); not (deleted.Contains path)))
            paths, remaining

        match change with
        | WorkspaceLifecycleChange.ResumeEditing id ->
            let reason = WorkspaceProjectState.resumeReason root document id
            if reason <> "" then invalidOp reason
            resume (index.FindProject id)
        | WorkspaceLifecycleChange.RemoveRunFiles(projectId, runId) ->
            let entry = index.FindProject projectId
            if not (entry.Document.Runs |> Array.exists (fun r -> r.id = runId)) then
                invalidOp "Save the run before removing its parameter files."
            let paths, remaining = runFiles entry runId
            removed.AddRange paths
            if not remaining then resume entry
        | WorkspaceLifecycleChange.ResetGenerated(kind, id) ->
            let found = match kind with
                        | "workspace" -> document.Definition.id = id
                        | "module" -> index.TryFindModule(id).IsSome
                        | "project" -> index.TryFindProject(id).IsSome
                        | "run" -> index.TryFindRun(id).IsSome
                        | _ -> false
            if not found then invalidOp "The selected item is not in the saved workspace. Save or reopen the workspace before resetting."
            let selected = index.Projects |> Array.filter (fun p ->
                kind = "workspace" || (kind = "module" && p.ModulePath |> Array.exists (fun m -> m.id = id))
                || (kind = "project" && p.Document.Definition.id = id)
                || (kind = "run" && p.Document.Runs |> Array.exists (fun r -> r.id = id)))
            for entry in selected do
                token.ThrowIfCancellationRequested()
                if kind = "run" then
                    let paths, remaining = runFiles entry id
                    if remaining then
                        removed.AddRange paths
                        // Reset invalidates project reports even when other runs remain.
                        addExisting (Path.Combine(folder entry, "data", "results", "reports"))
                    else clearGenerated entry
                else clearGenerated entry

        // A resumed cache or a reset folder subsumes individual files beneath it.
        let paths = HashSet<string>(removed, pathComparer)
        let roots = paths |> Seq.filter (fun path ->
            token.ThrowIfCancellationRequested()
            WorkspaceFolderMove.CheckPath(root, path)
            let mutable parent = Path.GetDirectoryName path
            let mutable nested = false
            while not (isNull parent) && not nested do
                nested <- paths.Contains parent
                parent <- Path.GetDirectoryName parent
            not nested) |> Seq.toArray
        { Document = next; ResumedProjectIds = resumed; RemovedPaths = roots }
