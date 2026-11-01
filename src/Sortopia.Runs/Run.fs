namespace Sortopia.Runs

open FSharp.UMX
open Sortopia.Core  
open Sortopia.Storage

[<Measure>] type databaseName
[<Measure>] type projectName
[<Measure>] type runName


module private RunConfiguration =
    let validate databaseArgument (databaseName: string<databaseName>) (projectName: string<projectName>)
                 (runName: string<runName>) (parameterSpans: parameterSpan list)
                 functionArgument functionName =
        for argument, value in
            [ databaseArgument, UMX.untag databaseName
              "projectName", UMX.untag projectName
              "runName", UMX.untag runName
              functionArgument, functionName ] do
            if System.String.IsNullOrWhiteSpace value then
                invalidArg argument "Name must not be null, empty, or whitespace."

        let keys = parameterSpans |> List.map (fun span -> span.KeyName)
        let spanKeys = Set.ofList keys
        if spanKeys.Count <> keys.Length then
            invalidArg "parameterSpans" "Parameter spans must have unique key names."

        for span in parameterSpans do
            if System.String.IsNullOrWhiteSpace (UMX.untag span.KeyName) then
                invalidArg "parameterSpans" "Parameter key names must not be null, empty, or whitespace."
            if span.Values.IsEmpty then
                invalidArg "parameterSpans" $"Parameter '{span.KeyName}' must have at least one value."

        spanKeys

    let validateOutputQueryKeys spanKeys (outputQueryKeys: string<paramKeyName> list) =
        if outputQueryKeys |> List.exists (fun key -> System.String.IsNullOrWhiteSpace (UMX.untag key)) then
            invalidArg "outputQueryKeys" "Output query key names must not be blank."
        if (Set.ofList outputQueryKeys).Count <> outputQueryKeys.Length then
            invalidArg "outputQueryKeys" "Output query keys must be unique."

        for key in outputQueryKeys do
            if not (Set.contains key spanKeys) then
                invalidArg "outputQueryKeys" $"Query key '{key}' has no matching output parameter span."

    let validateQueryKeys spanKeys outputDataTypes (queryParameterKeys: Map<outputDataType, string<paramKeyName> list>) =
        for outputType in outputDataTypes do
            if not (Map.containsKey outputType queryParameterKeys) then
                invalidArg "queryParameterKeys" $"Output '{outputType}' must have a query parameter entry."

        for KeyValue(outputType, queryKeys) in queryParameterKeys do
            if (Set.ofList queryKeys).Count <> queryKeys.Length then
                invalidArg "queryParameterKeys" $"Output '{outputType}' must have unique query parameter keys."

            for key in queryKeys do
                if not (Set.contains key spanKeys) then
                    invalidArg "queryParameterKeys" $"Setup output '{outputType}' query key '{key}' has no matching setup parameter span."

    let makeParameterSets (spans: parameterSpan list) : parameterSet seq =
        match spans with
        | [] -> Seq.singleton (parameterSet.create Map.empty)
        | _ ->
            spans
            |> List.map (fun span -> span.KeyName, span.Values)
            |> Combinatorics.cartesianProductMaps
            |> Seq.map parameterSet.create

type rootRunConfig = {
    ProjectName: string<projectName>
    RunName: string<runName>
    Description: string
    ResultsDatabaseName: string<databaseName>
    ExecuteFunctionName: string
    OutputParameterSpans: parameterSpan list
    OutputDataTypes: outputDataType list
    OutputQueryParameterKeys: string<paramKeyName> list
}

/// A run whose execution has no dependencies on other runs.
type rootRun =
    private { config: rootRunConfig }

    member this.ProjectName = this.config.ProjectName
    member this.RunName = this.config.RunName
    member this.Description = this.config.Description
    member this.ResultsDatabaseName = this.config.ResultsDatabaseName
    member this.ExecuteFunctionName = this.config.ExecuteFunctionName
    member this.OutputParameterSpans = this.config.OutputParameterSpans
    member this.OutputDataTypes = this.config.OutputDataTypes
    member this.OutputQueryParameterKeys = this.config.OutputQueryParameterKeys

module RootRun =
    let create (config: rootRunConfig) : rootRun =
        let outputSpanKeys =
            RunConfiguration.validate "resultsDatabaseName" config.ResultsDatabaseName config.ProjectName config.RunName
                config.OutputParameterSpans "executeFunctionName" config.ExecuteFunctionName
        RunConfiguration.validateOutputQueryKeys outputSpanKeys config.OutputQueryParameterKeys
        { config = config }

    let makeParameterSets (root: rootRun) =
        RunConfiguration.makeParameterSets root.OutputParameterSpans

type branchRunConfig = {
    ProjectName: string<projectName>
    RunName: string<runName>
    Description: string
    ResultsDatabaseName: string<databaseName>
    ExecuteFunctionName: string
    OutputParameterSpans: parameterSpan list
    OutputDataTypes: outputDataType list
    OutputQueryParameterKeys: string<paramKeyName> list
    SetupDatabaseName: string<databaseName>
    SetupFunctionName: string
    SetupParameterSpans: parameterSpan list
    SetupOutputDataTypes: outputDataType list
    SetupQueryParameterKeys: Map<outputDataType, string<paramKeyName> list>
}

/// A run with separate setup and execution steps.
type branchRun =
    private { config: branchRunConfig }

    member this.ProjectName = this.config.ProjectName
    member this.RunName = this.config.RunName
    member this.Description = this.config.Description
    member this.ResultsDatabaseName = this.config.ResultsDatabaseName
    member this.ExecuteFunctionName = this.config.ExecuteFunctionName
    member this.OutputParameterSpans = this.config.OutputParameterSpans
    member this.OutputDataTypes = this.config.OutputDataTypes
    member this.OutputQueryParameterKeys = this.config.OutputQueryParameterKeys
    member this.SetupDatabaseName = this.config.SetupDatabaseName
    member this.SetupFunctionName = this.config.SetupFunctionName
    member this.SetupParameterSpans = this.config.SetupParameterSpans
    member this.SetupOutputDataTypes = this.config.SetupOutputDataTypes
    member this.SetupQueryParameterKeys = this.config.SetupQueryParameterKeys

module BranchRun =
    let create (config: branchRunConfig) : branchRun =
        let outputSpanKeys =
            RunConfiguration.validate "resultsDatabaseName" config.ResultsDatabaseName config.ProjectName config.RunName
                config.OutputParameterSpans "executeFunctionName" config.ExecuteFunctionName
        RunConfiguration.validateOutputQueryKeys outputSpanKeys config.OutputQueryParameterKeys
        let setupSpanKeys =
            RunConfiguration.validate "setupDatabaseName" config.SetupDatabaseName config.ProjectName config.RunName
                config.SetupParameterSpans "setupFunctionName" config.SetupFunctionName
        RunConfiguration.validateQueryKeys setupSpanKeys config.SetupOutputDataTypes config.SetupQueryParameterKeys
        { config = config }

    let makeParameterSets (branch: branchRun) =
        RunConfiguration.makeParameterSets branch.OutputParameterSpans

    let makeSetupParameterSets (branch: branchRun) =
        RunConfiguration.makeParameterSets branch.SetupParameterSpans


type run =
    | RootRun of rootRun
    | BranchRun of branchRun

    member this.HasSetup =
        match this with
        | RootRun _ -> false
        | BranchRun _ -> true

    member this.SetupDatabaseName =
        match this with
        | RootRun _ -> None
        | BranchRun value -> Some value.SetupDatabaseName

    member this.ResultsDatabaseName =
        match this with
        | RootRun value -> value.ResultsDatabaseName
        | BranchRun value -> value.ResultsDatabaseName

    member this.ProjectName =
        match this with
        | RootRun value -> value.ProjectName
        | BranchRun value -> value.ProjectName

    member this.RunName =
        match this with
        | RootRun value -> value.RunName
        | BranchRun value -> value.RunName

    member this.Description =
        match this with
        | RootRun value -> value.Description
        | BranchRun value -> value.Description

    member this.ParameterSpans =
        match this with
        | RootRun value -> value.OutputParameterSpans
        | BranchRun value -> value.OutputParameterSpans

    member this.ExecuteFunctionName =
        match this with
        | RootRun value -> value.ExecuteFunctionName
        | BranchRun value -> value.ExecuteFunctionName

    member this.OutputDataTypes =
        match this with
        | RootRun value -> value.OutputDataTypes
        | BranchRun value -> value.OutputDataTypes

    member this.OutputQueryKeys =
        match this with
        | RootRun value -> value.OutputQueryParameterKeys
        | BranchRun value -> value.OutputQueryParameterKeys

    member this.SetupFunctionName =
        match this with
        | RootRun _ -> None
        | BranchRun value -> Some value.SetupFunctionName

    member this.SetupParameterSpans =
        match this with
        | RootRun _ -> None
        | BranchRun value -> Some value.SetupParameterSpans

    member this.SetupOutputDataTypes =
        match this with
        | RootRun _ -> None
        | BranchRun value -> Some value.SetupOutputDataTypes

    member this.SetupQueryParameterKeys =
        match this with
        | RootRun _ -> None
        | BranchRun value -> Some value.SetupQueryParameterKeys

[<Measure>] type queryParamsId

/// Identifies results by database, project, output type, and selected parameter values.
type queryParams =
    private {
        databaseName: string<databaseName>
        projectName: string<projectName>
        outputDataType: outputDataType
        properties: Map<string<paramKeyName>, string>
        id: Guid<queryParamsId>
    }

    member this.DatabaseName = this.databaseName
    member this.ProjectName = this.projectName
    member this.OutputDataType = this.outputDataType
    member this.Properties = this.properties
    member this.Id = this.id

module QueryParams =
    let private create database project outputType (keys: string<paramKeyName> list) (parameters: parameterSet) =
        let property key =
            match parameters.TryFind(key: string<paramKeyName>) with
            | Some value -> key, value
            | None -> invalidArg "parameters" $"Query for '{outputType}' requires parameter '{key}'."

        let properties = keys |> List.map property |> Map.ofList
        let id =
            seq {
                yield box database
                yield box project
                // GeneSort reserves these positions for optional replica and generation.
                yield box false
                yield box false
                yield box (string outputType)
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
          outputDataType = outputType
          properties = properties
          id = id }

    /// Builds a result query for either run type.
    let forResults (run: run) outputType parameters =
        if not (List.contains outputType run.OutputDataTypes) then
            invalidArg "outputType" $"Run '{run.RunName}' does not declare result type '{outputType}'."
        create run.ResultsDatabaseName run.ProjectName outputType run.OutputQueryKeys parameters

    /// Builds a setup query using the keys configured for the requested output type.
    let forSetup (branch: branchRun) outputType parameters =
        if not (List.contains outputType branch.SetupOutputDataTypes) then
            invalidArg "outputType" $"Run '{branch.RunName}' does not declare setup type '{outputType}'."
        create branch.SetupDatabaseName branch.ProjectName outputType branch.SetupQueryParameterKeys[outputType] parameters
