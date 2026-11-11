namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type rootDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsParameterDefaults: parameterBinding list option
}

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type branchDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsParameterDefaults: parameterBinding list option
    SetupDatabaseName: string<setupDatabaseName> option
    SetupSources: setupSourceConfig list option
    SetupParameterDefaults: parameterBinding list option
}

type configurationOverrides =
    | RootOverrides of rootDefaultOverrides
    | BranchOverrides of branchDefaultOverrides

module Defaults =
    let private resultsRequirements name = FunctionCatalogs.resultsFunctionCatalog |> Map.tryFind name |> Option.map (fun value -> value.RequiredParameters)
    let private setupRequirements name = FunctionCatalogs.setupFunctionCatalog |> Map.tryFind name |> Option.map (fun value -> value.RequiredParameters)
    let private inheritedFor requirements values =
        match requirements with
        | None -> values
        | Some keys -> values |> List.filter (fun (value: parameterBinding) -> keys |> List.exists (fun key -> key.Name = value.KeyName))
    let internal resultsDefaultsFor name values = inheritedFor (resultsRequirements name) values
    let internal setupDefaultsFor name values = inheritedFor (setupRequirements name) values
    let private validateParameters requirements defaults queryKeys =
        match requirements with
        | None -> ()
        | Some keys ->
            for binding: parameterBinding in defaults do
                match keys |> List.tryFind (fun key -> key.Name = binding.KeyName) with
                | None -> invalidArg "parameterDefaults" $"Parameter '{binding.KeyName}' is not required by the selected function. Remove this default or choose a compatible module function."
                | Some key when key.ValueType <> binding.ValueType -> invalidArg "parameterDefaults" $"Parameter '{binding.KeyName}' must have type {ParameterValue.label key.ValueType}."
                | _ -> ()
            for name in queryKeys do
                if not (keys |> List.exists (fun key -> key.Name = name)) then
                    invalidArg "queryParameterKeys" $"Query key '{name}' is not a required input of the selected function."
    /// Local bindings replace matching keys; unrelated inherited bindings remain in order.
    let mergeParameterDefaults (inherited: parameterBinding list) (local: parameterBinding list) =
        ParameterBinding.validateUnique inherited
        ParameterBinding.validateUnique local
        let replacements = local |> List.map (fun binding -> binding.KeyName, binding) |> Map.ofList
        let inheritedKeys = inherited |> List.map (fun binding -> binding.KeyName) |> Set.ofList
        let merged = inherited |> List.map (fun binding -> Map.tryFind binding.KeyName replacements |> Option.defaultValue binding)
        merged @ (local |> List.filter (fun binding -> not (Set.contains binding.KeyName inheritedKeys)))

    let resultOutputs (name: string<resultsFunctionName>) =
        if UMX.untag name = "" then [] else FunctionCatalogs.resultsOutputs name
    let setupOutputs (name: string<setupFunctionName>) (sources: setupSourceConfig list) : setupDataTypeConfig list =
        if UMX.untag name = "" then [] else
        FunctionCatalogs.setupOutputs name |> List.map (fun (dataType, keys) ->
            let source = if FunctionConfiguration.requiresSource name dataType then sources |> List.tryFind (fun entry -> entry.DataType = dataType) |> Option.map (fun entry -> entry.SourceProjectId) |> Option.defaultValue (UMX.tag System.Guid.Empty) else UMX.tag System.Guid.Empty
            { DataType = dataType; SourceProjectId = source; QueryParameterKeys = keys })
    let resultsVersion (name: string<resultsFunctionName>) = if UMX.untag name = "" then "" else (FunctionCatalogs.requireResults name).Version
    let setupVersion (name: string<setupFunctionName>) = if UMX.untag name = "" then "" else (FunctionCatalogs.requireSetup name).Version
    let sources (entries: setupDataTypeConfig list) : setupSourceConfig list =
        entries |> List.filter (fun entry -> UMX.untag entry.SourceProjectId <> System.Guid.Empty)
        |> List.map (fun entry -> { DataType = entry.DataType; SourceProjectId = entry.SourceProjectId })

    // Database names start on each project; the remaining defaults may come from modules.
    let root : rootProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            FunctionVersion = ""
            Outputs = []
            ParameterDefaults = [] } }
    let inheritRoot : rootDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsParameterDefaults = None }
    let localRoot (value: rootProjectConfig) : rootDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsParameterDefaults = Some value.ResultsParameterDefaults }
    let resolveRoot (parent: rootProjectConfig) (local: rootDefaultOverrides) : rootProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = parent.ResultsFunctionName
            FunctionVersion = resultsVersion parent.ResultsFunctionName
            Outputs = resultOutputs parent.ResultsFunctionName
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) } }
    let validateRoot (local: rootDefaultOverrides) =
        local.Description |> Option.iter (fun value -> if isNull value then nullArg "description")
        local.ResultsDatabaseName |> Option.iter (fun value -> if System.String.IsNullOrWhiteSpace (UMX.untag value) then invalidArg "databaseName" "Database name must not be blank.")
        local.ResultsParameterDefaults |> Option.iter ParameterBinding.validateUnique
    let internal validateRootModule (value: rootProjectConfig) =
        if UMX.untag value.ResultsFunctionName <> "" then FunctionConfiguration.validateResults value.Results
        validateParameters (resultsRequirements value.ResultsFunctionName) value.ResultsParameterDefaults value.ResultsQueryParameterKeys
        if isNull value.Description then nullArg "description"
        ParameterBinding.validateUnique value.ResultsParameterDefaults
    let branch : branchProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            FunctionVersion = ""
            Outputs = []
            ParameterDefaults = [] }
        Setup = {
            DatabaseName = UMX.tag "Setup"
            FunctionName = UMX.tag ""
            FunctionVersion = ""
            Outputs = []
            ParameterDefaults = [] } }
    let inheritBranch : branchDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsParameterDefaults = None
        SetupDatabaseName = None
        SetupSources = None
        SetupParameterDefaults = None }
    let localBranch (value: branchProjectConfig) : branchDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsParameterDefaults = Some value.ResultsParameterDefaults
        SetupDatabaseName = Some value.SetupDatabaseName
        SetupSources = Some (sources value.SetupDataTypes)
        SetupParameterDefaults = Some value.SetupParameterDefaults }
    let resolveBranch (parent: branchProjectConfig) (local: branchDefaultOverrides) : branchProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = parent.ResultsFunctionName
            FunctionVersion = resultsVersion parent.ResultsFunctionName
            Outputs = resultOutputs parent.ResultsFunctionName
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) }
        Setup = {
            DatabaseName = local.SetupDatabaseName |> Option.defaultValue (UMX.tag "Setup")
            FunctionName = parent.SetupFunctionName
            FunctionVersion = setupVersion parent.SetupFunctionName
            Outputs = setupOutputs parent.SetupFunctionName (local.SetupSources |> Option.defaultValue (sources parent.SetupDataTypes))
            ParameterDefaults = mergeParameterDefaults parent.SetupParameterDefaults (local.SetupParameterDefaults |> Option.defaultValue []) } }
    let validateBranch (local: branchDefaultOverrides) =
        validateRoot { Description = local.Description; ResultsDatabaseName = local.ResultsDatabaseName; ResultsParameterDefaults = local.ResultsParameterDefaults }
        local.SetupDatabaseName |> Option.iter (fun value -> if System.String.IsNullOrWhiteSpace (UMX.untag value) then invalidArg "databaseName" "Database name must not be blank.")
        local.SetupParameterDefaults |> Option.iter ParameterBinding.validateUnique
        local.SetupSources |> Option.iter (fun sources ->
            if (sources |> List.map (fun entry -> entry.DataType) |> List.distinct).Length <> sources.Length then invalidArg "setupSources" "Choose each source data type only once."
            for entry in sources do SortopiaDataType.validate entry.DataType)
    let internal validateBranchModule (value: branchProjectConfig) =
        validateRootModule { Description = value.Description; Results = value.Results }
        if UMX.untag value.SetupFunctionName <> "" then FunctionConfiguration.validateSetup false value.Setup
        validateParameters (setupRequirements value.SetupFunctionName) value.SetupParameterDefaults (value.SetupDataTypes |> List.collect (fun output -> output.QueryParameterKeys))
        ParameterBinding.validateUnique value.SetupParameterDefaults
    let configuration kindIsBranch = if kindIsBranch then BranchProjectConfig branch else RootProjectConfig root
    let inherited kindIsBranch = if kindIsBranch then BranchOverrides inheritBranch else RootOverrides inheritRoot
    let resolve parent local =
        match parent, local with
        | RootProjectConfig p, RootOverrides l ->
            validateRoot l
            let value = resolveRoot p l
            validateRootModule value
            RootProjectConfig value
        | BranchProjectConfig p, BranchOverrides l ->
            validateBranch l
            let value = resolveBranch p l
            validateBranchModule value
            BranchProjectConfig value
        | _ -> invalidArg "local" "Inherited defaults and local overrides must have the same root/branch type."
