namespace Sortopia.Runs

open FSharp.UMX
open Sortopia.Core  

[<Measure>] type databaseName
[<Measure>] type resultsDatabaseName
[<Measure>] type setupDatabaseName
[<Measure>] type workspaceName
[<Measure>] type moduleName
[<Measure>] type projectName
[<Measure>] type runId
[<Measure>] type projectId
[<Measure>] type moduleId
[<Measure>] type workspaceId

[<Measure>] type runName
[<Measure>] type resultsFunctionName
[<Measure>] type setupFunctionName


type resultsConfig = {
    DatabaseName: string<resultsDatabaseName>
    FunctionName: string<resultsFunctionName>
    SortopiaDataTypes: sortopiaDataType list
    QueryParameterKeys: string<paramKeyName> list
    ParameterDefaults: parameterBinding list
}

type setupDataTypeConfig = {
    DataType: sortopiaDataType
    SourceProjectId: Guid<projectId>
    QueryParameterKeys: string<paramKeyName> list
}

type setupConfig =
    {
        DatabaseName: string<setupDatabaseName>
        FunctionName: string<setupFunctionName>
        DataTypes: setupDataTypeConfig list
        ParameterDefaults: parameterBinding list
    }
    member this.SortopiaDataTypes = this.DataTypes |> List.map (fun entry -> entry.DataType)
    member this.SourceProjectIds = this.DataTypes |> List.map (fun entry -> entry.DataType, entry.SourceProjectId) |> Map.ofList
    member this.QueryParameterKeys = this.DataTypes |> List.map (fun entry -> entry.DataType, entry.QueryParameterKeys) |> Map.ofList

type rootProjectConfig =
    { Description: string; Results: resultsConfig }
    member this.ResultsDatabaseName = this.Results.DatabaseName
    member this.ResultsFunctionName = this.Results.FunctionName
    member this.ResultsSortopiaDataTypes = this.Results.SortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.Results.QueryParameterKeys
    member this.ResultsParameterDefaults = this.Results.ParameterDefaults

type branchProjectConfig =
    { Description: string; Results: resultsConfig; Setup: setupConfig }
    member this.ResultsDatabaseName = this.Results.DatabaseName
    member this.ResultsFunctionName = this.Results.FunctionName
    member this.ResultsSortopiaDataTypes = this.Results.SortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.Results.QueryParameterKeys
    member this.ResultsParameterDefaults = this.Results.ParameterDefaults
    member this.SetupDatabaseName = this.Setup.DatabaseName
    member this.SetupFunctionName = this.Setup.FunctionName
    member this.SetupDataTypes = this.Setup.DataTypes
    member this.SetupSortopiaDataTypes = this.Setup.SortopiaDataTypes
    member this.SourceProjectIds = this.Setup.SourceProjectIds
    member this.SetupQueryParameterKeys = this.Setup.QueryParameterKeys
    member this.SetupParameterDefaults = this.Setup.ParameterDefaults

type sourceProjectSnapshot = {
    ProjectId: Guid<projectId>
    ProjectName: string<projectName>
    Results: resultsConfig
    IsSealed: bool
}

type projectConfiguration =
    | RootProjectConfig of rootProjectConfig
    | BranchProjectConfig of branchProjectConfig

/// Project contracts keep run construction independent of the workspace hierarchy.
type IRootProject =
    abstract Id: Guid<projectId>
    abstract IsSealed: bool
    abstract ProjectName: string<projectName>
    abstract Configuration: rootProjectConfig

type IBranchProject =
    abstract Id: Guid<projectId>
    abstract IsSealed: bool
    abstract ProjectName: string<projectName>
    abstract Configuration: branchProjectConfig

module ProjectConfiguration =
    let private name argument value =
        if System.String.IsNullOrWhiteSpace value then invalidArg argument "Name must not be null, empty, or whitespace."
    let private keys argument (values: string<paramKeyName> list) =
        if isNull (box values) then nullArg argument
        for value in values do name argument (UMX.untag value)
        if (Set.ofList values).Count <> values.Length then invalidArg argument "Query parameter keys must be unique."
    let private outputs argument (values: sortopiaDataType list) =
        if isNull (box values) then nullArg argument
        values |> List.iter SortopiaDataType.validate
        if (Set.ofList values).Count <> values.Length then invalidArg argument "Output data types must be unique."
    let validateRoot (value: rootProjectConfig) =
        if isNull (box value) then nullArg "configuration"
        if isNull (box value.Results) then nullArg "results"
        if isNull value.Description then nullArg "description"
        name "resultsDatabaseName" (UMX.untag value.ResultsDatabaseName)
        name "resultsFunctionName" (UMX.untag value.ResultsFunctionName)
        outputs "resultsSortopiaDataTypes" value.ResultsSortopiaDataTypes
        keys "resultsQueryParameterKeys" value.ResultsQueryParameterKeys
        ParameterBinding.validateUnique value.ResultsParameterDefaults
    let validateBranch (value: branchProjectConfig) =
        if isNull (box value) then nullArg "configuration"
        if isNull (box value.Setup) then nullArg "setup"
        if isNull (box value.Setup.DataTypes) then nullArg "setupDataTypes"
        for entry in value.Setup.DataTypes do
            if isNull (box entry) then nullArg "setupDataType"
        validateRoot { Description = value.Description; Results = value.Results }
        name "setupDatabaseName" (UMX.untag value.SetupDatabaseName)
        name "setupFunctionName" (UMX.untag value.SetupFunctionName)
        outputs "setupSortopiaDataTypes" value.SetupSortopiaDataTypes
        for entry in value.SetupDataTypes do
            if UMX.untag entry.SourceProjectId = System.Guid.Empty then invalidArg "sourceProjectId" "Choose a source project for every setup data type."
            keys "setupQueryParameterKeys" entry.QueryParameterKeys
        ParameterBinding.validateUnique value.SetupParameterDefaults

module private RunConfiguration =
    let identity (id: Guid<runId>) (projectName: string<projectName>) (runName: string<runName>) =
        if UMX.untag id = System.Guid.Empty then invalidArg "id" "Run ID must not be empty."
        for argument, value in ["projectName", UMX.untag projectName; "runName", UMX.untag runName] do
            if System.String.IsNullOrWhiteSpace value then invalidArg argument "Name must not be null, empty, or whitespace."
    let spans argument queryKeys defaults (spans: parameterSpan list) =
        ParameterSets.validateSpans defaults spans
        let names = Set.ofSeq (seq {
            for span in spans do yield span.KeyName
            for value in defaults do yield value.KeyName })
        for key in queryKeys do
            if not (Set.contains key names) then invalidArg argument $"Query key '{key}' has no default value or matching parameter span."

/// Only run identity, an optional description, and execution parameters are supplied per root run.
type rootRunConfig = {
    Id: Guid<runId>
    RunName: string<runName>
    ParentProjectId: Guid<projectId>
    DescriptionOverride: string option
    ResultsParameterSpans: parameterSpan list
}

type rootRun =
    private { projectName: string<projectName>; projectConfiguration: rootProjectConfig; config: rootRunConfig }
    member this.ProjectName = this.projectName
    member this.RunName = this.config.RunName
    member this.Id = this.config.Id
    member this.ParentProjectId = this.config.ParentProjectId
    member this.DescriptionOverride = this.config.DescriptionOverride
    member this.ProjectConfiguration = this.projectConfiguration
    member this.Configuration = this.config
    member this.Description = this.config.DescriptionOverride |> Option.defaultValue this.projectConfiguration.Description
    member this.ResultsDatabaseName = this.projectConfiguration.ResultsDatabaseName
    member this.ResultsFunctionName = this.projectConfiguration.ResultsFunctionName
    member this.ResultsParameterSpans = this.config.ResultsParameterSpans
    member this.ResultsSortopiaDataTypes = this.projectConfiguration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.projectConfiguration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.projectConfiguration.ResultsParameterDefaults

module RootRun =
    let create (parent: IRootProject) (config: rootRunConfig) : rootRun =
        if config.ParentProjectId <> parent.Id then invalidArg "parentProjectId" "The run must reference its containing project by ID."
        if not parent.IsSealed then invalidArg "parent" "Seal the project before creating a run."
        let settings = parent.Configuration
        ProjectConfiguration.validateRoot settings
        RunConfiguration.identity config.Id parent.ProjectName config.RunName
        if config.DescriptionOverride |> Option.exists isNull then nullArg "description"
        RunConfiguration.spans "resultsParameterSpans" settings.ResultsQueryParameterKeys settings.ResultsParameterDefaults config.ResultsParameterSpans
        { projectName = parent.ProjectName; projectConfiguration = settings; config = config }
    let withNames projectName runName (value: rootRun) =
        if projectName <> value.ProjectName then invalidArg "projectName" "A run's parent project name is fixed."
        RunConfiguration.identity value.Id projectName runName
        { value with projectName = projectName; config = { value.config with RunName = runName } }
    let makeResultsParameterSets (root: rootRun) = ParameterSets.create root.ResultsParameterDefaults root.ResultsParameterSpans

type branchRunConfig = {
    Id: Guid<runId>
    RunName: string<runName>
    ParentProjectId: Guid<projectId>
    DescriptionOverride: string option
    ResultsParameterSpans: parameterSpan list
    SetupFunctionNameOverride: string<setupFunctionName> option
    SetupParameterSpans: parameterSpan list
}

type branchRun =
    private { projectName: string<projectName>; projectConfiguration: branchProjectConfig; config: branchRunConfig; sources: Map<sortopiaDataType, sourceProjectSnapshot> }
    member this.ResolvedSources = this.sources
    member this.SetupDataTypes = this.projectConfiguration.SetupDataTypes
    member this.SourceProjectIds = this.projectConfiguration.SourceProjectIds
    member this.ProjectName = this.projectName
    member this.RunName = this.config.RunName
    member this.Id = this.config.Id
    member this.ParentProjectId = this.config.ParentProjectId
    member this.DescriptionOverride = this.config.DescriptionOverride
    member this.ProjectConfiguration = this.projectConfiguration
    member this.Configuration = this.config
    member this.Description = this.config.DescriptionOverride |> Option.defaultValue this.projectConfiguration.Description
    member this.ResultsDatabaseName = this.projectConfiguration.ResultsDatabaseName
    member this.ResultsFunctionName = this.projectConfiguration.ResultsFunctionName
    member this.ResultsParameterSpans = this.config.ResultsParameterSpans
    member this.ResultsSortopiaDataTypes = this.projectConfiguration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.projectConfiguration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.projectConfiguration.ResultsParameterDefaults
    member this.SetupDatabaseName = this.projectConfiguration.SetupDatabaseName
    member this.SetupFunctionName = this.config.SetupFunctionNameOverride |> Option.defaultValue this.projectConfiguration.SetupFunctionName
    member this.SetupParameterSpans = this.config.SetupParameterSpans
    member this.SetupSortopiaDataTypes = this.projectConfiguration.SetupSortopiaDataTypes
    member this.SetupQueryParameterKeys = this.projectConfiguration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.projectConfiguration.SetupParameterDefaults

module BranchRun =
    let create (parent: IBranchProject) (config: branchRunConfig) : branchRun =
        if config.ParentProjectId <> parent.Id then invalidArg "parentProjectId" "The run must reference its containing project by ID."
        if not parent.IsSealed then invalidArg "parent" "Seal the project before creating a run."
        let settings = parent.Configuration
        ProjectConfiguration.validateBranch settings
        RunConfiguration.identity config.Id parent.ProjectName config.RunName
        if config.DescriptionOverride |> Option.exists isNull then nullArg "description"
        for argument, value in ["setupFunctionName", config.SetupFunctionNameOverride |> Option.map UMX.untag] do
            if value |> Option.exists System.String.IsNullOrWhiteSpace then invalidArg argument "An override must not be blank; omit it to inherit the project setting."
        RunConfiguration.spans "resultsParameterSpans" settings.ResultsQueryParameterKeys settings.ResultsParameterDefaults config.ResultsParameterSpans
        RunConfiguration.spans "setupParameterSpans" (settings.SetupDataTypes |> List.collect (fun entry -> entry.QueryParameterKeys)) settings.SetupParameterDefaults config.SetupParameterSpans
        { projectName = parent.ProjectName; projectConfiguration = settings; config = config; sources = Map.empty }
    let withNames projectName runName (value: branchRun) =
        if projectName <> value.ProjectName then invalidArg "projectName" "A run's parent project name is fixed."
        RunConfiguration.identity value.Id projectName runName
        { value with projectName = projectName; config = { value.config with RunName = runName } }
    let makeResultsParameterSets (branch: branchRun) = ParameterSets.create branch.ResultsParameterDefaults branch.ResultsParameterSpans
    let makeSetupParameterSets (branch: branchRun) = ParameterSets.create branch.SetupParameterDefaults branch.SetupParameterSpans

module BranchRunSources =
    let resolve (find: Guid<projectId> -> sourceProjectSnapshot option) (value: branchRun) =
        let sources = value.SetupDataTypes |> List.map (fun entry ->
            let source = find entry.SourceProjectId |> Option.defaultWith (fun () -> invalidArg "sourceProjectId" $"Source project '{entry.SourceProjectId}' was not found.")
            if not source.IsSealed then invalidArg "sourceProjectId" $"Source project '{source.ProjectName}' must be sealed."
            if not (List.contains entry.DataType source.Results.SortopiaDataTypes) then invalidArg "sourceProjectId" $"Source project '{source.ProjectName}' does not provide '{entry.DataType}'."
            RunConfiguration.spans "setupParameterSpans" source.Results.QueryParameterKeys value.SetupParameterDefaults value.SetupParameterSpans
            entry.DataType, source) |> Map.ofList
        { value with sources = sources }

type run =
    | RootRun of rootRun
    | BranchRun of branchRun
    member this.ParentProjectId = match this with RootRun r -> r.ParentProjectId | BranchRun r -> r.ParentProjectId
    member this.HasSetup = match this with RootRun _ -> false | BranchRun _ -> true
    member this.ProjectName = match this with RootRun r -> r.ProjectName | BranchRun r -> r.ProjectName
    member this.RunName = match this with RootRun r -> r.RunName | BranchRun r -> r.RunName
    member this.Id = match this with RootRun r -> r.Id | BranchRun r -> r.Id
    member this.Description = match this with RootRun r -> r.Description | BranchRun r -> r.Description
    member this.ResultsDatabaseName = match this with RootRun r -> r.ResultsDatabaseName | BranchRun r -> r.ResultsDatabaseName
    member this.ResultsFunctionName = match this with RootRun r -> r.ResultsFunctionName | BranchRun r -> r.ResultsFunctionName
    member this.ResultsParameterSpans = match this with RootRun r -> r.ResultsParameterSpans | BranchRun r -> r.ResultsParameterSpans
    member this.ResultsSortopiaDataTypes = match this with RootRun r -> r.ResultsSortopiaDataTypes | BranchRun r -> r.ResultsSortopiaDataTypes
    member this.ResultsParameterDefaults = match this with RootRun r -> r.ResultsParameterDefaults | BranchRun r -> r.ResultsParameterDefaults
    member this.ResultsQueryParameterKeys = match this with RootRun r -> r.ResultsQueryParameterKeys | BranchRun r -> r.ResultsQueryParameterKeys
    member this.SetupDatabaseName = match this with RootRun _ -> None | BranchRun r -> Some r.SetupDatabaseName
    member this.SetupFunctionName = match this with RootRun _ -> None | BranchRun r -> Some r.SetupFunctionName
    member this.SetupParameterSpans = match this with RootRun _ -> None | BranchRun r -> Some r.SetupParameterSpans
    member this.SetupSortopiaDataTypes = match this with RootRun _ -> None | BranchRun r -> Some r.SetupSortopiaDataTypes
    member this.SetupParameterDefaults = match this with RootRun _ -> None | BranchRun r -> Some r.SetupParameterDefaults
    member this.SetupQueryParameterKeys = match this with RootRun _ -> None | BranchRun r -> Some r.SetupQueryParameterKeys

[<Measure>] type queryParamsId

/// Identifies results by database, project, output type, and selected parameter values.
type queryParams =
    private {
        databaseName: string<databaseName>
        projectName: string<projectName>
        projectId: Guid<projectId>
        outputDataType: sortopiaDataType
        properties: Map<string<paramKeyName>, string>
        id: Guid<queryParamsId>
    }

    member this.ProjectId = this.projectId
    member this.DatabaseName = this.databaseName
    member this.ProjectName = this.projectName
    member this.OutputDataType = this.outputDataType
    member this.Properties = this.properties
    member this.Id = this.id

module QueryParams =
    let private create database (projectId: Guid<projectId>) project outputType (keys: string<paramKeyName> list) (parameters: parameterSet) =
        let property key =
            match parameters.TryFindText(key: string<paramKeyName>) with
            | Some value -> key, value
            | None -> invalidArg "parameters" $"Query for '{outputType}' requires parameter '{key}'."

        let properties = keys |> List.map property |> Map.ofList
        let id =
            seq {
                yield box database
                yield box (UMX.untag projectId)
                // GeneSort reserves these positions for optional replica and generation.
                yield box false
                yield box false
                yield box (SortopiaDataType.kindName outputType)
                yield box (SortopiaDataType.text outputType)
                yield box properties.Count
                // F# maps enumerate in ascending key order.
                for KeyValue(key, value) in properties do
                    yield box key
                    yield box value
            }
            |> GuidUtils.guidFromObjs
            |> UMX.tag<queryParamsId>

        { databaseName = database
          projectName = project
          projectId = projectId
          outputDataType = outputType
          properties = properties
          id = id }

    /// Builds a result query for either run type.
    let forResults (run: run) outputType parameters =
        if not (List.contains outputType run.ResultsSortopiaDataTypes) then
            invalidArg "outputType" $"Run '{run.RunName}' does not declare result type '{outputType}'."
        create (UMX.cast<resultsDatabaseName, databaseName> run.ResultsDatabaseName) run.ParentProjectId run.ProjectName outputType run.ResultsQueryParameterKeys parameters

    /// Builds a setup query using the keys configured for the requested output type.
    let forSetup (branch: branchRun) outputType parameters =
        if not (List.contains outputType branch.SetupSortopiaDataTypes) then
            invalidArg "outputType" $"Run '{branch.RunName}' does not declare setup type '{outputType}'."
        create (UMX.cast<setupDatabaseName, databaseName> branch.SetupDatabaseName) branch.ParentProjectId branch.ProjectName outputType branch.SetupQueryParameterKeys[outputType] parameters

    /// Look up upstream results for the same data type, using the source project's query metadata.
    let forSource (branch: branchRun) dataType parameters =
        let source = Map.tryFind dataType branch.ResolvedSources |> Option.defaultWith (fun () -> invalidArg "dataType" "Resolve the run's source projects before querying upstream results.")
        create (UMX.cast<resultsDatabaseName, databaseName> source.Results.DatabaseName) source.ProjectId source.ProjectName dataType source.Results.QueryParameterKeys parameters
