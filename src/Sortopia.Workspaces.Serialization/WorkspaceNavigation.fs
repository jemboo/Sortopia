namespace Sortopia.Workspaces.Serialization

open System
open System.IO

type ModuleDocumentEntry = { Document: ModuleDocument; ParentId: Guid option; ModulePath: ModuleDefinition array; RelativeFolder: string }
type ProjectDocumentEntry = { Document: ProjectDocument; Parent: ModuleDocument; ModulePath: ModuleDefinition array; RelativeFolder: string }
type RunDocumentEntry = { Document: runDto; Parent: ProjectDocumentEntry; RelativePath: string }

/// A navigation view for one operation. Never cache it across document edits.
[<Sealed>]
type WorkspaceDocumentIndex(value: WorkspaceDocument) =
    let modules = ResizeArray<ModuleDocumentEntry>()
    let projects = ResizeArray<ProjectDocumentEntry>()
    let runs = ResizeArray<RunDocumentEntry>()
    do
        let rec visit parent folder ancestors (m: ModuleDocument) =
            let folder = Path.Combine(folder, m.Definition.moduleName)
            let path = Array.append ancestors [| m.Definition |]
            modules.Add { Document = m; ParentId = parent; ModulePath = path; RelativeFolder = folder }
            for p in m.Projects do
                let entry = { Document = p; Parent = m; ModulePath = path; RelativeFolder = Path.Combine(folder, p.Definition.projectName) }
                projects.Add entry
                for r in p.Runs do
                    runs.Add { Document = r; Parent = entry; RelativePath = Path.Combine(entry.RelativeFolder, "runs", string r.id + ".msgpack") }
            for child in m.Modules do visit (Some m.Definition.id) folder path child
        for m in value.Modules do visit None "" [||] m
    let moduleIndex = modules |> Seq.map (fun m -> m.Document.Definition.id, m) |> dict
    let projectIndex = projects |> Seq.map (fun p -> p.Document.Definition.id, p) |> dict
    let runIndex = runs |> Seq.map (fun r -> r.Document.id, r) |> dict
    let find key (index: Collections.Generic.IDictionary<_, _>) = match index.TryGetValue key with true, entry -> Some entry | _ -> None
    member _.Modules = modules.ToArray()
    member _.Projects = projects.ToArray()
    member _.Runs = runs.ToArray()
    member _.TryFindModule id = find id moduleIndex
    member _.TryFindProject id = find id projectIndex
    member _.TryFindRun id = find id runIndex
    member this.FindProject id = this.TryFindProject id |> Option.defaultWith (fun () -> invalidOp "The saved project was not found. Save or reopen the workspace first.")

module WorkspaceNavigation =
    /// Replace a project while preserving identity and sibling order.
    let updateProject id (update: ProjectDocument -> ProjectDocument) (value: WorkspaceDocument) =
        let mutable found = false
        let rec visit (m: ModuleDocument) =
            let projects = m.Projects |> Array.map (fun p ->
                if p.Definition.id <> id then p else
                found <- true
                let next = update p
                if next.Definition.id <> id then invalidArg "project" "Replacing a project must preserve its identity."
                next)
            WorkspaceDocument.withModuleChildren m (m.Modules |> Array.map visit) projects
        let result = WorkspaceDocument.withModules value (value.Modules |> Array.map visit)
        if not found then invalidOp "The saved project was not found. Save or reopen the workspace first."
        result
    let replaceProject (project: ProjectDocument) value = updateProject project.Definition.id (fun _ -> project) value
