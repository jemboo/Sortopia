namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type rootDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsSortopiaDataTypes: sortopiaDataType list option
    ResultsQueryParameterKeys: string<paramKeyName> list option
    ResultsParameterDefaults: parameterBinding list option
}

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type branchDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsSortopiaDataTypes: sortopiaDataType list option
    ResultsQueryParameterKeys: string<paramKeyName> list option
    ResultsParameterDefaults: parameterBinding list option
    SetupDatabaseName: string<setupDatabaseName> option
    SetupDataTypes: setupDataTypeConfig list option
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

    let resultOutputs name types keys =
        FunctionCatalogs.resultsOutputs name |> Option.defaultWith (fun () ->
            types |> List.map (fun dataType -> { DataType = dataType; QueryParameterKeys = keys } : resultsDataTypeConfig))
    let setupOutputs name (entries: setupDataTypeConfig list) =
        FunctionCatalogs.setupOutputs name |> Option.map (List.map (fun (dataType, keys) ->
            let source = if FunctionConfiguration.requiresSource name dataType then entries |> List.tryFind (fun entry -> entry.DataType = dataType) |> Option.map (fun entry -> entry.SourceProjectId) |> Option.defaultValue (UMX.tag System.Guid.Empty) else UMX.tag System.Guid.Empty
            { DataType = dataType; SourceProjectId = source; QueryParameterKeys = keys })) |> Option.defaultValue entries

    // Database names start on each project; the remaining defaults may come from modules.
    let root : rootProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            Outputs = []
            ParameterDefaults = [] } }
    let inheritRoot : rootDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsSortopiaDataTypes = None
        ResultsQueryParameterKeys = None
        ResultsParameterDefaults = None }
    let localRoot (value: rootProjectConfig) : rootDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsSortopiaDataTypes = Some value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = Some value.ResultsQueryParameterKeys
        ResultsParameterDefaults = Some value.ResultsParameterDefaults }
    let resolveRoot (parent: rootProjectConfig) (local: rootDefaultOverrides) : rootProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = parent.ResultsFunctionName
            Outputs = resultOutputs parent.ResultsFunctionName (local.ResultsSortopiaDataTypes |> Option.defaultValue parent.ResultsSortopiaDataTypes) (local.ResultsQueryParameterKeys |> Option.defaultValue parent.ResultsQueryParameterKeys)
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) } }
    let validateRoot local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { root with Results = { root.Results with FunctionName = UMX.tag "unspecified" } }
        resolveRoot baseline local |> ProjectConfiguration.validateRoot
    let internal validateRootModule (value: rootProjectConfig) =
        FunctionConfiguration.validateResults value.Results
        validateParameters (resultsRequirements value.ResultsFunctionName) value.ResultsParameterDefaults value.ResultsQueryParameterKeys
        { value with Results = { value.Results with FunctionName = if value.ResultsFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.ResultsFunctionName } }
        |> ProjectConfiguration.validateRoot
    let branch : branchProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            Outputs = []
            ParameterDefaults = [] }
        Setup = {
            DatabaseName = UMX.tag "Setup"
            FunctionName = UMX.tag ""
            Outputs = []
            ParameterDefaults = [] } }
    let inheritBranch : branchDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsSortopiaDataTypes = None
        ResultsQueryParameterKeys = None
        ResultsParameterDefaults = None
        SetupDatabaseName = None
        SetupDataTypes = None
        SetupParameterDefaults = None }
    let localBranch (value: branchProjectConfig) : branchDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsSortopiaDataTypes = Some value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = Some value.ResultsQueryParameterKeys
        ResultsParameterDefaults = Some value.ResultsParameterDefaults
        SetupDatabaseName = Some value.SetupDatabaseName
        SetupDataTypes = Some value.SetupDataTypes
        SetupParameterDefaults = Some value.SetupParameterDefaults }
    let resolveBranch (parent: branchProjectConfig) (local: branchDefaultOverrides) : branchProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = parent.ResultsFunctionName
            Outputs = resultOutputs parent.ResultsFunctionName (local.ResultsSortopiaDataTypes |> Option.defaultValue parent.ResultsSortopiaDataTypes) (local.ResultsQueryParameterKeys |> Option.defaultValue parent.ResultsQueryParameterKeys)
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) }
        Setup = {
            DatabaseName = local.SetupDatabaseName |> Option.defaultValue (UMX.tag "Setup")
            FunctionName = parent.SetupFunctionName
            Outputs = setupOutputs parent.SetupFunctionName (local.SetupDataTypes |> Option.defaultValue parent.SetupDataTypes)
            ParameterDefaults = mergeParameterDefaults parent.SetupParameterDefaults (local.SetupParameterDefaults |> Option.defaultValue []) } }
    let validateBranch local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { branch with Results = { branch.Results with FunctionName = UMX.tag "unspecified" }; Setup = { branch.Setup with FunctionName = UMX.tag "unspecified" } }
        resolveBranch baseline local |> ProjectConfiguration.validateBranch
    let internal validateBranchModule (value: branchProjectConfig) =
        FunctionConfiguration.validateResults value.Results
        FunctionConfiguration.validateSetup false value.Setup
        validateParameters (resultsRequirements value.ResultsFunctionName) value.ResultsParameterDefaults value.ResultsQueryParameterKeys
        validateParameters (setupRequirements value.SetupFunctionName) value.SetupParameterDefaults (value.SetupDataTypes |> List.collect (fun output -> output.QueryParameterKeys))
        { value with Results = { value.Results with FunctionName = if value.ResultsFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.ResultsFunctionName }; Setup = { value.Setup with FunctionName = if value.SetupFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.SetupFunctionName } }
        |> ProjectConfiguration.validateBranch
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
