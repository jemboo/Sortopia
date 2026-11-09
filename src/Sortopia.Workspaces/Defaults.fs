namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs

/// None inherits; Some (including an empty list or description) is a local value.
type rootDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ExecuteFunctionName: string<executeFunctionName> option
    OutputDataTypes: outputDataType list option
    OutputQueryParameterKeys: string<paramKeyName> list option
    OutputParameterDefaults: parameterDefault list option
}

/// None inherits; Some (including an empty list or description) is a local value.
type branchDefaultOverrides = {
    Description: string option
    ResultsDatabaseName: string<resultsDatabaseName> option
    ExecuteFunctionName: string<executeFunctionName> option
    OutputDataTypes: outputDataType list option
    OutputQueryParameterKeys: string<paramKeyName> list option
    OutputParameterDefaults: parameterDefault list option
    SetupDatabaseName: string<setupDatabaseName> option
    SetupFunctionName: string<setupFunctionName> option
    SetupOutputDataTypes: outputDataType list option
    SetupQueryParameterKeys: string<paramKeyName> list option
    SetupParameterDefaults: parameterDefault list option
}

type configurationOverrides =
    | RootOverrides of rootDefaultOverrides
    | BranchOverrides of branchDefaultOverrides

module Defaults =
    // Metadata defaults apply when no module or project supplies a local value.
    let root : rootProjectConfig = {
        Description = ""
        ResultsDatabaseName = UMX.tag "Results"
        ExecuteFunctionName = UMX.tag ""
        OutputDataTypes = []
        OutputQueryParameterKeys = []
        OutputParameterDefaults = [] }
    let inheritRoot : rootDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ExecuteFunctionName = None
        OutputDataTypes = None
        OutputQueryParameterKeys = None
        OutputParameterDefaults = None }
    let localRoot (value: rootProjectConfig) : rootDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ExecuteFunctionName = Some value.ExecuteFunctionName
        OutputDataTypes = Some value.OutputDataTypes
        OutputQueryParameterKeys = Some value.OutputQueryParameterKeys
        OutputParameterDefaults = Some value.OutputParameterDefaults }
    let resolveRoot (parent: rootProjectConfig) (local: rootDefaultOverrides) : rootProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        ResultsDatabaseName = local.ResultsDatabaseName |> Option.defaultValue parent.ResultsDatabaseName
        ExecuteFunctionName = local.ExecuteFunctionName |> Option.defaultValue parent.ExecuteFunctionName
        OutputDataTypes = local.OutputDataTypes |> Option.defaultValue parent.OutputDataTypes
        OutputQueryParameterKeys = local.OutputQueryParameterKeys |> Option.defaultValue parent.OutputQueryParameterKeys
        OutputParameterDefaults = local.OutputParameterDefaults |> Option.defaultValue parent.OutputParameterDefaults }
    let validateRoot local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { root with ExecuteFunctionName = UMX.tag "unspecified" }
        resolveRoot baseline local |> ProjectConfiguration.validateRoot
    let internal validateRootModule (value: rootProjectConfig) =
        { value with ExecuteFunctionName = if value.ExecuteFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.ExecuteFunctionName }
        |> ProjectConfiguration.validateRoot
    let branch : branchProjectConfig = {
        Description = ""
        ResultsDatabaseName = UMX.tag "Results"
        ExecuteFunctionName = UMX.tag ""
        OutputDataTypes = []
        OutputQueryParameterKeys = []
        OutputParameterDefaults = []
        SetupDatabaseName = UMX.tag "Setup"
        SetupFunctionName = UMX.tag ""
        SetupOutputDataTypes = []
        SetupQueryParameterKeys = []
        SetupParameterDefaults = [] }
    let inheritBranch : branchDefaultOverrides = {
        Description = None
        ResultsDatabaseName = None
        ExecuteFunctionName = None
        OutputDataTypes = None
        OutputQueryParameterKeys = None
        OutputParameterDefaults = None
        SetupDatabaseName = None
        SetupFunctionName = None
        SetupOutputDataTypes = None
        SetupQueryParameterKeys = None
        SetupParameterDefaults = None }
    let localBranch (value: branchProjectConfig) : branchDefaultOverrides = {
        Description = Some value.Description
        ResultsDatabaseName = Some value.ResultsDatabaseName
        ExecuteFunctionName = Some value.ExecuteFunctionName
        OutputDataTypes = Some value.OutputDataTypes
        OutputQueryParameterKeys = Some value.OutputQueryParameterKeys
        OutputParameterDefaults = Some value.OutputParameterDefaults
        SetupDatabaseName = Some value.SetupDatabaseName
        SetupFunctionName = Some value.SetupFunctionName
        SetupOutputDataTypes = Some value.SetupOutputDataTypes
        SetupQueryParameterKeys = Some value.SetupQueryParameterKeys
        SetupParameterDefaults = Some value.SetupParameterDefaults }
    let resolveBranch (parent: branchProjectConfig) (local: branchDefaultOverrides) : branchProjectConfig = {
        Description = local.Description |> Option.defaultValue parent.Description
        ResultsDatabaseName = local.ResultsDatabaseName |> Option.defaultValue parent.ResultsDatabaseName
        ExecuteFunctionName = local.ExecuteFunctionName |> Option.defaultValue parent.ExecuteFunctionName
        OutputDataTypes = local.OutputDataTypes |> Option.defaultValue parent.OutputDataTypes
        OutputQueryParameterKeys = local.OutputQueryParameterKeys |> Option.defaultValue parent.OutputQueryParameterKeys
        OutputParameterDefaults = local.OutputParameterDefaults |> Option.defaultValue parent.OutputParameterDefaults
        SetupDatabaseName = local.SetupDatabaseName |> Option.defaultValue parent.SetupDatabaseName
        SetupFunctionName = local.SetupFunctionName |> Option.defaultValue parent.SetupFunctionName
        SetupOutputDataTypes = local.SetupOutputDataTypes |> Option.defaultValue parent.SetupOutputDataTypes
        SetupQueryParameterKeys = local.SetupQueryParameterKeys |> Option.defaultValue parent.SetupQueryParameterKeys
        SetupParameterDefaults = local.SetupParameterDefaults |> Option.defaultValue parent.SetupParameterDefaults }
    let validateBranch local =
        // Validate supplied values without requiring an organizational module to supply functions.
        let baseline = { branch with ExecuteFunctionName = UMX.tag "unspecified"; SetupFunctionName = UMX.tag "unspecified" }
        resolveBranch baseline local |> ProjectConfiguration.validateBranch
    let internal validateBranchModule (value: branchProjectConfig) =
        { value with ExecuteFunctionName = if value.ExecuteFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.ExecuteFunctionName
                     SetupFunctionName = if value.SetupFunctionName = UMX.tag "" then UMX.tag "unspecified" else value.SetupFunctionName }
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
