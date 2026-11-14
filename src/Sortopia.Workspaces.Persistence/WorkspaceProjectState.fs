namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open WorkspaceFileSystem

module internal WorkspaceProjectState =
    let rec projects folder (modules: ModuleDocument array) = seq {
        for m in modules do
            let folder = Path.Combine(folder, m.Definition.folderName)
            for p in m.Projects do yield p, Path.Combine(folder, p.Definition.folderName)
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
            { p with Definition = { definition with runs = p.Definition.runs } }) value

    let resumeReason root value id =
        let selected, folder = find id value
        if not selected.Definition.isSealed then "This project is already editable."
        else
            let dependent = projects "" value.Modules |> Seq.tryPick (fun (p, _) ->
                if p.Definition.id <> id && p.Definition.isSealed
                   && (p.Definition.configuration.setupDataTypes |> Array.exists (fun entry -> entry.sourceProjectId = id))
                then Some p.Definition.projectName else None)
            match dependent with
            | Some name -> $"Project '{name}' has fixed settings that use this source. Create a new project version instead."
            | None ->
                let rec hasFiles path =
                    WorkspaceFolderMove.CheckPath(root, path)
                    File.Exists(path) || (Directory.Exists(path) &&
                        Directory.EnumerateFileSystemEntries(path) |> Seq.exists (fun entry ->
                            WorkspaceFolderMove.CheckPath(root, entry)
                            File.Exists(entry) || hasFiles entry))
                if [ "runParameters"; "data" ] |> List.exists (fun name -> hasFiles (Path.Combine(root, folder, name))) then
                    "This project has generated parameter or data files. Create a new project version to change its settings."
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
            let local = OverridesDto.fromDomain header.LocalOverrides
            let settings = p.Definition.configuration
            // Reopening must not reintroduce inherited defaults that this project's
            // snapshot excluded, especially keys already supplied by run spans.
            let local = { local with
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
