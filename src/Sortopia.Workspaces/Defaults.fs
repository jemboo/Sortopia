namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type rootDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsFunctionName: string<resultsFunctionName> option
    ResultsSortopiaDataTypes: sortopiaDataType list option
    ResultsQueryParameterKeys: string<paramKeyName> list option
    ResultsParameterDefaults: parameterBinding list option
}

/// None inherits. Parameter defaults merge by key; other lists and descriptions replace inherited values.
type branchDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ResultsFunctionName: string<resultsFunctionName> option
    ResultsSortopiaDataTypes: sortopiaDataType list option
    ResultsQueryParameterKeys: string<paramKeyName> list option
    ResultsParameterDefaults: parameterBinding list option
    SetupDatabaseName: string<setupDatabaseName> option
    SetupFunctionName: string<setupFunctionName> option
    SetupDataTypes: setupDataTypeConfig list option
    SetupParameterDefaults: parameterBinding list option
}

type configurationOverrides =
    | RootOverrides of rootDefaultOverrides
    | BranchOverrides of branchDefaultOverrides

module Defaults =
    /// Local bindings replace matching keys; unrelated inherited bindings remain in order.
    let mergeParameterDefaults (inherited: parameterBinding list) (local: parameterBinding list) =
        ParameterBinding.validateUnique inherited
        ParameterBinding.validateUnique local
        let replacements = local |> List.map (fun binding -> binding.KeyName, binding) |> Map.ofList
        let inheritedKeys = inherited |> List.map (fun binding -> binding.KeyName) |> Set.ofList
        let merged = inherited |> List.map (fun binding -> Map.tryFind binding.KeyName replacements |> Option.defaultValue binding)
        merged @ (local |> List.filter (fun binding -> not (Set.contains binding.KeyName inheritedKeys)))

    // Database names start on each project; the remaining defaults may come from modules.
    let root : rootProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            SortopiaDataTypes = []
            QueryParameterKeys = []
            ParameterDefaults = [] } }
    let inheritRoot : rootDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsFunctionName = None
        ResultsSortopiaDataTypes = None
        ResultsQueryParameterKeys = None
        ResultsParameterDefaults = None }
    let localRoot (value: rootProjectConfig) : rootDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsFunctionName = Some value.ResultsFunctionName
        ResultsSortopiaDataTypes = Some value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = Some value.ResultsQueryParameterKeys
        ResultsParameterDefaults = Some value.ResultsParameterDefaults }
    let resolveRoot (parent: rootProjectConfig) (local: rootDefaultOverrides) : rootProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = local.ResultsFunctionName |> Option.defaultValue parent.ResultsFunctionName
            SortopiaDataTypes = local.ResultsSortopiaDataTypes |> Option.defaultValue parent.ResultsSortopiaDataTypes
            QueryParameterKeys = local.ResultsQueryParameterKeys |> Option.defaultValue parent.ResultsQueryParameterKeys
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) } }
    let validateRoot local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { root with Results = { root.Results with FunctionName = UMX.tag "unspecified" } }
        resolveRoot baseline local |> ProjectConfiguration.validateRoot
    let internal validateRootModule (value: rootProjectConfig) =
        { value with Results = { value.Results with FunctionName = if value.ResultsFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.ResultsFunctionName } }
        |> ProjectConfiguration.validateRoot
    let branch : branchProjectConfig = {
        Description = ""
        Results = {
            DatabaseName = UMX.tag "Results"
            FunctionName = UMX.tag ""
            SortopiaDataTypes = []
            QueryParameterKeys = []
            ParameterDefaults = [] }
        Setup = {
            DatabaseName = UMX.tag "Setup"
            FunctionName = UMX.tag ""
            DataTypes = []
            ParameterDefaults = [] } }
    let inheritBranch : branchDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ResultsFunctionName = None
        ResultsSortopiaDataTypes = None
        ResultsQueryParameterKeys = None
        ResultsParameterDefaults = None
        SetupDatabaseName = None
        SetupFunctionName = None
        SetupDataTypes = None
        SetupParameterDefaults = None }
    let localBranch (value: branchProjectConfig) : branchDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ResultsFunctionName = Some value.ResultsFunctionName
        ResultsSortopiaDataTypes = Some value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = Some value.ResultsQueryParameterKeys
        ResultsParameterDefaults = Some value.ResultsParameterDefaults
        SetupDatabaseName = Some value.SetupDatabaseName
        SetupFunctionName = Some value.SetupFunctionName
        SetupDataTypes = Some value.SetupDataTypes
        SetupParameterDefaults = Some value.SetupParameterDefaults }
    let resolveBranch (parent: branchProjectConfig) (local: branchDefaultOverrides) : branchProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        Results = {
            DatabaseName = local.ResultsDatabaseName |> Option.defaultValue (UMX.tag "Results")
            FunctionName = local.ResultsFunctionName |> Option.defaultValue parent.ResultsFunctionName
            SortopiaDataTypes = local.ResultsSortopiaDataTypes |> Option.defaultValue parent.ResultsSortopiaDataTypes
            QueryParameterKeys = local.ResultsQueryParameterKeys |> Option.defaultValue parent.ResultsQueryParameterKeys
            ParameterDefaults = mergeParameterDefaults parent.ResultsParameterDefaults (local.ResultsParameterDefaults |> Option.defaultValue []) }
        Setup = {
            DatabaseName = local.SetupDatabaseName |> Option.defaultValue (UMX.tag "Setup")
            FunctionName = local.SetupFunctionName |> Option.defaultValue parent.SetupFunctionName
            DataTypes = local.SetupDataTypes |> Option.defaultValue parent.SetupDataTypes
            ParameterDefaults = mergeParameterDefaults parent.SetupParameterDefaults (local.SetupParameterDefaults |> Option.defaultValue []) } }
    let validateBranch local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { branch with Results = { branch.Results with FunctionName = UMX.tag "unspecified" }; Setup = { branch.Setup with FunctionName = UMX.tag "unspecified" } }
        resolveBranch baseline local |> ProjectConfiguration.validateBranch
    let internal validateBranchModule (value: branchProjectConfig) =
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
