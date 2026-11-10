namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Core.Serialization

[<CLIMutable>]
type parameterSpanDto = { keyName: string; values: string array }
[<CLIMutable>]
type sortopiaDataTypeDto = { kind: string; value: string }
[<CLIMutable>]
type resultsDataTypeConfigDto = { dataType: sortopiaDataTypeDto; queryParameterKeys: string array }
[<CLIMutable>]
type setupDataTypeConfigDto = { dataType: sortopiaDataTypeDto; sourceProjectId: Guid; queryParameterKeys: string array }
[<CLIMutable>]
type projectSettingsDto = {
    description: string
    resultsDatabaseName: string
    resultsFunctionName: string
    resultsDataTypes: resultsDataTypeConfigDto array
    resultsSortopiaDataTypes: sortopiaDataTypeDto array
    resultsQueryParameterKeys: string array
    resultsParameterDefaults: parameterBindingDto array
    setupDatabaseName: string
    setupFunctionName: string
    setupDataTypes: setupDataTypeConfigDto array
    setupParameterDefaults: parameterBindingDto array
}
[<CLIMutable>]
type moduleSettingsDto = {
    description: string
    resultsFunctionName: string
    resultsDataTypes: resultsDataTypeConfigDto array
    resultsSortopiaDataTypes: sortopiaDataTypeDto array
    resultsQueryParameterKeys: string array
    resultsParameterDefaults: parameterBindingDto array
    setupFunctionName: string
    setupDataTypes: setupDataTypeConfigDto array
    setupParameterDefaults: parameterBindingDto array
}
[<CLIMutable>]
type runDto = {
    schemaVersion: int
    runKind: string
    parentProjectId: Guid
    runName: string
    id: Guid
    descriptionOverride: string
    resultsParameterSpans: parameterSpanDto array
    setupParameterSpans: parameterSpanDto array
}
[<CLIMutable>]
type projectDto = { schemaVersion: int; isSealed: bool; id: Guid; projectKind: string; projectName: string; folderName: string; configuration: projectSettingsDto; localOverrides: projectSettingsDto; runs: runDto array }
[<CLIMutable>]
type moduleDto = { schemaVersion: int; id: Guid; moduleKind: string; moduleName: string; folderName: string; configuration: moduleSettingsDto; localOverrides: moduleSettingsDto; modules: moduleDto array; projects: projectDto array }
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
        if value <> 13 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}. Create a new workspace in the current format."
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

module SetupDataTypeConfigDto =
    let fromDomain (value: setupDataTypeConfig) : setupDataTypeConfigDto =
        { dataType = SortopiaDataTypeDto.fromDomain value.DataType; sourceProjectId = UMX.untag value.SourceProjectId
          queryParameterKeys = value.QueryParameterKeys |> List.map UMX.untag |> List.toArray }
    let toDomain (value: setupDataTypeConfigDto) : setupDataTypeConfig =
        if isNull (box value) then nullArg "setupDataType"
        { DataType = SortopiaDataTypeDto.toDomain value.dataType; SourceProjectId = UMX.tag value.sourceProjectId
          QueryParameterKeys = Contract.array "queryParameterKeys" value.queryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList }

module RunDto =
    let fromDomain (value: run) : runDto =
        let description, setupSpans =
            match value with
            | run.RootRun r -> r.Configuration.DescriptionOverride, []
            | run.BranchRun r -> r.Configuration.DescriptionOverride, r.SetupParameterSpans
        { schemaVersion = 13; runKind = if value.HasSetup then "branch" else "root"
          parentProjectId = UMX.untag value.ParentProjectId; runName = UMX.untag value.RunName; id = UMX.untag value.Id
          descriptionOverride = description |> Option.toObj
          resultsParameterSpans = value.ResultsParameterSpans |> List.map Contract.spanFrom |> List.toArray
          setupParameterSpans = setupSpans |> List.map Contract.spanFrom |> List.toArray }
    let readId (value: runDto) =
        Contract.version value.schemaVersion
        Contract.kind value.runKind |> ignore
        EntityId.validate value.id
        EntityId.validate value.parentProjectId
        EntityName.validate value.runName |> ignore
        UMX.tag<runId> value.id
    /// A run file contains overrides and must be resolved against its containing project.
    let toDomain (parent: project) (value: runDto) : run =
        let id = readId value
        if value.parentProjectId <> UMX.untag parent.Id then invalidArg "parentProjectId" "A run must reference its containing project ID."
        let parent = project.seal parent
        let outputSpans = Contract.array "resultsParameterSpans" value.resultsParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let setupSpans = Contract.array "setupParameterSpans" value.setupParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let description = Option.ofObj value.descriptionOverride
        let result =
            match parent, value.runKind with
            | project.RootProject p, "root" ->
                if not setupSpans.IsEmpty then
                    invalidArg "runKind" "A root run cannot have setup settings."
                RootRun.create p { Id = id; RunName = UMX.tag value.runName; ParentProjectId = UMX.tag value.parentProjectId; DescriptionOverride = description; ResultsParameterSpans = outputSpans } |> run.RootRun
            | project.BranchProject p, "branch" ->
                BranchRun.create p { Id = id; RunName = UMX.tag value.runName; ParentProjectId = UMX.tag value.parentProjectId; DescriptionOverride = description; ResultsParameterSpans = outputSpans
                                     SetupParameterSpans = setupSpans } |> run.BranchRun
            | _ -> invalidArg "runKind" "A project can only contain runs of its own type."
        FunctionConfiguration.validateRunInputs result
        result

module ResultsDataTypeConfigDto =
    let fromDomain (value: resultsDataTypeConfig) : resultsDataTypeConfigDto =
        { dataType = SortopiaDataTypeDto.fromDomain value.DataType; queryParameterKeys = value.QueryParameterKeys |> List.map UMX.untag |> List.toArray }
    let toDomain (value: resultsDataTypeConfigDto) : resultsDataTypeConfig =
        { DataType = SortopiaDataTypeDto.toDomain value.dataType; QueryParameterKeys = Contract.array "queryParameterKeys" value.queryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList }

module SettingsDto =
    let private fromRoot (value: rootProjectConfig) : projectSettingsDto = {
        description = value.Description
        resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        resultsFunctionName = UMX.untag value.ResultsFunctionName
        resultsDataTypes = value.ResultsDataTypes |> List.map ResultsDataTypeConfigDto.fromDomain |> List.toArray
        resultsSortopiaDataTypes = value.ResultsSortopiaDataTypes |> List.map SortopiaDataTypeDto.fromDomain |> List.toArray
        resultsQueryParameterKeys = value.ResultsQueryParameterKeys |> List.map UMX.untag |> List.toArray
        resultsParameterDefaults = value.ResultsParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupDatabaseName = ""
        setupFunctionName = ""
        setupDataTypes = [||]
        setupParameterDefaults = [||] }
    let private fromBranch (value: branchProjectConfig) : projectSettingsDto = {
        description = value.Description
        resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        resultsFunctionName = UMX.untag value.ResultsFunctionName
        resultsDataTypes = value.ResultsDataTypes |> List.map ResultsDataTypeConfigDto.fromDomain |> List.toArray
        resultsSortopiaDataTypes = value.ResultsSortopiaDataTypes |> List.map SortopiaDataTypeDto.fromDomain |> List.toArray
        resultsQueryParameterKeys = value.ResultsQueryParameterKeys |> List.map UMX.untag |> List.toArray
        resultsParameterDefaults = value.ResultsParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupDatabaseName = UMX.untag value.SetupDatabaseName
        setupFunctionName = UMX.untag value.SetupFunctionName
        setupDataTypes = value.SetupDataTypes |> List.map SetupDataTypeConfigDto.fromDomain |> List.toArray
        setupParameterDefaults = value.SetupParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray }
    let fromDomain = function RootProjectConfig v -> fromRoot v | BranchProjectConfig v -> fromBranch v
    let toDomain kind (value: projectSettingsDto) =
        if isNull (box value) then nullArg "configuration"
        let outputs = Contract.array "resultsSortopiaDataTypes" value.resultsSortopiaDataTypes |> Array.map SortopiaDataTypeDto.toDomain |> Array.toList
        let keys = Contract.array "resultsQueryParameterKeys" value.resultsQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let resultTypes = if isNull value.resultsDataTypes then Defaults.resultOutputs (UMX.tag value.resultsFunctionName) outputs keys else value.resultsDataTypes |> Array.map ResultsDataTypeConfigDto.toDomain |> Array.toList
        let setupTypes = Contract.array "setupDataTypes" value.setupDataTypes |> Array.map SetupDataTypeConfigDto.toDomain |> Array.toList
        let defaults = Contract.array "resultsParameterDefaults" value.resultsParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        let setupDefaults = Contract.array "setupParameterDefaults" value.setupParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        match kind with
        | hierarchyKind.Root ->
            if value.setupDatabaseName <> "" || value.setupFunctionName <> "" || not setupTypes.IsEmpty || not setupDefaults.IsEmpty then
                invalidArg "configuration" "A root project cannot have setup settings."
            RootProjectConfig { Description = value.description
                                Results = { DatabaseName = UMX.tag value.resultsDatabaseName; FunctionName = UMX.tag value.resultsFunctionName
                                            Outputs = resultTypes; ParameterDefaults = defaults } }
        | hierarchyKind.Branch ->
            BranchProjectConfig { Description = value.description
                                  Results = { DatabaseName = UMX.tag value.resultsDatabaseName; FunctionName = UMX.tag value.resultsFunctionName
                                              Outputs = resultTypes; ParameterDefaults = defaults }
                                  Setup = { DatabaseName = UMX.tag value.setupDatabaseName; FunctionName = UMX.tag value.setupFunctionName
                                            Outputs = setupTypes; ParameterDefaults = setupDefaults } }
        | _ -> invalidArg "kind" "Project kind must be root or branch."

module OverridesDto =
    let private fromRoot (value: rootDefaultOverrides) : projectSettingsDto = {
        description = value.Description |> Option.toObj
        resultsDatabaseName = value.ResultsDatabaseName |> Option.map UMX.untag |> Option.toObj
        resultsFunctionName = null
        resultsDataTypes = null
        resultsSortopiaDataTypes = value.ResultsSortopiaDataTypes |> Option.map (List.map SortopiaDataTypeDto.fromDomain >> List.toArray) |> Option.toObj
        resultsQueryParameterKeys = value.ResultsQueryParameterKeys |> Option.map (List.map UMX.untag >> List.toArray) |> Option.toObj
        resultsParameterDefaults = value.ResultsParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        setupDatabaseName = null
        setupFunctionName = null
        setupDataTypes = null
        setupParameterDefaults = null }
    let private fromBranch (value: branchDefaultOverrides) : projectSettingsDto = {
        description = value.Description |> Option.toObj
        resultsDatabaseName = value.ResultsDatabaseName |> Option.map UMX.untag |> Option.toObj
        resultsFunctionName = null
        resultsDataTypes = null
        resultsSortopiaDataTypes = value.ResultsSortopiaDataTypes |> Option.map (List.map SortopiaDataTypeDto.fromDomain >> List.toArray) |> Option.toObj
        resultsQueryParameterKeys = value.ResultsQueryParameterKeys |> Option.map (List.map UMX.untag >> List.toArray) |> Option.toObj
        resultsParameterDefaults = value.ResultsParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        setupDatabaseName = value.SetupDatabaseName |> Option.map UMX.untag |> Option.toObj
        setupFunctionName = null
        setupDataTypes = value.SetupDataTypes |> Option.map (List.map SetupDataTypeConfigDto.fromDomain >> List.toArray) |> Option.toObj
        setupParameterDefaults = value.SetupParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj }
    let fromDomain = function RootOverrides v -> fromRoot v | BranchOverrides v -> fromBranch v
    let toDomain kind (value: projectSettingsDto) =
        if isNull (box value) then nullArg "localOverrides"
        if not (isNull value.resultsFunctionName) || not (isNull value.setupFunctionName) then
            invalidArg "localOverrides" "Functions are selected on modules; projects inherit their function selections."
        match kind with
        | hierarchyKind.Root ->
            if not (isNull value.setupDatabaseName) || not (isNull value.setupFunctionName) || not (isNull value.setupDataTypes) || not (isNull value.setupParameterDefaults) then
                invalidArg "localOverrides" "Root defaults cannot have setup settings."
            RootOverrides {
                Description = Option.ofObj value.description
                ResultsDatabaseName = Option.ofObj value.resultsDatabaseName |> Option.map UMX.tag<resultsDatabaseName>
                ResultsSortopiaDataTypes = Option.ofObj value.resultsSortopiaDataTypes |> Option.map (Array.map SortopiaDataTypeDto.toDomain >> Array.toList)
                ResultsQueryParameterKeys = Option.ofObj value.resultsQueryParameterKeys |> Option.map (Array.map UMX.tag<paramKeyName> >> Array.toList)
                ResultsParameterDefaults = Option.ofObj value.resultsParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList) }
        | hierarchyKind.Branch ->
            BranchOverrides {
                Description = Option.ofObj value.description
                ResultsDatabaseName = Option.ofObj value.resultsDatabaseName |> Option.map UMX.tag<resultsDatabaseName>
                ResultsSortopiaDataTypes = Option.ofObj value.resultsSortopiaDataTypes |> Option.map (Array.map SortopiaDataTypeDto.toDomain >> Array.toList)
                ResultsQueryParameterKeys = Option.ofObj value.resultsQueryParameterKeys |> Option.map (Array.map UMX.tag<paramKeyName> >> Array.toList)
                ResultsParameterDefaults = Option.ofObj value.resultsParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList)
                SetupDatabaseName = Option.ofObj value.setupDatabaseName |> Option.map UMX.tag<setupDatabaseName>
                SetupDataTypes = Option.ofObj value.setupDataTypes |> Option.map (Array.map SetupDataTypeConfigDto.toDomain >> Array.toList)
                SetupParameterDefaults = Option.ofObj value.setupParameterDefaults |> Option.map (Array.map ParameterBindingDto.toDomain >> Array.toList) }
        | _ -> invalidArg "kind" "Defaults must be root or branch."

module ModuleSettingsDto =
    let fromProjectSettings (value: projectSettingsDto) : moduleSettingsDto = {
        description = value.description
        resultsFunctionName = value.resultsFunctionName
        resultsDataTypes = value.resultsDataTypes
        resultsSortopiaDataTypes = value.resultsSortopiaDataTypes
        resultsQueryParameterKeys = value.resultsQueryParameterKeys
        resultsParameterDefaults = value.resultsParameterDefaults
        setupFunctionName = value.setupFunctionName
        setupDataTypes = value.setupDataTypes
        setupParameterDefaults = value.setupParameterDefaults }
    let toProjectSettings (value: moduleSettingsDto) : projectSettingsDto = {
        description = value.description
        resultsFunctionName = value.resultsFunctionName
        resultsDataTypes = value.resultsDataTypes
        resultsSortopiaDataTypes = value.resultsSortopiaDataTypes
        resultsQueryParameterKeys = value.resultsQueryParameterKeys
        resultsParameterDefaults = value.resultsParameterDefaults
        setupFunctionName = value.setupFunctionName
        setupDataTypes = value.setupDataTypes
        setupParameterDefaults = value.setupParameterDefaults
        resultsDatabaseName = null; setupDatabaseName = null }
    let fromDomain value = value |> ModuleDefaults.toProject |> SettingsDto.fromDomain |> fromProjectSettings
    let toDomain kind value =
        let settings = toProjectSettings value
        { settings with resultsDatabaseName = "Results"; setupDatabaseName = if kind = hierarchyKind.Branch then "Setup" else "" }
        |> SettingsDto.toDomain kind |> ModuleDefaults.fromProject

module ModuleOverridesDto =
    let fromDomain value =
        let dto = value |> ModuleDefaults.toOverrides |> OverridesDto.fromDomain |> ModuleSettingsDto.fromProjectSettings
        match value with
        | RootModuleOverrides local -> { dto with resultsFunctionName = local.ResultsFunctionName |> Option.map UMX.untag |> Option.toObj }
        | BranchModuleOverrides local ->
            { dto with resultsFunctionName = local.ResultsFunctionName |> Option.map UMX.untag |> Option.toObj
                       setupFunctionName = local.SetupFunctionName |> Option.map UMX.untag |> Option.toObj }
    let toDomain kind (value: moduleSettingsDto) =
        let settings = ModuleSettingsDto.toProjectSettings value
        let local = { settings with resultsFunctionName = null; setupFunctionName = null } |> OverridesDto.toDomain kind |> ModuleDefaults.fromOverrides
        match local with
        | RootModuleOverrides local ->
            if not (isNull value.setupFunctionName) then invalidArg "setupFunctionName" "Root modules cannot have a setup function."
            RootModuleOverrides { local with ResultsFunctionName = Option.ofObj value.resultsFunctionName |> Option.map UMX.tag }
        | BranchModuleOverrides local ->
            BranchModuleOverrides { local with ResultsFunctionName = Option.ofObj value.resultsFunctionName |> Option.map UMX.tag
                                               SetupFunctionName = Option.ofObj value.setupFunctionName |> Option.map UMX.tag }

module ProjectDto =
    let settingsFromDomain (value: project) = SettingsDto.fromDomain value.Configuration
    let settingsToDomain kind value = SettingsDto.toDomain kind value
    let fromDomain (value: project) : projectDto = {
        schemaVersion = 13; isSealed = value.IsSealed; id = UMX.untag value.Id; projectKind = Contract.kindName value.Kind; projectName = UMX.untag value.ProjectName; folderName = value.FolderName
        configuration = settingsFromDomain value; localOverrides = OverridesDto.fromDomain value.LocalOverrides
        runs = value.Runs |> Array.map RunDto.fromDomain }
    let internal read depth inherited (value: projectDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        let kind = Contract.kind value.projectKind
        let savedRuns = Contract.array "runs" value.runs
        if savedRuns.Length > 0 && not value.isSealed then invalidArg "isSealed" "Projects containing runs must be sealed."
        let snapshot = SettingsDto.toDomain kind value.configuration
        if isNull value.configuration.resultsDataTypes then invalidArg "resultsDataTypes" "The saved project must include parameter keys for each results data type."
        if value.isSealed then
            match snapshot with
            | RootProjectConfig config -> FunctionConfiguration.validateResults config.Results
            | BranchProjectConfig config ->
                FunctionConfiguration.validateResults config.Results
                FunctionConfiguration.validateSetup true config.Setup
        let local = OverridesDto.toDomain kind value.localOverrides
        let configuration = if value.isSealed then snapshot else inherited |> Option.map (fun p -> Defaults.resolve p local) |> Option.defaultValue snapshot
        let parent = project.restore (UMX.tag value.id) (UMX.tag value.projectName) value.folderName configuration local value.isSealed [||]
        savedRuns |> Array.map (RunDto.toDomain parent) |> project.withRuns parent
    let toDomain value = read 0 None value

module ModuleDto =
    let rec fromDomain (value: workspaceModule) : moduleDto = {
        schemaVersion = 13; id = UMX.untag value.Id; moduleKind = Contract.kindName value.Kind; moduleName = UMX.untag value.ModuleName; folderName = value.FolderName
        configuration = ModuleSettingsDto.fromDomain value.Configuration; localOverrides = ModuleOverridesDto.fromDomain value.LocalOverrides
        modules = value.Modules |> Array.map fromDomain; projects = value.Projects |> Array.map ProjectDto.fromDomain }
    let rec internal read depth inherited (value: moduleDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        let kind = Contract.kind value.moduleKind
        let parent = inherited |> Option.defaultWith (fun () -> ModuleSettingsDto.toDomain kind value.configuration)
        let local = ModuleOverridesDto.toDomain kind value.localOverrides
        let effective = ModuleDefaults.resolve parent local
        workspaceModule.createInherited (UMX.tag value.id) (UMX.tag value.moduleName) value.folderName parent local
            (Contract.array "modules" value.modules |> Array.map (read (depth + 1) (Some effective)))
            (Contract.array "projects" value.projects |> Array.map (ProjectDto.read (depth + 1) (Some (ModuleDefaults.toProject effective))))
    let toDomain value = read 0 None value

module WorkspaceDto =
    let fromDomain (value: workspace) : workspaceDto = {
        schemaVersion = 13; id = UMX.untag value.Id; workspaceName = UMX.untag value.WorkspaceName; folderName = value.FolderName
        modules = value.Modules |> Array.map ModuleDto.fromDomain }
    let toDomain (value: workspaceDto) =
        Contract.version value.schemaVersion
        workspace.create (UMX.tag value.id) (UMX.tag value.workspaceName) value.folderName
            (Contract.array "modules" value.modules |> Array.map (fun m -> ModuleDto.read 1 (Some (ModuleDefaults.configuration (Contract.kind m.moduleKind = hierarchyKind.Branch))) m))

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
    let decodeRun (parent: project) bytes =
        if not parent.IsSealed then invalidArg "parentProjectId" "A saved run must reference a sealed project."
        bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.toDomain parent
    let readRunId bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.readId

    /// Resolve a standalone run by its stored parent ID, including immutable source snapshots.
    let decodeRunById (workspace: workspace) bytes =
        let dto = MessagePackSetup.deserialize<runDto> bytes
        let parent = workspace.FindProject (UMX.tag dto.parentProjectId) |> Option.defaultWith (fun () -> invalidArg "parentProjectId" "Run parent project was not found.")
        if not parent.IsSealed then invalidArg "parentProjectId" "A saved run must reference a sealed project."
        match RunDto.toDomain parent dto with
        | run.RootRun value -> run.RootRun value
        | run.BranchRun value ->
            BranchRunSources.resolve (fun id -> workspace.FindProject id |> Option.map (fun p -> p.Snapshot)) value |> run.BranchRun
