namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// Project-owned setup settings and overrides for values inherited from modules.
type projectSetupSettings = {
    DatabaseName: string<setupDatabaseName>
    Sources: setupSourceConfig list option
    ParameterDefaults: parameterBinding list option
    ExcludedDefaults: string<paramKeyName> list
    Queries: resultsDataTypeConfig list
}

/// Defaults merge by key; query projections and database names belong to each project.
type projectLocalSettings = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName>
    ResultsParameterDefaults: parameterBinding list option
    ExcludedResultsDefaults: string<paramKeyName> list
    ResultsQueries: resultsDataTypeConfig list
    Setup: projectSetupSettings option
}
module ProjectResolution =
    open ConfigurationInheritance
    let private queries (outputs: resultsDataTypeConfig list) (configured: resultsDataTypeConfig list) =
        if (configured |> List.map _.DataType |> List.sort) <> (outputs |> List.map _.DataType |> List.sort) then
            invalidArg "queryParameterKeys" "Configure query keys for exactly the selected function's outputs."
        configured
    let resultOutputs (name: string<resultsFunctionName>) =
        if UMX.untag name = "" then [] else ResultsFunctionCatalog.outputs name |> List.map (fun output -> { output with QueryParameterKeys = [] })
    let setupOutputs (name: string<setupFunctionName>) (sources: setupSourceConfig list) : setupDataTypeConfig list =
        if UMX.untag name = "" then [] else
        SetupFunctionCatalog.outputs name |> List.map (fun (dataType, keys) ->
            let source = if FunctionConfiguration.requiresSource name dataType then sources |> List.tryFind (fun entry -> entry.DataType = dataType) |> Option.map (fun entry -> entry.SourceProjectId) |> Option.defaultValue (UMX.tag System.Guid.Empty) else UMX.tag System.Guid.Empty
            { DataType = dataType; SourceProjectId = source; QueryParameterKeys = [] })
    let resultsVersion (name: string<resultsFunctionName>) = if UMX.untag name = "" then "" else (ResultsFunctionCatalog.require name).Version
    let setupVersion (name: string<setupFunctionName>) = if UMX.untag name = "" then "" else (SetupFunctionCatalog.require name).Version
    let sources (entries: setupDataTypeConfig list) : setupSourceConfig list =
        entries |> List.filter (fun entry -> UMX.untag entry.SourceProjectId <> System.Guid.Empty)
        |> List.map (fun entry -> { DataType = entry.DataType; SourceProjectId = entry.SourceProjectId })

    // Database names start on each project; the remaining defaults may come from modules.
    let configuration hasSetup : projectConfiguration = {
        Description = ""
        Results = { DatabaseName = UMX.tag "Results"; FunctionName = UMX.tag ""; FunctionVersion = ""; Outputs = []; ParameterDefaults = [] }
        Setup = if hasSetup then Some { DatabaseName = UMX.tag "Setup"; FunctionName = UMX.tag ""; FunctionVersion = ""; Outputs = []; ParameterDefaults = [] } else None }
    /// Each new project starts with its own names and empty query projections.
    let initial (parent: projectConfiguration) : projectLocalSettings = {
        Description = None; ResultsDatabaseName = UMX.tag "Results"; ResultsParameterDefaults = None; ExcludedResultsDefaults = []
        ResultsQueries = resultOutputs parent.Results.FunctionName
        Setup = parent.Setup |> Option.map (fun setup -> {
            DatabaseName = UMX.tag "Setup"; Sources = None; ParameterDefaults = None; ExcludedDefaults = []
            Queries = setupOutputs setup.FunctionName [] |> List.map (fun output -> { DataType = output.DataType; QueryParameterKeys = [] }) }) }
    let local (value: projectConfiguration) : projectLocalSettings = {
        Description = Some value.Description; ResultsDatabaseName = value.Results.DatabaseName
        ResultsParameterDefaults = Some value.Results.ParameterDefaults; ExcludedResultsDefaults = []; ResultsQueries = value.Results.Outputs
        Setup = value.Setup |> Option.map (fun setup -> {
            DatabaseName = setup.DatabaseName; ParameterDefaults = Some setup.ParameterDefaults; ExcludedDefaults = []
            Sources = Some (setup.Outputs |> List.filter (fun entry -> FunctionConfiguration.requiresSource setup.FunctionName entry.DataType)
                            |> List.map (fun entry -> { DataType = entry.DataType; SourceProjectId = entry.SourceProjectId }))
            Queries = setup.Outputs |> List.map (fun entry -> { DataType = entry.DataType; QueryParameterKeys = entry.QueryParameterKeys }) }) }
    let private validateDatabase value =
        if System.String.IsNullOrWhiteSpace value then invalidArg "databaseName" "Database name must not be blank."
    let validate (local: projectLocalSettings) =
        local.Description |> Option.iter validateDescription
        local.ResultsDatabaseName |> UMX.untag |> validateDatabase
        validateDefaultOverrides "excludedResultsDefaults" local.ResultsParameterDefaults local.ExcludedResultsDefaults
        local.Setup |> Option.iter (fun setup ->
            setup.DatabaseName |> UMX.untag |> validateDatabase
            validateDefaultOverrides "excludedSetupDefaults" setup.ParameterDefaults setup.ExcludedDefaults
            setup.Sources |> Option.iter validateSources)
    let internal validateConfiguration (value: projectConfiguration) =
        validateDescription value.Description
        if UMX.untag value.Results.FunctionName <> "" then FunctionConfiguration.validateResults value.Results
        validateParameters (resultsRequirements value.Results.FunctionName) value.Results.ParameterDefaults value.Results.QueryParameterKeys
        ParameterBinding.validateUnique value.Results.ParameterDefaults
        value.Setup |> Option.iter (fun setup ->
            if UMX.untag setup.FunctionName <> "" then FunctionConfiguration.validateSetup false setup
            validateParameters (setupRequirements setup.FunctionName) setup.ParameterDefaults (setup.Outputs |> List.collect _.QueryParameterKeys)
            ParameterBinding.validateUnique setup.ParameterDefaults)
    let resolve (parent: projectConfiguration) (local: projectLocalSettings) : projectConfiguration =
        validate local
        let setup =
            match parent.Setup, local.Setup with
            | None, None -> None
            | Some parent, Some local ->
                let outputs = setupOutputs parent.FunctionName (mergeSources (sources parent.Outputs) (Option.defaultValue [] local.Sources))
                let selected = queries (outputs |> List.map (fun output -> { DataType = output.DataType; QueryParameterKeys = [] })) local.Queries
                Some {
                    DatabaseName = local.DatabaseName
                    FunctionName = parent.FunctionName; FunctionVersion = setupVersion parent.FunctionName
                    Outputs = outputs |> List.map (fun output -> { output with QueryParameterKeys = (selected |> List.find (fun entry -> entry.DataType = output.DataType)).QueryParameterKeys })
                    ParameterDefaults = resolveDefaults parent.ParameterDefaults local.ParameterDefaults local.ExcludedDefaults }
            | _ -> invalidArg "local" "Project settings must match the inherited setup phase."
        let value = {
            Description = local.Description |> Option.defaultValue parent.Description
            Results = {
                DatabaseName = local.ResultsDatabaseName
                FunctionName = parent.Results.FunctionName; FunctionVersion = resultsVersion parent.Results.FunctionName
                Outputs = queries (resultOutputs parent.Results.FunctionName) local.ResultsQueries
                ParameterDefaults = resolveDefaults parent.Results.ParameterDefaults local.ResultsParameterDefaults local.ExcludedResultsDefaults }
            Setup = setup }
        validateConfiguration value
        value
