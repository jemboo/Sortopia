namespace Sortopia.Workspaces

open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// A present setup block identifies a branch, even before a function is selected.
type moduleSetupConfig = {
    FunctionName: string<setupFunctionName>
    Sources: setupSourceConfig list
    ParameterDefaults: parameterBinding list
} with
    member this.SortopiaDataTypes =
        if string this.FunctionName = "" then [] else (SetupFunctionCatalog.require this.FunctionName).SortopiaDataTypes
    member this.SourceProjectIds = this.Sources |> List.map (fun entry -> entry.DataType, entry.SourceProjectId) |> Map.ofList

/// Resolved module inheritance contains neither database identity nor query projections.
type moduleConfiguration = {
    Description: string
    ResultsFunctionName: string<resultsFunctionName>
    ResultsParameterDefaults: parameterBinding list
    Setup: moduleSetupConfig option
} with
    member this.HasSetup = this.Setup.IsSome
    member this.ResultsSortopiaDataTypes =
        if string this.ResultsFunctionName = "" then [] else (ResultsFunctionCatalog.require this.ResultsFunctionName).SortopiaDataTypes

type moduleSetupOverrides = {
    FunctionName: string<setupFunctionName> option
    Sources: setupSourceConfig list option
    ParameterDefaults: parameterBinding list option
    ExcludedDefaults: string<paramKeyName> list
}

type moduleOverrides = {
    Description: string option
    ResultsFunctionName: string<resultsFunctionName> option
    ResultsParameterDefaults: parameterBinding list option
    ExcludedResultsDefaults: string<paramKeyName> list
    Setup: moduleSetupOverrides option
}

module ModuleDefaults =
    open ConfigurationInheritance

    let private defined value = not (System.String.IsNullOrWhiteSpace(string value))
    // Empty containers may inherit functions later. Definition counts are checked
    // along the module path when a project is prepared for generation.
    let private resolveFunction parent local = local |> Option.filter defined |> Option.defaultValue parent
    let private resolveParameters requirements oldName name inherited local excluded =
        let inherited = if name = oldName then inherited else defaultsFor (requirements name) inherited
        resolveDefaults inherited local excluded

    let configuration hasSetup : moduleConfiguration = {
        Description = ""; ResultsFunctionName = UMX.tag ""; ResultsParameterDefaults = []
        Setup = if hasSetup then Some { FunctionName = UMX.tag ""; Sources = []; ParameterDefaults = [] } else None }
    let inherited hasSetup : moduleOverrides = {
        Description = None; ResultsFunctionName = None; ResultsParameterDefaults = None; ExcludedResultsDefaults = []
        Setup = if hasSetup then Some { FunctionName = None; Sources = None; ParameterDefaults = None; ExcludedDefaults = [] } else None }

    let validate (local: moduleOverrides) =
        local.Description |> Option.iter validateDescription
        validateDefaultOverrides "excludedResultsDefaults" local.ResultsParameterDefaults local.ExcludedResultsDefaults
        local.ResultsFunctionName |> Option.filter defined |> Option.iter (ResultsFunctionCatalog.require >> ignore)
        local.Setup |> Option.iter (fun setup ->
            validateDefaultOverrides "excludedSetupDefaults" setup.ParameterDefaults setup.ExcludedDefaults
            setup.FunctionName |> Option.filter defined |> Option.iter (SetupFunctionCatalog.require >> ignore)
            setup.Sources |> Option.iter validateSources)

    let validateConfiguration (value: moduleConfiguration) =
        validateDescription value.Description
        ParameterBinding.validateUnique value.ResultsParameterDefaults
        if defined value.ResultsFunctionName then
            FunctionValidation.validateDefaults (ResultsFunctionCatalog.require value.ResultsFunctionName) value.ResultsParameterDefaults
        validateParameters (resultsRequirements value.ResultsFunctionName) value.ResultsParameterDefaults []
        value.Setup |> Option.iter (fun setup ->
            ParameterBinding.validateUnique setup.ParameterDefaults
            validateSources setup.Sources
            if defined setup.FunctionName then
                let definition = SetupFunctionCatalog.require setup.FunctionName
                FunctionValidation.validateDefaults definition setup.ParameterDefaults
                if definition.ParametersFromSources then
                    FunctionValidation.validate definition (parameterSet.fromBindings setup.ParameterDefaults)
                if setup.Sources |> List.exists (fun source -> not (List.contains source.DataType definition.SourceDataTypes)) then
                    invalidArg "setupSources" "Source assignments must be inputs consumed by the selected setup function."
            validateParameters (setupRequirements setup.FunctionName) setup.ParameterDefaults [])

    let resolve (parent: moduleConfiguration) (local: moduleOverrides) : moduleConfiguration =
        validate local
        let setup =
            match parent.Setup, local.Setup with
            | None, None -> None
            | Some parent, Some local ->
                let name = resolveFunction parent.FunctionName local.FunctionName
                let sourceTypes = if defined name then (SetupFunctionCatalog.require name).SourceDataTypes else []
                Some {
                    FunctionName = name
                    Sources = mergeSources parent.Sources (Option.defaultValue [] local.Sources)
                              |> List.filter (fun entry -> List.contains entry.DataType sourceTypes)
                    ParameterDefaults = resolveParameters setupRequirements parent.FunctionName name parent.ParameterDefaults local.ParameterDefaults local.ExcludedDefaults }
            | _ -> invalidArg "local" "Module overrides must match the inherited setup phase."
        let results = resolveFunction parent.ResultsFunctionName local.ResultsFunctionName
        let value = {
            Description = local.Description |> Option.defaultValue parent.Description
            ResultsFunctionName = results
            ResultsParameterDefaults = resolveParameters resultsRequirements parent.ResultsFunctionName results parent.ResultsParameterDefaults local.ResultsParameterDefaults local.ExcludedResultsDefaults
            Setup = setup }
        validateConfiguration value
        value

    // One-way boundary: a project starts from module inheritance, then owns its
    // database names and per-output query projections. Module resolution never uses this.
    let toProject (value: moduleConfiguration) : projectConfiguration = {
        Description = value.Description
        Results = { DatabaseName = UMX.tag "Results"; FunctionName = value.ResultsFunctionName; FunctionVersion = ProjectResolution.resultsVersion value.ResultsFunctionName
                    Outputs = ProjectResolution.resultOutputs value.ResultsFunctionName; ParameterDefaults = value.ResultsParameterDefaults }
        Setup = value.Setup |> Option.map (fun setup -> {
            DatabaseName = UMX.tag "Setup"; FunctionName = setup.FunctionName; FunctionVersion = ProjectResolution.setupVersion setup.FunctionName
            Outputs = ProjectResolution.setupOutputs setup.FunctionName setup.Sources; ParameterDefaults = setup.ParameterDefaults }) }
