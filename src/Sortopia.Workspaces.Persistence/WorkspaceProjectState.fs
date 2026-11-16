namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open WorkspaceFileSystem

module internal WorkspaceProjectState =
    let rec projects folder (modules: ModuleDocument array) = seq {
        for m in modules do
            let folder = Path.Combine(folder, m.Definition.moduleName)
            for p in m.Projects do yield p, Path.Combine(folder, p.Definition.projectName)
            yield! projects folder m.Modules }

    let find id (value: WorkspaceDocument) =
        projects "" value.Modules |> Seq.tryFind (fun (p, _) -> p.Definition.id = id)
        |> Option.defaultWith (fun () -> invalidOp "The saved project was not found. Save or reopen the workspace first.")

    let replace id update (value: WorkspaceDocument) =
        let rec visit (m: ModuleDocument) =
            { m with Modules = Array.map visit m.Modules
                     Projects = m.Projects |> Array.map (fun p -> if p.Definition.id = id then update p else p) }
        { value with Modules = Array.map visit value.Modules }

    let seal (parent: project) value =
        replace (UMX.untag parent.Id) (fun p ->
            let definition = WorkspaceDefinitionCodec.projectWithOrigin Unchecked.defaultof<_> (project.seal parent)
            // A selected-run scope deliberately omits sibling runs, including drafts.
            { p with Definition = { definition with runs = p.Definition.runs; localOverrides = p.Definition.localOverrides } }) value

    let hasRunParameters root folder =
        let rec hasFiles path =
            WorkspaceFolderMove.CheckPath(root, path)
            File.Exists(path) || (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path) |> Seq.exists hasFiles)
        hasFiles (Path.Combine(root, folder, "runParameters"))

    let validateLockedModules root (previous: WorkspaceDocument) (next: WorkspaceDocument) =
        let rec modules (values: ModuleDocument array) = seq {
            for m in values do
                yield m.Definition.id, m.Definition
                yield! modules m.Modules }
        let current = modules next.Modules |> Map.ofSeq
        let rec visit folder (m: ModuleDocument) =
            let folder = Path.Combine(folder, m.Definition.moduleName)
            let children = m.Modules |> Array.map (visit folder)
            let locked = (children |> Array.contains true) || (m.Projects |> Array.exists (fun p -> hasRunParameters root (Path.Combine(folder, p.Definition.projectName))))
            if locked then
                match Map.tryFind m.Definition.id current with
                | Some value when value.moduleName <> m.Definition.moduleName
                                  || MessagePackSetup.serialize value.localOverrides <> MessagePackSetup.serialize m.Definition.localOverrides ->
                    invalidOp $"Module '{m.Definition.moduleName}' has descendant generated run parameter files. Remove them before editing its properties."
                | _ -> ()
            locked
        previous.Modules |> Array.iter (fun m -> visit "" m |> ignore)

    let resumeReason root value id =
        let selected, folder = find id value
        if not selected.Definition.isSealed then "This project is already editable."
        elif hasRunParameters root folder then
            "This project has generated run parameter files. Remove them from all its runs to edit its settings."
        else ""

    let resume value id =
        let rec owner (modules: ModuleDocument array) =
            modules |> Array.tryPick (fun m ->
                if m.Projects |> Array.exists (fun p -> p.Definition.id = id) then Some m.Definition
                else owner m.Modules)
        let parent = owner value.Modules |> Option.get
        let inherited =
            try
                (WorkspaceDefinitionCodec.moduleHeader None parent).Configuration
                |> ModuleDefaults.toProject |> SettingsDto.fromDomain
            with error -> invalidOp $"Module '{parent.moduleName}' has invalid inherited settings. Correct those settings before resuming project editing. {error.Message}"
        let orEmpty values = if isNull values then [||] else values
        let exclusions (inherited: parameterBindingDto array) (local: parameterBindingDto array) (saved: parameterBindingDto array) spans =
            Seq.append (Seq.append (orEmpty inherited) (orEmpty local) |> Seq.map _.keyName) spans
            |> Seq.except (saved |> Seq.map _.keyName) |> Seq.distinct |> Seq.toArray
        replace id (fun p ->
            let header = WorkspaceDefinitionCodec.projectHeader None p.Definition
            let local = if isNull (box p.Definition.localOverrides) then OverridesDto.fromDomain header.LocalOverrides else p.Definition.localOverrides
            let settings = p.Definition.configuration
            // Reopening must not reintroduce inherited defaults that this project's
            // snapshot excluded, especially keys already supplied by run spans.
            let local =
                if not (isNull (box p.Definition.localOverrides)) then local
                else
                    { local with
                        excludedResultsDefaults = exclusions parent.inheritedDefaults.resultsParameterDefaults parent.localOverrides.resultsParameterDefaults settings.resultsParameterDefaults
                            (p.Runs |> Seq.collect _.resultsParameterSpans |> Seq.map _.keyName)
                        excludedSetupDefaults = exclusions parent.inheritedDefaults.setupParameterDefaults parent.localOverrides.setupParameterDefaults settings.setupParameterDefaults
                            (p.Runs |> Seq.collect _.setupParameterSpans |> Seq.map _.keyName) }
            let definition =
                { p.Definition with
                    isSealed = false
                    configuration = Unchecked.defaultof<_>
                    localOverrides = local
                    inheritedDefaults = inherited }
            { p with Definition = definition }) value
        |> WorkspaceDocument.withOrigins
