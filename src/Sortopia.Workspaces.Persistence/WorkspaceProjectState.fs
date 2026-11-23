namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open FSharp.UMX
open Sortopia.Workspaces
open Sortopia.Workspaces.Serialization
open Sortopia.Core.Serialization
open Sortopia.FileDb
open Sortopia.Runs
open WorkspaceFileSystem

module internal WorkspaceProjectState =
    let find id (value: WorkspaceDocument) =
        let entry = (WorkspaceDocumentIndex value).FindProject id
        entry.Document, entry.RelativeFolder

    let seal (parent: projectSnapshot) value =
        WorkspaceNavigation.updateProject (UMX.untag parent.Id) (fun p ->
            // The selected scope omits sibling draft runs; the document keeps all of them.
            { p with Definition = { p.Definition with state = ProjectLifecycle.seal (SettingsDto.fromDomain parent.Configuration) p.Definition.state } }) value

    let validateLockedModules (generated: WorkspaceGeneratedFiles) (previous: WorkspaceDocumentIndex) (next: WorkspaceDocumentIndex) =
        for id in generated.LockedModuleIds do
            match previous.TryFindModule id, next.TryFindModule id with
            | Some before, Some after when before.Document.Definition.moduleName <> after.Document.Definition.moduleName
                                        || MessagePackSetup.serialize before.Document.Definition.localOverrides <> MessagePackSetup.serialize after.Document.Definition.localOverrides ->
                invalidOp $"Module '{before.Document.Definition.moduleName}' has descendant generated run parameter files. Remove them before editing its properties."
            | _ -> ()

    let validateLockedRuns (generated: WorkspaceGeneratedFiles) (previous: WorkspaceDocumentIndex) (next: WorkspaceDocumentIndex) =
        for entry in previous.Projects do
            let project = entry.Document
            let changed = project.Runs |> Array.choose (fun before ->
                match next.TryFindRun before.id with
                | Some after when before.sgd <> after.Document.sgd -> Some (before, after.Document)
                | _ -> None)
            if changed.Length > 0 then
                let files = generated.Projects |> Array.find (fun p -> p.ProjectId = project.Definition.id)
                if files.ReadError <> "" then invalidOp files.ReadError
                let runs = Set.ofArray files.RunIds
                for before, after in changed do
                    if Set.contains before.id runs then
                        match before.sgd, after.sgd with
                        | Some old, Some value -> SgdRun.validateExtension old value
                        | None, None -> ()
                        | _ -> invalidOp "SGD snapshot controls cannot be replaced after runParameters are created."

    let resumeReason root value id =
        let selected, folder = find id value
        if not selected.Definition.isSealed then "This project is already editable."
        elif WorkspaceGeneratedFiles.hasFiles root (Path.Combine(root, folder)) System.Threading.CancellationToken.None then
            "This project has generated run parameter files. Remove them from all its runs to edit its settings."
        else ""

    let resume value id =
        let parent = (WorkspaceDocumentIndex value).FindProject(id).Parent.Definition
        let inherited =
            try
                WorkspaceDefinitionResolution.moduleConfiguration None parent
                |> ModuleDefaults.toProject |> SettingsDto.fromDomain
            with error -> invalidOp $"Module '{parent.moduleName}' has invalid inherited settings. Correct those settings before resuming project editing. {error.Message}"
        let orEmpty values = if isNull values then [||] else values
        let exclusions (inherited: parameterBindingDto array) (local: parameterBindingDto array) (saved: parameterBindingDto array) spans =
            Seq.append (Seq.append (orEmpty inherited) (orEmpty local) |> Seq.map _.keyName) spans
            |> Seq.except (saved |> Seq.map _.keyName) |> Seq.distinct |> Seq.toArray
        WorkspaceNavigation.updateProject id (fun p ->
            let header = WorkspaceDefinitionResolution.projectSnapshot p.Definition
            let local = if isNull (box p.Definition.localSettings) then ProjectLocalSettingsDto.fromDomain (ProjectResolution.local header.Configuration) else p.Definition.localSettings
            let settings = p.Definition.configuration
            // Reopening must not reintroduce inherited defaults that this project's
            // snapshot excluded, especially keys already supplied by run spans.
            let local =
                if not (isNull (box p.Definition.localSettings)) then local
                else
                    { local with
                        excludedResultsDefaults = exclusions parent.inheritedDefaults.resultsParameterDefaults parent.localOverrides.resultsParameterDefaults settings.resultsParameterDefaults
                            (p.Runs |> Seq.collect _.resultsParameterSpans |> Seq.map _.keyName)
                        excludedSetupDefaults = exclusions parent.inheritedDefaults.setupParameterDefaults parent.localOverrides.setupParameterDefaults settings.setupParameterDefaults
                            (p.Runs |> Seq.collect _.setupParameterSpans |> Seq.map _.keyName) }
            let definition =
                { p.Definition with state = Draft(local, inherited, { moduleId = parent.id; moduleName = parent.moduleName }) }
            { p with Definition = definition }) value
        |> WorkspaceDocument.withOrigins
