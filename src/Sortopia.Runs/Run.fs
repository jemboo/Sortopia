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


type resultsDataTypeConfig = { DataType: sortopiaDataType; QueryParameterKeys: string<paramKeyName> list }

type resultsConfig = {
    DatabaseName: string<resultsDatabaseName>
    FunctionName: string<resultsFunctionName>
    FunctionVersion: string
    Outputs: resultsDataTypeConfig list
    ParameterDefaults: parameterBinding list
}
with
    member this.DataTypes = this.Outputs |> List.map (fun entry -> entry.DataType, entry.QueryParameterKeys) |> Map.ofList
    member this.SortopiaDataTypes = this.Outputs |> List.map (fun entry -> entry.DataType)
    member this.QueryParameterKeys = this.Outputs |> List.collect (fun entry -> entry.QueryParameterKeys) |> List.distinct

/// Editable source assignment; output contracts belong to the selected function.
type setupSourceConfig = { DataType: sortopiaDataType; SourceProjectId: Guid<projectId> }

type setupDataTypeConfig = {
    DataType: sortopiaDataType
    SourceProjectId: Guid<projectId>
    QueryParameterKeys: string<paramKeyName> list
}

type setupConfig =
    {
        DatabaseName: string<setupDatabaseName>
        FunctionName: string<setupFunctionName>
        FunctionVersion: string
        Outputs: setupDataTypeConfig list
        ParameterDefaults: parameterBinding list
    }
    member this.DataTypes = this.Outputs |> List.map (fun entry -> entry.DataType, entry.QueryParameterKeys) |> Map.ofList
    member this.SortopiaDataTypes = this.Outputs |> List.map (fun entry -> entry.DataType)
    member this.SourceProjectIds = this.Outputs |> List.map (fun entry -> entry.DataType, entry.SourceProjectId) |> Map.ofList
    member this.QueryParameterKeys = this.DataTypes

/// Results are always present. A setup block identifies a branch, including unfinished drafts.
type projectConfiguration = {
    Description: string
    Results: resultsConfig
    Setup: setupConfig option
} with
    member this.HasSetup = this.Setup.IsSome
    member this.ResultsDatabaseName = this.Results.DatabaseName
    member this.ResultsFunctionName = this.Results.FunctionName
    member this.ResultsSortopiaDataTypes = this.Results.SortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.Results.QueryParameterKeys
    member this.ResultsDataTypes = this.Results.Outputs
    member this.ResultsParameterDefaults = this.Results.ParameterDefaults

/// Only the results contract is needed when reading another project's outputs.
type sourceProjectSnapshot = {
    ProjectId: Guid<projectId>
    ProjectName: string<projectName>
    Results: resultsConfig
}

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
    let validateResults (value: resultsConfig) =
        if isNull (box value) then nullArg "results"
        name "resultsDatabaseName" (UMX.untag value.DatabaseName)
        name "resultsFunctionName" (UMX.untag value.FunctionName)
        name "resultsFunctionVersion" value.FunctionVersion
        outputs "resultsSortopiaDataTypes" value.SortopiaDataTypes
        for entry in value.Outputs do keys "resultsQueryParameterKeys" entry.QueryParameterKeys
        ParameterBinding.validateUnique value.ParameterDefaults
        let conflict = ParameterSets.overlapError (value.ParameterDefaults |> List.map _.KeyName) value.QueryParameterKeys
        if conflict <> "" then invalidArg "resultsQueryParameterKeys" ("Results query keys require spans. " + conflict)
    let validateSetup (value: setupConfig) =
        if isNull (box value) then nullArg "setup"
        if isNull (box value.Outputs) then nullArg "setupDataTypes"
        for entry in value.Outputs do
            if isNull (box entry) then nullArg "setupDataType"
        name "setupDatabaseName" (UMX.untag value.DatabaseName)
        name "setupFunctionName" (UMX.untag value.FunctionName)
        name "setupFunctionVersion" value.FunctionVersion
        outputs "setupSortopiaDataTypes" value.SortopiaDataTypes
        for entry in value.Outputs do keys "setupQueryParameterKeys" entry.QueryParameterKeys
        ParameterBinding.validateUnique value.ParameterDefaults
        let conflict = ParameterSets.overlapError (value.ParameterDefaults |> List.map _.KeyName) (value.Outputs |> List.collect _.QueryParameterKeys)
        if conflict <> "" then invalidArg "setupQueryParameterKeys" ("Setup query keys require spans. " + conflict)
    let validate (value: projectConfiguration) =
        if isNull (box value) then nullArg "configuration"
        if isNull value.Description then nullArg "description"
        validateResults value.Results
        value.Setup |> Option.iter validateSetup

/// Immutable execution settings, independent of the workspace hierarchy.
type projectSnapshot = private {
    id: Guid<projectId>
    projectName: string<projectName>
    configuration: projectConfiguration
} with
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.Configuration = this.configuration
    member this.ResultsSnapshot = { ProjectId = this.Id; ProjectName = this.ProjectName; Results = this.Configuration.Results }

module ProjectSnapshot =
    let create (id: Guid<projectId>) (projectName: string<projectName>) configuration =
        if UMX.untag id = System.Guid.Empty then invalidArg "id" "Project ID must not be empty."
        if System.String.IsNullOrWhiteSpace (UMX.untag projectName) then invalidArg "projectName" "Project name must not be blank."
        ProjectConfiguration.validate configuration
        { id = id; projectName = projectName; configuration = configuration }

module private RunConfiguration =
    let identity (id: Guid<runId>) (projectName: string<projectName>) (runName: string<runName>) =
        if UMX.untag id = System.Guid.Empty then invalidArg "id" "Run ID must not be empty."
        for argument, value in ["projectName", UMX.untag projectName; "runName", UMX.untag runName] do
            if System.String.IsNullOrWhiteSpace value then invalidArg argument "Name must not be null, empty, or whitespace."
    let spans queryKeys defaults (spans: parameterSpan list) =
        ParameterSets.validateSpans defaults spans
        ParameterSets.validateQuerySpans queryKeys spans
    let sourceParameters queryKeys (defaults: parameterBinding list) (spans: parameterSpan list) =
        let names = Set.ofSeq (seq {
            for span in spans do yield span.KeyName
            for value in defaults do yield value.KeyName })
        for key in queryKeys do
            if not (Set.contains key names) then invalidArg "setupParameters" $"Source query key '{key}' has no default value or matching parameter span."

/// None means no setup phase; Some [] is a setup phase with only fixed inputs.
type runConfig = {
    Id: Guid<runId>
    RunName: string<runName>
    ParentProjectId: Guid<projectId>
    DescriptionOverride: string option
    ResultsParameterSpans: parameterSpan list
    SetupParameterSpans: parameterSpan list option
    Reporting: sgdReportingConfig option
    Sgd: sgdRunConfig option
}

type run = private {
    parent: projectSnapshot
    config: runConfig
    sources: Map<sortopiaDataType, sourceProjectSnapshot>
} with
    member this.Parent = this.parent
    member this.ProjectName = this.parent.ProjectName
    member this.ProjectConfiguration = this.parent.Configuration
    member this.Configuration = this.config
    member this.RunName = this.config.RunName
    member this.Id = this.config.Id
    member this.ParentProjectId = this.config.ParentProjectId
    member this.DescriptionOverride = this.config.DescriptionOverride
    member this.Description = this.config.DescriptionOverride |> Option.defaultValue this.ProjectConfiguration.Description
    member this.Reporting = this.config.Reporting
    member this.Sgd = this.config.Sgd
    member this.ResultsDatabaseName = this.ProjectConfiguration.Results.DatabaseName
    member this.ResultsFunctionName = this.ProjectConfiguration.Results.FunctionName
    member this.ResultsParameterSpans = this.config.ResultsParameterSpans
    member this.ResultsSortopiaDataTypes = this.ProjectConfiguration.Results.SortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.ProjectConfiguration.Results.QueryParameterKeys
    member this.ResultsDataTypes = this.ProjectConfiguration.Results.Outputs
    member this.ResultsParameterDefaults = this.ProjectConfiguration.Results.ParameterDefaults
    member this.Setup = this.ProjectConfiguration.Setup
    member this.HasSetup = this.Setup.IsSome
    member this.SetupDatabaseName = this.Setup |> Option.map _.DatabaseName
    member this.SetupFunctionName = this.Setup |> Option.map _.FunctionName
    member this.SetupParameterSpans = this.config.SetupParameterSpans
    member this.SetupSortopiaDataTypes = this.Setup |> Option.map _.SortopiaDataTypes
    member this.SetupParameterDefaults = this.Setup |> Option.map _.ParameterDefaults
    member this.SetupQueryParameterKeys = this.Setup |> Option.map _.QueryParameterKeys
    member this.SetupDataTypes = this.Setup |> Option.map _.Outputs |> Option.defaultValue []
    member this.SourceProjectIds = this.Setup |> Option.map _.SourceProjectIds |> Option.defaultValue Map.empty
    member this.ResolvedSources = this.sources

module Run =
    let create (parent: projectSnapshot) (config: runConfig) : run =
        if config.ParentProjectId <> parent.Id then invalidArg "parentProjectId" "The run must reference its containing project by ID."
        let settings = parent.Configuration
        RunConfiguration.identity config.Id parent.ProjectName config.RunName
        if config.DescriptionOverride |> Option.exists isNull then nullArg "description"
        config.Reporting |> Option.iter (SgdReporting.validate >> ignore)
        config.Sgd |> Option.iter (SgdRun.validate >> ignore)
        RunConfiguration.spans settings.Results.QueryParameterKeys settings.Results.ParameterDefaults config.ResultsParameterSpans
        match settings.Setup, config.SetupParameterSpans with
        | None, None -> ()
        | Some setup, Some spans -> RunConfiguration.spans (setup.Outputs |> List.collect _.QueryParameterKeys) setup.ParameterDefaults spans
        | _ -> invalidArg "setupParameterSpans" "Run setup inputs must match the containing project's setup phase."
        { parent = parent; config = config; sources = Map.empty }
    let withNames projectName runName (value: run) =
        if projectName <> value.ProjectName then invalidArg "projectName" "A run's parent project name is fixed."
        RunConfiguration.identity value.Id projectName runName
        { value with config = { value.config with RunName = runName } }
    let makeResultsParameterSets (value: run) = ParameterSets.create value.ResultsParameterDefaults value.ResultsParameterSpans
    let makeSetupParameterSets (value: run) =
        match value.Setup, value.SetupParameterSpans with
        | Some setup, Some spans -> ParameterSets.create setup.ParameterDefaults spans
        | _ -> invalidArg "run" "A root run has no setup phase."

module RunSources =
    let resolveFor isRequired (find: Guid<projectId> -> sourceProjectSnapshot option) (value: run) =
        match value.Setup, value.SetupParameterSpans with
        | None, None -> value
        | Some setup, Some spans ->
            let sources = setup.Outputs |> List.filter (fun entry -> isRequired entry.DataType && UMX.untag entry.SourceProjectId <> System.Guid.Empty) |> List.map (fun entry ->
                let source = find entry.SourceProjectId |> Option.defaultWith (fun () -> invalidArg "sourceProjectId" $"Source project '{entry.SourceProjectId}' was not found.")
                if not (List.contains entry.DataType source.Results.SortopiaDataTypes) then invalidArg "sourceProjectId" $"Source project '{source.ProjectName}' does not provide '{entry.DataType}'."
                RunConfiguration.sourceParameters source.Results.DataTypes[entry.DataType] setup.ParameterDefaults spans
                entry.DataType, source) |> Map.ofList
            { value with sources = sources }
        | _ -> invalidArg "run" "Run setup inputs must match its project."
    let resolve find value = resolveFor (fun _ -> true) find value
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
        create (UMX.cast<resultsDatabaseName, databaseName> run.ResultsDatabaseName) run.ParentProjectId run.ProjectName outputType (run.ResultsDataTypes |> List.find (fun entry -> entry.DataType = outputType)).QueryParameterKeys parameters

    /// Builds a setup query using the keys configured for the requested output type.
    let forSetup (branch: run) outputType parameters =
        let setup = branch.Setup |> Option.defaultWith (fun () -> invalidArg "run" "A root run has no setup phase.")
        if not (List.contains outputType setup.SortopiaDataTypes) then
            invalidArg "outputType" $"Run '{branch.RunName}' does not declare setup type '{outputType}'."
        create (UMX.cast<setupDatabaseName, databaseName> setup.DatabaseName) branch.ParentProjectId branch.ProjectName outputType setup.QueryParameterKeys[outputType] parameters

    /// Look up upstream results for the same data type, using the source project's query metadata.
    let forSource (branch: run) dataType parameters =
        let source = Map.tryFind dataType branch.ResolvedSources |> Option.defaultWith (fun () -> invalidArg "dataType" "Resolve the run's source projects before querying upstream results.")
        create (UMX.cast<resultsDatabaseName, databaseName> source.Results.DatabaseName) source.ProjectId source.ProjectName dataType source.Results.DataTypes[dataType] parameters
