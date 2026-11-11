namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs

// Module defaults deliberately contain no database identity.
type rootModuleConfig = {
    Description: string
    ResultsFunctionName: string<resultsFunctionName>
    ResultsSortopiaDataTypes: sortopiaDataType list
    ResultsQueryParameterKeys: string<paramKeyName> list
    ResultsParameterDefaults: parameterBinding list
}
type rootModuleOverrides = {
    Description: string option
    ResultsFunctionName: string<resultsFunctionName> option
    ResultsParameterDefaults: parameterBinding list option
}

type branchModuleConfig = {
    Description: string
    ResultsFunctionName: string<resultsFunctionName>
    ResultsSortopiaDataTypes: sortopiaDataType list
    ResultsQueryParameterKeys: string<paramKeyName> list
    ResultsParameterDefaults: parameterBinding list
    SetupFunctionName: string<setupFunctionName>
    SetupDataTypes: setupDataTypeConfig list
    SetupParameterDefaults: parameterBinding list
} with
    member this.SetupSortopiaDataTypes = this.SetupDataTypes |> List.map (fun entry -> entry.DataType)
    member this.SourceProjectIds = this.SetupDataTypes |> List.map (fun entry -> entry.DataType, entry.SourceProjectId) |> Map.ofList
    member this.SetupQueryParameterKeys = this.SetupDataTypes |> List.map (fun entry -> entry.DataType, entry.QueryParameterKeys) |> Map.ofList
type branchModuleOverrides = {
    Description: string option
    ResultsFunctionName: string<resultsFunctionName> option
    ResultsParameterDefaults: parameterBinding list option
    SetupFunctionName: string<setupFunctionName> option
    SetupSources: setupSourceConfig list option
    SetupParameterDefaults: parameterBinding list option
}

type moduleConfiguration = RootModuleConfig of rootModuleConfig | BranchModuleConfig of branchModuleConfig
type moduleOverrides = RootModuleOverrides of rootModuleOverrides | BranchModuleOverrides of branchModuleOverrides

module ModuleDefaults =
    let fromRootProject (value: rootProjectConfig) : rootModuleConfig = {
        Description = value.Description
        ResultsFunctionName = value.ResultsFunctionName
        ResultsSortopiaDataTypes = value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = value.ResultsQueryParameterKeys
        ResultsParameterDefaults = value.ResultsParameterDefaults
    }
    let toRootProject (value: rootModuleConfig) : rootProjectConfig = {
        Description = value.Description
        Results = { DatabaseName = UMX.tag "Results"; FunctionName = value.ResultsFunctionName; FunctionVersion = Defaults.resultsVersion value.ResultsFunctionName
                    Outputs = Defaults.resultOutputs value.ResultsFunctionName
                    ParameterDefaults = value.ResultsParameterDefaults }
    }
    let fromRootOverrides (value: rootDefaultOverrides) : rootModuleOverrides = {
        Description = value.Description
        ResultsFunctionName = None
        ResultsParameterDefaults = value.ResultsParameterDefaults
    }
    let toRootOverrides (value: rootModuleOverrides) : rootDefaultOverrides = {
        Description = value.Description
        ResultsParameterDefaults = value.ResultsParameterDefaults
        ResultsDatabaseName = None
    }
    let root = fromRootProject Defaults.root
    let inheritRoot = fromRootOverrides Defaults.inheritRoot
    let resolveRoot (parent: rootModuleConfig) (local: rootModuleOverrides) =
        let name = local.ResultsFunctionName |> Option.defaultValue parent.ResultsFunctionName
        let parent = { parent with ResultsFunctionName = name
                                   ResultsParameterDefaults = if name = parent.ResultsFunctionName then parent.ResultsParameterDefaults else Defaults.resultsDefaultsFor name parent.ResultsParameterDefaults }
        Defaults.resolveRoot (toRootProject parent) (toRootOverrides local) |> fromRootProject
    let validateRoot local =
        toRootOverrides local |> Defaults.validateRoot
        local.ResultsFunctionName |> Option.iter (Sortopia.Functions.FunctionCatalogs.requireResults >> ignore)
    let validateRootConfiguration value = toRootProject value |> Defaults.validateRootModule

    let fromBranchProject (value: branchProjectConfig) : branchModuleConfig = {
        Description = value.Description
        ResultsFunctionName = value.ResultsFunctionName
        ResultsSortopiaDataTypes = value.ResultsSortopiaDataTypes
        ResultsQueryParameterKeys = value.ResultsQueryParameterKeys
        ResultsParameterDefaults = value.ResultsParameterDefaults
        SetupFunctionName = value.SetupFunctionName
        SetupDataTypes = value.SetupDataTypes
        SetupParameterDefaults = value.SetupParameterDefaults
    }
    let toBranchProject (value: branchModuleConfig) : branchProjectConfig = {
        Description = value.Description
        Results = { DatabaseName = UMX.tag "Results"; FunctionName = value.ResultsFunctionName; FunctionVersion = Defaults.resultsVersion value.ResultsFunctionName
                    Outputs = Defaults.resultOutputs value.ResultsFunctionName
                    ParameterDefaults = value.ResultsParameterDefaults }
        Setup = { DatabaseName = UMX.tag "Setup"; FunctionName = value.SetupFunctionName; FunctionVersion = Defaults.setupVersion value.SetupFunctionName
                  Outputs = Defaults.setupOutputs value.SetupFunctionName (Defaults.sources value.SetupDataTypes); ParameterDefaults = value.SetupParameterDefaults }
    }
    let fromBranchOverrides (value: branchDefaultOverrides) : branchModuleOverrides = {
        Description = value.Description
        ResultsFunctionName = None
        ResultsParameterDefaults = value.ResultsParameterDefaults
        SetupFunctionName = None
        SetupSources = value.SetupSources
        SetupParameterDefaults = value.SetupParameterDefaults
    }
    let toBranchOverrides (value: branchModuleOverrides) : branchDefaultOverrides = {
        Description = value.Description
        ResultsParameterDefaults = value.ResultsParameterDefaults
        SetupSources = value.SetupSources
        SetupParameterDefaults = value.SetupParameterDefaults
        ResultsDatabaseName = None
        SetupDatabaseName = None
    }
    let branch = fromBranchProject Defaults.branch
    let inheritBranch = fromBranchOverrides Defaults.inheritBranch
    let resolveBranch (parent: branchModuleConfig) (local: branchModuleOverrides) =
        let resultsName = local.ResultsFunctionName |> Option.defaultValue parent.ResultsFunctionName
        let setupName = local.SetupFunctionName |> Option.defaultValue parent.SetupFunctionName
        let parent = { parent with ResultsFunctionName = resultsName; SetupFunctionName = setupName
                                   ResultsParameterDefaults = if resultsName = parent.ResultsFunctionName then parent.ResultsParameterDefaults else Defaults.resultsDefaultsFor resultsName parent.ResultsParameterDefaults
                                   SetupParameterDefaults = if setupName = parent.SetupFunctionName then parent.SetupParameterDefaults else Defaults.setupDefaultsFor setupName parent.SetupParameterDefaults }
        Defaults.resolveBranch (toBranchProject parent) (toBranchOverrides local) |> fromBranchProject
    let validateBranch local =
        toBranchOverrides local |> Defaults.validateBranch
        local.ResultsFunctionName |> Option.iter (Sortopia.Functions.FunctionCatalogs.requireResults >> ignore)
        local.SetupFunctionName |> Option.iter (Sortopia.Functions.FunctionCatalogs.requireSetup >> ignore)
    let validateBranchConfiguration value = toBranchProject value |> Defaults.validateBranchModule

    let fromProject = function RootProjectConfig value -> RootModuleConfig (fromRootProject value) | BranchProjectConfig value -> BranchModuleConfig (fromBranchProject value)
    let toProject = function RootModuleConfig value -> RootProjectConfig (toRootProject value) | BranchModuleConfig value -> BranchProjectConfig (toBranchProject value)
    let fromOverrides = function RootOverrides value -> RootModuleOverrides (fromRootOverrides value) | BranchOverrides value -> BranchModuleOverrides (fromBranchOverrides value)
    let toOverrides = function RootModuleOverrides value -> RootOverrides (toRootOverrides value) | BranchModuleOverrides value -> BranchOverrides (toBranchOverrides value)
    let configuration isBranch = if isBranch then BranchModuleConfig branch else RootModuleConfig root
    let resolve parent local =
        match parent, local with
        | RootModuleConfig p, RootModuleOverrides l ->
            validateRoot l
            let value = resolveRoot p l
            validateRootConfiguration value
            RootModuleConfig value
        | BranchModuleConfig p, BranchModuleOverrides l ->
            validateBranch l
            let value = resolveBranch p l
            validateBranchConfiguration value
            BranchModuleConfig value
        | _ -> invalidArg "local" "Module defaults must have the same root/branch type."

