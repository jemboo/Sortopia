namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Workspaces
open Sortopia.Core.Serialization

module RunDtoSchema =
    let version = 18
    let supportsVersion value = value = version

[<CLIMutable>]
type parameterSpanDto = { keyName: string; values: string array; valueType: Nullable<int> }
[<CLIMutable>]
type sortopiaDataTypeDto = { kind: string; value: string }
[<CLIMutable>]
type resultsDataTypeConfigDto = { dataType: sortopiaDataTypeDto; queryParameterKeys: string array }
[<CLIMutable>]
type setupDataTypeConfigDto = { dataType: sortopiaDataTypeDto; sourceProjectId: Guid; queryParameterKeys: string array }
[<CLIMutable>]
type setupSourceConfigDto = { dataType: sortopiaDataTypeDto; sourceProjectId: Guid }
[<CLIMutable>]
type projectLocalSettingsDto = {
    resultsQueries: resultsDataTypeConfigDto array
    setupQueries: resultsDataTypeConfigDto array
    description: string
    resultsDatabaseName: string
    resultsParameterDefaults: parameterBindingDto array
    excludedResultsDefaults: string array
    setupDatabaseName: string
    setupSources: setupSourceConfigDto array
    setupParameterDefaults: parameterBindingDto array
    excludedSetupDefaults: string array
}
[<CLIMutable>]
type moduleOverridesDto = {
    description: string
    resultsFunctionName: string
    resultsParameterDefaults: parameterBindingDto array
    excludedResultsDefaults: string array
    setupFunctionName: string
    setupSources: setupSourceConfigDto array
    setupParameterDefaults: parameterBindingDto array
    excludedSetupDefaults: string array
}
[<CLIMutable>]
type projectSettingsDto = {
    description: string
    resultsDatabaseName: string
    resultsFunctionName: string
    resultsFunctionVersion: string
    resultsDataTypes: resultsDataTypeConfigDto array
    resultsParameterDefaults: parameterBindingDto array
    setupDatabaseName: string
    setupFunctionName: string
    setupFunctionVersion: string
    setupDataTypes: setupDataTypeConfigDto array
    setupParameterDefaults: parameterBindingDto array
}
[<CLIMutable>]
type moduleSettingsDto = {
    description: string
    resultsFunctionName: string
    resultsParameterDefaults: parameterBindingDto array
    setupFunctionName: string
    setupSources: setupSourceConfigDto array
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
    reporting: sgdReportingConfig option
    sgd: sgdRunConfig option
}
module SortopiaDataTypeDto =
    let fromDomain (value: sortopiaDataType) : sortopiaDataTypeDto =
        SortopiaDataType.validate value
        { kind = SortopiaDataType.kindName value; value = SortopiaDataType.text value }
    let toDomain (value: sortopiaDataTypeDto) =
        if isNull (box value) then nullArg "dataType"
        SortopiaDataType.create value.kind value.value

module private Contract =
    let version value =
        if not (RunDtoSchema.supportsVersion value) then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}. Create a new workspace in the current format."
    let kind value =
        match value with
        | "root" -> hierarchyKind.Root
        | "branch" -> hierarchyKind.Branch
        | _ -> invalidArg "kind" "Kind must be root or branch."
    let array name (value: 'a array) =
        if isNull value then invalidArg name "Required collection is missing."
        value
    let spanTo (value: parameterSpanDto) =
        let kind = if value.valueType.HasValue then enum<parameterValueType> value.valueType.Value else ParamKeys.valueType (UMX.tag value.keyName)
        ParameterSpan.fromTexts (UMX.tag value.keyName) kind (array "values" value.values |> Array.toList)
    let spanFrom (value: parameterSpan) = { keyName = UMX.untag value.KeyName; values = List.toArray value.Values; valueType = Nullable(int value.ValueType) }
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

module SetupSourceConfigDto =
    let fromDomain (value: setupSourceConfig) : setupSourceConfigDto =
        { dataType = SortopiaDataTypeDto.fromDomain value.DataType; sourceProjectId = UMX.untag value.SourceProjectId }
    let toDomain (value: setupSourceConfigDto) : setupSourceConfig =
        { DataType = SortopiaDataTypeDto.toDomain value.dataType; SourceProjectId = UMX.tag value.sourceProjectId }

module RunDto =
    let fromDomain (value: run) : runDto =
        let description = value.DescriptionOverride
        let setupSpans = value.SetupParameterSpans |> Option.defaultValue []
        { schemaVersion = RunDtoSchema.version; runKind = if value.HasSetup then "branch" else "root"
          parentProjectId = UMX.untag value.ParentProjectId; runName = UMX.untag value.RunName; id = UMX.untag value.Id
          descriptionOverride = description |> Option.toObj
          reporting = value.Reporting
          sgd = value.Sgd
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
    let toDomain (parent: projectSnapshot) (value: runDto) : run =
        let id = readId value
        if value.parentProjectId <> UMX.untag parent.Id then invalidArg "parentProjectId" "A run must reference its containing project ID."
        let outputSpans = Contract.array "resultsParameterSpans" value.resultsParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let setupSpans = Contract.array "setupParameterSpans" value.setupParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let description = Option.ofObj value.descriptionOverride
        // These two records are mutable from C#. Runtime callers must not receive
        // references into the editable document or a protected saved revision.
        let reporting = value.reporting |> Option.map (fun value ->
            SgdReporting.validate value |> ignore
            { value with SnapshotSchedule = value.SnapshotSchedule })
        let sgd = value.sgd |> Option.map (fun value -> { value with SnapshotInterval = value.SnapshotInterval })
        if (Contract.kind value.runKind = hierarchyKind.Branch) <> parent.Configuration.HasSetup then invalidArg "runKind" "A project can only contain runs of its own type."
        if not parent.Configuration.HasSetup && not setupSpans.IsEmpty then invalidArg "runKind" "A root run cannot have setup settings."
        let result = Run.create parent {
            Id = id; RunName = UMX.tag value.runName; ParentProjectId = UMX.tag value.parentProjectId
            DescriptionOverride = description; ResultsParameterSpans = outputSpans; Reporting = reporting; Sgd = sgd
            SetupParameterSpans = if parent.Configuration.HasSetup then Some setupSpans else None }
        FunctionConfiguration.validateSavedRunInputs result
        result

module ResultsDataTypeConfigDto =
    let fromDomain (value: resultsDataTypeConfig) : resultsDataTypeConfigDto =
        { dataType = SortopiaDataTypeDto.fromDomain value.DataType; queryParameterKeys = value.QueryParameterKeys |> List.map UMX.untag |> List.toArray }
    let toDomain (value: resultsDataTypeConfigDto) : resultsDataTypeConfig =
        { DataType = SortopiaDataTypeDto.toDomain value.dataType; QueryParameterKeys = Contract.array "queryParameterKeys" value.queryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList }

module SettingsDto =
    let fromDomain (value: projectConfiguration) : projectSettingsDto = {
        description = value.Description
        resultsDatabaseName = UMX.untag value.Results.DatabaseName
        resultsFunctionName = UMX.untag value.Results.FunctionName
        resultsFunctionVersion = value.Results.FunctionVersion
        resultsDataTypes = value.Results.Outputs |> List.map ResultsDataTypeConfigDto.fromDomain |> List.toArray
        resultsParameterDefaults = value.Results.ParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupDatabaseName = value.Setup |> Option.map (fun setup -> UMX.untag setup.DatabaseName) |> Option.defaultValue ""
        setupFunctionName = value.Setup |> Option.map (fun setup -> UMX.untag setup.FunctionName) |> Option.defaultValue ""
        setupFunctionVersion = value.Setup |> Option.map _.FunctionVersion |> Option.defaultValue ""
        setupDataTypes = value.Setup |> Option.map (fun setup -> setup.Outputs |> List.map SetupDataTypeConfigDto.fromDomain |> List.toArray) |> Option.defaultValue [||]
        setupParameterDefaults = value.Setup |> Option.map (fun setup -> setup.ParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray) |> Option.defaultValue [||] }
    /// Reading stored results does not require parsing a source's setup settings or runs.
    let resultsToDomain (value: projectSettingsDto) : resultsConfig =
        if isNull (box value) then nullArg "configuration"
        { DatabaseName = UMX.tag value.resultsDatabaseName; FunctionName = UMX.tag value.resultsFunctionName
          FunctionVersion = value.resultsFunctionVersion
          Outputs = Contract.array "resultsDataTypes" value.resultsDataTypes |> Array.map ResultsDataTypeConfigDto.toDomain |> Array.toList
          ParameterDefaults = Contract.array "resultsParameterDefaults" value.resultsParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList }
    let toDomain kind (value: projectSettingsDto) : projectConfiguration =
        let results = resultsToDomain value
        let setupTypes = Contract.array "setupDataTypes" value.setupDataTypes |> Array.map SetupDataTypeConfigDto.toDomain |> Array.toList
        let setupDefaults = Contract.array "setupParameterDefaults" value.setupParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        let setup =
            match kind with
            | hierarchyKind.Root ->
                if value.setupDatabaseName <> "" || value.setupFunctionName <> "" || value.setupFunctionVersion <> "" || not setupTypes.IsEmpty || not setupDefaults.IsEmpty then
                    invalidArg "configuration" "A root project cannot have setup settings."
                None
            | hierarchyKind.Branch ->
                Some { DatabaseName = UMX.tag value.setupDatabaseName; FunctionName = UMX.tag value.setupFunctionName; FunctionVersion = value.setupFunctionVersion
                       Outputs = setupTypes; ParameterDefaults = setupDefaults }
            | _ -> invalidArg "kind" "Project kind must be root or branch."
        { Description = value.description; Results = results; Setup = setup }
module private ConfigurationValuesDto =
    // Draft documents retain excluded text for restoration. Runtime overrides only
    // need active bindings, so an unfinished excluded value must not be parsed.
    let activeDefaults excluded values =
        let excluded = if isNull excluded then Set.empty else Set.ofArray excluded
        values |> Option.ofObj |> Option.map (Array.filter (fun (binding: parameterBindingDto) -> not (Set.contains binding.keyName excluded)) >> Array.map ParameterBindingDto.toDomain >> Array.toList)
module ProjectLocalSettingsDto =
    open ConfigurationValuesDto
    let fromDomain (value: projectLocalSettings) : projectLocalSettingsDto = {
        description = Option.toObj value.Description
        resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        excludedResultsDefaults = value.ExcludedResultsDefaults |> List.map UMX.untag |> List.toArray
        resultsQueries = value.ResultsQueries |> List.map ResultsDataTypeConfigDto.fromDomain |> List.toArray
        resultsParameterDefaults = value.ResultsParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        setupDatabaseName = value.Setup |> Option.map (fun setup -> UMX.untag setup.DatabaseName) |> Option.toObj
        setupSources = value.Setup |> Option.bind _.Sources |> Option.map (List.map SetupSourceConfigDto.fromDomain >> List.toArray) |> Option.toObj
        excludedSetupDefaults = value.Setup |> Option.map (fun setup -> setup.ExcludedDefaults |> List.map UMX.untag |> List.toArray) |> Option.defaultValue [||]
        setupQueries = value.Setup |> Option.map (fun setup -> setup.Queries |> List.map ResultsDataTypeConfigDto.fromDomain |> List.toArray) |> Option.toObj
        setupParameterDefaults = value.Setup |> Option.bind _.ParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj }
    let toDomain (parent: projectConfiguration) (value: projectLocalSettingsDto) : projectLocalSettings =
        if isNull (box value) then nullArg "localSettings"
        let initial = ProjectResolution.initial parent
        let queries defaults values =
            if isNull values then defaults else values |> Array.map ResultsDataTypeConfigDto.toDomain |> Array.toList
        let exclusions (values: string array) = (if isNull values then [||] else values) |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let setup =
            match initial.Setup with
            | None ->
                if not (isNull value.setupQueries) || not (List.isEmpty (exclusions value.excludedSetupDefaults)) || not (isNull value.setupDatabaseName) || not (isNull value.setupSources) || not (isNull value.setupParameterDefaults) then
                    invalidArg "localSettings" "A project without setup cannot have setup settings."
                None
            | Some initial ->
                Some {
                    DatabaseName = if isNull value.setupDatabaseName then initial.DatabaseName else UMX.tag value.setupDatabaseName
                    Sources = Option.ofObj value.setupSources |> Option.map (Array.map SetupSourceConfigDto.toDomain >> Array.toList)
                    ExcludedDefaults = exclusions value.excludedSetupDefaults
                    Queries = queries initial.Queries value.setupQueries
                    ParameterDefaults = activeDefaults value.excludedSetupDefaults value.setupParameterDefaults }
        { Description = Option.ofObj value.description
          ResultsDatabaseName = if isNull value.resultsDatabaseName then initial.ResultsDatabaseName else UMX.tag value.resultsDatabaseName
          ExcludedResultsDefaults = exclusions value.excludedResultsDefaults
          ResultsQueries = queries initial.ResultsQueries value.resultsQueries
          ResultsParameterDefaults = activeDefaults value.excludedResultsDefaults value.resultsParameterDefaults
          Setup = setup }
module ModuleSettingsDto =
    let fromDomain (value: moduleConfiguration) : moduleSettingsDto = {
        description = value.Description; resultsFunctionName = UMX.untag value.ResultsFunctionName
        resultsParameterDefaults = value.ResultsParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray
        setupFunctionName = value.Setup |> Option.map (fun setup -> UMX.untag setup.FunctionName) |> Option.defaultValue ""
        setupSources = value.Setup |> Option.map (fun setup -> setup.Sources |> List.map SetupSourceConfigDto.fromDomain |> List.toArray) |> Option.defaultValue [||]
        setupParameterDefaults = value.Setup |> Option.map (fun setup -> setup.ParameterDefaults |> List.map ParameterBindingDto.fromDomain |> List.toArray) |> Option.defaultValue [||] }
    let toDomain kind (value: moduleSettingsDto) : moduleConfiguration =
        if isNull (box value) then nullArg "configuration"
        let defaults = Contract.array "resultsParameterDefaults" value.resultsParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        let setupDefaults = Contract.array "setupParameterDefaults" value.setupParameterDefaults |> Array.map ParameterBindingDto.toDomain |> Array.toList
        let sources = Contract.array "setupSources" value.setupSources |> Array.map SetupSourceConfigDto.toDomain |> Array.toList
        let setup =
            match kind with
            | hierarchyKind.Root ->
                if value.setupFunctionName <> "" || not sources.IsEmpty || not setupDefaults.IsEmpty then
                    invalidArg "configuration" "A root module cannot have setup settings."
                None
            | hierarchyKind.Branch ->
                Some { FunctionName = UMX.tag value.setupFunctionName; Sources = sources; ParameterDefaults = setupDefaults }
            | _ -> invalidArg "kind" "Module kind must be root or branch."
        { Description = value.description; ResultsFunctionName = UMX.tag value.resultsFunctionName; ResultsParameterDefaults = defaults; Setup = setup }

module ModuleOverridesDto =
    open ConfigurationValuesDto
    let fromDomain (value: moduleOverrides) : moduleOverridesDto = {
        description = Option.toObj value.Description
        resultsFunctionName = value.ResultsFunctionName |> Option.map UMX.untag |> Option.toObj
        resultsParameterDefaults = value.ResultsParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        excludedResultsDefaults = value.ExcludedResultsDefaults |> List.map UMX.untag |> List.toArray
        setupFunctionName = value.Setup |> Option.bind _.FunctionName |> Option.map UMX.untag |> Option.toObj
        setupSources = value.Setup |> Option.bind _.Sources |> Option.map (List.map SetupSourceConfigDto.fromDomain >> List.toArray) |> Option.toObj
        setupParameterDefaults = value.Setup |> Option.bind _.ParameterDefaults |> Option.map (List.map ParameterBindingDto.fromDomain >> List.toArray) |> Option.toObj
        excludedSetupDefaults = value.Setup |> Option.map (fun setup -> setup.ExcludedDefaults |> List.map UMX.untag |> List.toArray) |> Option.defaultValue [||] }
    let toDomain kind (value: moduleOverridesDto) : moduleOverrides =
        if isNull (box value) then nullArg "localOverrides"
        let exclusions (values: string array) = (if isNull values then [||] else values) |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let setup =
            match kind with
            | hierarchyKind.Root ->
                if not (isNull value.setupFunctionName) || not (isNull value.setupSources) || not (isNull value.setupParameterDefaults) || not (List.isEmpty (exclusions value.excludedSetupDefaults)) then
                    invalidArg "localOverrides" "Root defaults cannot have setup settings."
                None
            | hierarchyKind.Branch ->
                Some {
                    FunctionName = Option.ofObj value.setupFunctionName |> Option.map UMX.tag
                    Sources = Option.ofObj value.setupSources |> Option.map (Array.map SetupSourceConfigDto.toDomain >> Array.toList)
                    ParameterDefaults = activeDefaults value.excludedSetupDefaults value.setupParameterDefaults
                    ExcludedDefaults = exclusions value.excludedSetupDefaults }
            | _ -> invalidArg "kind" "Module kind must be root or branch."
        { Description = Option.ofObj value.description
          ResultsFunctionName = Option.ofObj value.resultsFunctionName |> Option.map UMX.tag
          ResultsParameterDefaults = activeDefaults value.excludedResultsDefaults value.resultsParameterDefaults
          ExcludedResultsDefaults = exclusions value.excludedResultsDefaults
          Setup = setup }

/// Persisted run inputs are resolved against a sealed project.
module RunCodec =
    let encodeRun value = value |> RunDto.fromDomain |> MessagePackSetup.serialize
    let decodeRun (parent: projectSnapshot) bytes =
        bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.toDomain parent
    let readRunId bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.readId
