namespace Sortopia.Workspaces.Persistence

open System
open System.Collections.Generic
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Workspaces.Serialization

/// Plans generated-file removal without changing the workspace or its definitions on disk.
module internal WorkspaceReset =
    let plan root (document: WorkspaceDocument) kind id (token: CancellationToken) =
        let selected = ResizeArray<ProjectDocument * string>()
        let mutable found = kind = "workspace" && document.Definition.id = id
        let rec visit parent included (modules: ModuleDocument array) =
            for m in modules do
                token.ThrowIfCancellationRequested()
                let folder = Path.Combine(parent, m.Definition.moduleName)
                let matches = kind = "module" && m.Definition.id = id
                found <- found || matches
                for p in m.Projects do
                    let matchesProject = kind = "project" && p.Definition.id = id
                    let matchesRun = kind = "run" && (p.Runs |> Array.exists (fun r -> r.id = id))
                    if included || matches || matchesProject || matchesRun then
                        selected.Add(p, Path.Combine(folder, p.Definition.projectName))
                    found <- found || matchesProject || matchesRun
                visit folder (included || matches) m.Modules
        visit root found document.Modules
        if not found then invalidOp "The selected item is not in the saved workspace. Save or reopen the workspace before resetting."
        let removed = ResizeArray<string>()
        let mutable resumed = Set.empty
        let mutable next = document
        let addDirectory path =
            WorkspaceFolderMove.CheckPath(root, path)
            if Directory.Exists path || File.Exists path then removed.Add path
        for p, folder in selected do
            token.ThrowIfCancellationRequested()
            let parameters = Path.Combine(folder, "runParameters")
            let data = Path.Combine(folder, "data")
            WorkspaceFolderMove.CheckPath(root, parameters)
            let remaining =
                if kind <> "run" then false
                else
                    let paths = WorkspaceRemoval.runFiles folder p (Set.singleton (UMX.tag id)) token
                    let deleted = HashSet<string>(paths, StringComparer.OrdinalIgnoreCase)
                    let hasRemaining = Directory.Exists parameters && (Directory.EnumerateFiles(parameters, "*", SearchOption.AllDirectories) |> Seq.exists (fun path -> not (deleted.Contains path)))
                    if hasRemaining then
                        removed.AddRange paths
                        // Project-wide exports are stale after a constituent run is reset.
                        addDirectory (Path.Combine(data, "results", "reports"))
                    hasRemaining
            if not remaining then
                addDirectory parameters
                addDirectory data
                if p.Definition.isSealed then
                    next <- WorkspaceProjectState.resume next p.Definition.id
                    resumed <- Set.add p.Definition.id resumed
        let paths = HashSet<string>(removed, StringComparer.OrdinalIgnoreCase)
        let roots = paths |> Seq.filter (fun path ->
            token.ThrowIfCancellationRequested()
            let mutable parent = Path.GetDirectoryName path
            let mutable nested = false
            while not (isNull parent) && not nested do
                nested <- paths.Contains parent
                parent <- Path.GetDirectoryName parent
            not nested) |> Seq.toArray
        next, resumed, roots
