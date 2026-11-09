namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

[<CLIMutable>]
type parameterSpanDto = { keyName: string; values: string array }
[<CLIMutable>]
type sortopiaDataTypeDto = { kind: string; value: string }
[<CLIMutable>]
type projectSettingsDto = {
    description: string
    resultsDatabaseName: string
    executeFunctionName: string
    outputDataTypes: sortopiaDataTypeDto array
    outputQueryParameterKeys: string array
    outputParameterDefaults: parameterBindingDto array
    setupDatabaseName: string
    setupFunctionName: string
    setupOutputDataTypes: sortopiaDataTypeDto array
    setupQueryParameterKeys: string array
    setupParameterDefaults: parameterBindingDto array
}
[<CLIMutable>]
type runDto = {
    schemaVersion: int
    runKind: string
    projectName: string
    runName: string
    id: Guid
    description: string
    outputParameterSpans: parameterSpanDto array
    setupDatabaseName: string
    setupFunctionName: string
    setupParameterSpans: parameterSpanDto array
}
[<CLIMutable>]
type projectDto = { schemaVersion: int; id: Guid; projectKind: string; projectName: string; folderName: string; configuration: projectSettingsDto; localOverrides: projectSettingsDto; runs: runDto array }
[<CLIMutable>]
type moduleDto = { schemaVersion: int; id: Guid; moduleKind: string; moduleName: string; folderName: string; configuration: projectSettingsDto; localOverrides: projectSettingsDto; modules: moduleDto array; projects: projectDto array }
[<CLIMutable>]
type workspaceDto = { schemaVersion: int; id: Guid; workspaceName: string; folderName: string; modules: moduleDto array }

module SortopiaDataTypeDto =
    let fromDomain (value: sortopiaDataType) : sortopiaDataTypeDto =
        SortopiaDataType.validate value
        { kind = SortopiaDataType.kindName value; value = SortopiaDataType.text value }
    let toDomain (value: sortopiaDataTypeDto) =
        if isNull (box value) then nullArg "dataType"
        SortopiaDataType.create value.kind value.value

module private Contract =
    let version value =
        if value <> 8 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}. Create a new workspace in the current format."
    let kind value =
        match value with
        | "root" -> hierarchyKind.Root
        | "branch" -> hierarchyKind.Branch
        | _ -> invalidArg "kind" "Kind must be root or branch."
    let kindName value = if value = hierarchyKind.Root then "root" else "branch"
    let array name (value: 'a array) =
        if isNull value then invalidArg name "Required collection is missing."
        value
    let spanTo (value: parameterSpanDto) =
        ParameterSpan.create (ParamKey.text value.keyName) (array "values" value.values |> Array.toList)
    let spanFrom (value: parameterSpan) = { keyName = UMX.untag value.KeyName; values = List.toArray value.Values }
    let depth value =
        if value > 64 then invalidArg "hierarchy" "Workspace nesting must not exceed 64 levels."

module RunDto =
    let fromDomain (value: run) : runDto =
        let description, setupDatabase, setupFunction, setupSpans =
            match value with
            | run.RootRun r -> r.Configuration.Description, None, None, []
            | run.BranchRun r -> r.Configuration.Description, r.Configuration.SetupDatabaseName, r.Configuration.SetupFunctionName, r.SetupParameterSpans
        { schemaVersion = 8; runKind = if value.HasSetup then "branch" else "root"
          projectName = UMX.untag value.ProjectName; runName = UMX.untag value.RunName; id = UMX.untag value.Id
          description = description |> Option.toObj
          outputParameterSpans = value.ParameterSpans |> List.map Contract.spanFrom |> List.toArray
          setupDatabaseName = setupDatabase |> Option.map UMX.untag |> Option.toObj
          setupFunctionName = setupFunction |> Option.map UMX.untag |> Option.toObj
          setupParameterSpans = setupSpans |> List.map Contract.spanFrom |> List.toArray }
    let readId (value: runDto) =
        Contract.version value.schemaVersion
        Contract.kind value.runKind |> ignore
        EntityId.validate value.id
        EntityName.validate value.runName |> ignore
        UMX.tag<runId> value.id
    /// A run file contains overrides and must be resolved against its containing project.
    let toDomain (parent: project) (value: runDto) : run =
        let id = readId value
        let outputSpans = Contract.array "outputParameterSpans" value.outputParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let setupSpans = Contract.array "setupParameterSpans" value.setupParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let description = Option.ofObj value.description
        match parent, value.runKind with
        | project.RootProject p, "root" ->
            if not (isNull value.setupDatabaseName) || not (isNull value.setupFunctionName) || not setupSpans.IsEmpty then
                invalidArg "runKind" "A root run cannot have setup settings."
            RootRun.create p { Id = id; RunName = UMX.tag value.runName; Description = description; OutputParameterSpans = outputSpans } |> run.RootRun
        | project.BranchProject p, "branch" ->
            BranchRun.create p { Id = id; RunName = UMX.tag value.runName; Description = description; OutputParameterSpans = outputSpans
                                 SetupDatabaseName = Option.ofObj value.setupDatabaseName |> Option.map UMX.tag<setupDatabaseName>
                                 SetupFunctionName = Option.ofObj value.setupFunctionName |> Option.map UMX.tag<setupFunctionName>
                                 SetupParameterSpans = setupSpans } |> run.BranchRun
        | _ -> invalidArg "runKind" "A project can only contain runs of its own type."

module SettingsDto =
    let private fromRoot (value: rootProjectConfig) : projectSettingsDto = {
        description = value.Description
        resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        executeFunctionName = UMX.untag value.ExecuteFunctionName
        outputDataTypes = value.OutputDataTypes |> List.map SortopiaDataTypeDto.fromDomain |> List.toArray
        outputQueryParameterKeys = value.OutputQueryParameterKeys |> List.map UMX.untag |> List.toArray
        outputParameterDefaults = value.OutputParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupDatabaseName = ""
        setupFunctionName = ""
        setupOutputDataTypes = [||]
        setupQueryParameterKeys = [||]
        setupParameterDefaults = [||] }
    let private fromBranch (value: branchProjectConfig) : projectSettingsDto = {
        description = value.Description
        resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        executeFunctionName = UMX.untag value.ExecuteFunctionName
        outputDataTypes = value.OutputDataTypes |> List.map SortopiaDataTypeDto.fromDomain |> List.toArray
        outputQueryParameterKeys = value.OutputQueryParameterKeys |> List.map UMX.untag |> List.toArray
        outputParameterDefaults = value.OutputParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupDatabaseName = UMX.untag value.SetupDatabaseName
        setupFunctionName = UMX.untag value.SetupFunctionName
        setupOutputDataTypes = value.SetupOutputDataTypes |> List.map SortopiaDataTypeDto.fromDomain |> List.toArray
        setupQueryParameterKeys = value.SetupQueryParameterKeys |> List.map UMX.untag |> List.toArray
        setupParameterDefaults = value.SetupParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray }
    let fromDomain = function RootProjectConfig v -> fromRoot v | BranchProjectConfig v -> fromBranch v
    let toDomain kind (value: projectSettingsDto) =
        if isNull (box value) then nullArg "configuration"
        let outputs = Contract.array "outputDataTypes" value.outputDataTypes |> Array.map SortopiaDataTypeDto.toDomain |> Array.toList
        let keys = Contract.array "outputQueryParameterKeys" value.outputQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let setupOutputs = Contract.array "setupOutputDataTypes" value.setupOutputDataTypes |> Array.map SortopiaDataTypeDto.toDomain |> Array.toList
        let setupKeys = Contract.array "setupQueryParameterKeys" value.setupQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let defaults = Contract.array "outputParameterDefaults" value.outputParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        let setupDefaults = Contract.array "setupParameterDefaults" value.setupParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        match kind with
        | hierarchyKind.Root ->
            if value.setupDatabaseName <> "" || value.setupFunctionName <> "" || not setupOutputs.IsEmpty || not setupKeys.IsEmpty || not setupDefaults.IsEmpty then
                invalidArg "configuration" "A root project cannot have setup settings."
            RootProjectConfig { Description = value.description; ResultsDatabaseName = UMX.tag value.resultsDatabaseName
                                ExecuteFunctionName = UMX.tag value.executeFunctionName; OutputDataTypes = outputs; OutputQueryParameterKeys = keys; OutputParameterDefaults = defaults }
        | hierarchyKind.Branch ->
            BranchProjectConfig { Description = value.description; ResultsDatabaseName = UMX.tag value.resultsDatabaseName
                                  ExecuteFunctionName = UMX.tag value.executeFunctionName; OutputDataTypes = outputs; OutputQueryParameterKeys = keys; OutputParameterDefaults = defaults
                                  SetupDatabaseName = UMX.tag value.setupDatabaseName; SetupFunctionName = UMX.tag value.setupFunctionName
                                  SetupOutputDataTypes = setupOutputs; SetupQueryParameterKeys = setupKeys; SetupParameterDefaults = setupDefaults }
        | _ -> invalidArg "kind" "Project kind must be root or branch."

module OverridesDto =
    let private fromRoot (value: rootDefaultOverrides) : projectSettingsDto = {
        description = value.Description |> Option.toObj
        resultsDatabaseName = value.ResultsDatabaseName |> Option.map UMX.untag |> Option.toObj
        executeFunctionName = value.ExecuteFunctionName |> Option.map UMX.untag |> Option.toObj
        outputDataTypes = value.OutputDataTypes |> Option.map (List.map SortopiaDataTypeDto.fromDomain >> List.toArray) |> Option.toObj
        outputQueryParameterKeys = value.OutputQueryParameterKeys |> Option.map (List.map UMX.untag >> List.toArray) |> Option.toObj
        outputParameterDefaults = value.OutputParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        setupDatabaseName = null
        setupFunctionName = null
        setupOutputDataTypes = null
        setupQueryParameterKeys = null
        setupParameterDefaults = null }
    let private fromBranch (value: branchDefaultOverrides) : projectSettingsDto = {
        description = value.Description |> Option.toObj
        resultsDatabaseName = value.ResultsDatabaseName |> Option.map UMX.untag |> Option.toObj
        executeFunctionName = value.ExecuteFunctionName |> Option.map UMX.untag |> Option.toObj
        outputDataTypes = value.OutputDataTypes |> Option.map (List.map SortopiaDataTypeDto.fromDomain >> List.toArray) |> Option.toObj
        outputQueryParameterKeys = value.OutputQueryParameterKeys |> Option.map (List.map UMX.untag >> List.toArray) |> Option.toObj
        outputParameterDefaults = value.OutputParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        setupDatabaseName = value.SetupDatabaseName |> Option.map UMX.untag |> Option.toObj
        setupFunctionName = value.SetupFunctionName |> Option.map UMX.untag |> Option.toObj
        setupOutputDataTypes = value.SetupOutputDataTypes |> Option.map (List.map SortopiaDataTypeDto.fromDomain >> List.toArray) |> Option.toObj
        setupQueryParameterKeys = value.SetupQueryParameterKeys |> Option.map (List.map UMX.untag >> List.toArray) |> Option.toObj
        setupParameterDefaults = value.SetupParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj }
    let fromDomain = function RootOverrides v -> fromRoot v | BranchOverrides v -> fromBranch v
    let toDomain kind (value: projectSettingsDto) =
        if isNull (box value) then nullArg "localOverrides"
        match kind with
        | hierarchyKind.Root ->
            if not (isNull value.setupDatabaseName) || not (isNull value.setupFunctionName) || not (isNull value.setupOutputDataTypes) || not (isNull value.setupQueryParameterKeys) || not (isNull value.setupParameterDefaults) then
                invalidArg "localOverrides" "Root defaults cannot have setup settings."
            RootOverrides {
                Description = Option.ofObj value.description
                ResultsDatabaseName = Option.ofObj value.resultsDatabaseName |> Option.map UMX.tag<resultsDatabaseName>
                ExecuteFunctionName = Option.ofObj value.executeFunctionName |> Option.map UMX.tag<executeFunctionName>
                OutputDataTypes = Option.ofObj value.outputDataTypes |> Option.map (Array.map SortopiaDataTypeDto.toDomain >> Array.toList)
                OutputQueryParameterKeys = Option.ofObj value.outputQueryParameterKeys |> Option.map (Array.map UMX.tag<paramKeyName> >> Array.toList)
                OutputParameterDefaults = Option.ofObj value.outputParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList) }
        | hierarchyKind.Branch ->
            BranchOverrides {
                Description = Option.ofObj value.description
                ResultsDatabaseName = Option.ofObj value.resultsDatabaseName |> Option.map UMX.tag<resultsDatabaseName>
                ExecuteFunctionName = Option.ofObj value.executeFunctionName |> Option.map UMX.tag<executeFunctionName>
                OutputDataTypes = Option.ofObj value.outputDataTypes |> Option.map (Array.map SortopiaDataTypeDto.toDomain >> Array.toList)
                OutputQueryParameterKeys = Option.ofObj value.outputQueryParameterKeys |> Option.map (Array.map UMX.tag<paramKeyName> >> Array.toList)
                OutputParameterDefaults = Option.ofObj value.outputParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList)
                SetupDatabaseName = Option.ofObj value.setupDatabaseName |> Option.map UMX.tag<setupDatabaseName>
                SetupFunctionName = Option.ofObj value.setupFunctionName |> Option.map UMX.tag<setupFunctionName>
                SetupOutputDataTypes = Option.ofObj value.setupOutputDataTypes |> Option.map (Array.map SortopiaDataTypeDto.toDomain >> Array.toList)
                SetupQueryParameterKeys = Option.ofObj value.setupQueryParameterKeys |> Option.map (Array.map UMX.tag<paramKeyName> >> Array.toList)
                SetupParameterDefaults = Option.ofObj value.setupParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList) }
        | _ -> invalidArg "kind" "Defaults must be root or branch."

module ProjectDto =
    let settingsFromDomain (value: project) = SettingsDto.fromDomain value.Configuration
    let settingsToDomain kind value = SettingsDto.toDomain kind value
    let fromDomain (value: project) : projectDto = {
        schemaVersion = 8; id = UMX.untag value.Id; projectKind = Contract.kindName value.Kind; projectName = UMX.untag value.ProjectName; folderName = value.FolderName
        configuration = settingsFromDomain value; localOverrides = OverridesDto.fromDomain value.LocalOverrides
        runs = value.Runs |> Array.map RunDto.fromDomain }
    let internal read depth inherited (value: projectDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        let kind = Contract.kind value.projectKind
        // Effective snapshots allow standalone imports; attached children always resolve against their actual parent.
        let configuration = inherited |> Option.defaultWith (fun () -> SettingsDto.toDomain kind value.configuration)
        let local = OverridesDto.toDomain kind value.localOverrides
        let parent = project.createInherited (UMX.tag value.id) (UMX.tag value.projectName) value.folderName configuration local [||]
        let runs = Contract.array "runs" value.runs |> Array.map (RunDto.toDomain parent)
        project.createInherited parent.Id parent.ProjectName parent.FolderName configuration local runs
    let toDomain value = read 0 None value

module ModuleDto =
    let rec fromDomain (value: workspaceModule) : moduleDto = {
        schemaVersion = 8; id = UMX.untag value.Id; moduleKind = Contract.kindName value.Kind; moduleName = UMX.untag value.ModuleName; folderName = value.FolderName
        configuration = SettingsDto.fromDomain value.Configuration; localOverrides = OverridesDto.fromDomain value.LocalOverrides
        modules = value.Modules |> Array.map fromDomain; projects = value.Projects |> Array.map ProjectDto.fromDomain }
    let rec internal read depth inherited (value: moduleDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        let kind = Contract.kind value.moduleKind
        let parent = inherited |> Option.defaultWith (fun () -> SettingsDto.toDomain kind value.configuration)
        let local = OverridesDto.toDomain kind value.localOverrides
        let effective = Defaults.resolve parent local
        workspaceModule.createInherited (UMX.tag value.id) (UMX.tag value.moduleName) value.folderName parent local
            (Contract.array "modules" value.modules |> Array.map (read (depth + 1) (Some effective)))
            (Contract.array "projects" value.projects |> Array.map (ProjectDto.read (depth + 1) (Some effective)))
    let toDomain value = read 0 None value

module WorkspaceDto =
    let fromDomain (value: workspace) : workspaceDto = {
        schemaVersion = 8; id = UMX.untag value.Id; workspaceName = UMX.untag value.WorkspaceName; folderName = value.FolderName
        modules = value.Modules |> Array.map ModuleDto.fromDomain }
    let toDomain (value: workspaceDto) =
        Contract.version value.schemaVersion
        workspace.create (UMX.tag value.id) (UMX.tag value.workspaceName) value.folderName
            (Contract.array "modules" value.modules |> Array.map (fun m -> ModuleDto.read 1 (Some (Defaults.configuration (Contract.kind m.moduleKind = hierarchyKind.Branch))) m))

module WorkspaceCodec =
    let encodeWorkspace value = value |> WorkspaceDto.fromDomain |> MessagePackSetup.serialize
    let decodeWorkspace bytes = bytes |> MessagePackSetup.deserialize<workspaceDto> |> WorkspaceDto.toDomain
    let encodeModule value = value |> ModuleDto.fromDomain |> MessagePackSetup.serialize
    let decodeModule bytes = bytes |> MessagePackSetup.deserialize<moduleDto> |> ModuleDto.toDomain
    let encodeProject value = value |> ProjectDto.fromDomain |> MessagePackSetup.serialize
    let decodeProject bytes = bytes |> MessagePackSetup.deserialize<projectDto> |> ProjectDto.toDomain
    let decodeModuleInherited parent bytes = bytes |> MessagePackSetup.deserialize<moduleDto> |> ModuleDto.read 0 (Some parent)
    let decodeProjectInherited parent bytes = bytes |> MessagePackSetup.deserialize<projectDto> |> ProjectDto.read 0 (Some parent)
    let encodeRun value = value |> RunDto.fromDomain |> MessagePackSetup.serialize
    let decodeRun parent bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.toDomain parent
    let readRunId bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.readId
