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
[<Measure>] type executeFunctionName
[<Measure>] type setupFunctionName


/// Shared execution settings, owned by a root project.
type rootProjectConfig = {
    Description: string
    ResultsDatabaseName: string<resultsDatabaseName>
    ExecuteFunctionName: string<executeFunctionName>
    OutputDataTypes: sortopiaDataType list
    OutputQueryParameterKeys: string<paramKeyName> list
    OutputParameterDefaults: parameterBinding list
}

/// Shared setup and execution settings, owned by a branch project.
type branchProjectConfig = {
    Description: string
    ResultsDatabaseName: string<resultsDatabaseName>
    ExecuteFunctionName: string<executeFunctionName>
    OutputDataTypes: sortopiaDataType list
    OutputQueryParameterKeys: string<paramKeyName> list
    OutputParameterDefaults: parameterBinding list
    SetupDatabaseName: string<setupDatabaseName>
    SetupFunctionName: string<setupFunctionName>
    SetupOutputDataTypes: sortopiaDataType list
    SetupQueryParameterKeys: string<paramKeyName> list
    SetupParameterDefaults: parameterBinding list
}

type projectConfiguration =
    | RootProjectConfig of rootProjectConfig
    | BranchProjectConfig of branchProjectConfig

/// Project contracts keep run construction independent of the workspace hierarchy.
type IRootProject =
    abstract ProjectName: string<projectName>
    abstract Configuration: rootProjectConfig

type IBranchProject =
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
        if isNull value.Description then nullArg "description"
        name "resultsDatabaseName" (UMX.untag value.ResultsDatabaseName)
        name "executeFunctionName" (UMX.untag value.ExecuteFunctionName)
        outputs "outputDataTypes" value.OutputDataTypes
        keys "outputQueryParameterKeys" value.OutputQueryParameterKeys
        ParameterBinding.validateUnique value.OutputParameterDefaults
    let validateBranch (value: branchProjectConfig) =
        validateRoot { Description = value.Description; ResultsDatabaseName = value.ResultsDatabaseName
                       ExecuteFunctionName = value.ExecuteFunctionName; OutputDataTypes = value.OutputDataTypes
                       OutputQueryParameterKeys = value.OutputQueryParameterKeys; OutputParameterDefaults = value.OutputParameterDefaults }
        name "setupDatabaseName" (UMX.untag value.SetupDatabaseName)
        name "setupFunctionName" (UMX.untag value.SetupFunctionName)
        outputs "setupOutputDataTypes" value.SetupOutputDataTypes
        keys "setupQueryParameterKeys" value.SetupQueryParameterKeys
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
    Description: string option
    OutputParameterSpans: parameterSpan list
}

type rootRun =
    private { projectName: string<projectName>; projectConfiguration: rootProjectConfig; config: rootRunConfig }
    member this.ProjectName = this.projectName
    member this.RunName = this.config.RunName
    member this.Id = this.config.Id
    member this.Configuration = this.config
    member this.Description = this.config.Description |> Option.defaultValue this.projectConfiguration.Description
    member this.ResultsDatabaseName = this.projectConfiguration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.projectConfiguration.ExecuteFunctionName
    member this.OutputParameterSpans = this.config.OutputParameterSpans
    member this.OutputDataTypes = this.projectConfiguration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.projectConfiguration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.projectConfiguration.OutputParameterDefaults

module RootRun =
    let create (parent: IRootProject) (config: rootRunConfig) : rootRun =
        let settings = parent.Configuration
        ProjectConfiguration.validateRoot settings
        RunConfiguration.identity config.Id parent.ProjectName config.RunName
        if config.Description |> Option.exists isNull then nullArg "description"
        RunConfiguration.spans "outputParameterSpans" settings.OutputQueryParameterKeys settings.OutputParameterDefaults config.OutputParameterSpans
        { projectName = parent.ProjectName; projectConfiguration = settings; config = config }
    let withNames projectName runName (value: rootRun) =
        RunConfiguration.identity value.Id projectName runName
        { value with projectName = projectName; config = { value.config with RunName = runName } }
    let makeParameterSets (root: rootRun) = ParameterSets.create root.OutputParameterDefaults root.OutputParameterSpans

type branchRunConfig = {
    Id: Guid<runId>
    RunName: string<runName>
    Description: string option
    OutputParameterSpans: parameterSpan list
    SetupDatabaseName: string<setupDatabaseName> option
    SetupFunctionName: string<setupFunctionName> option
    SetupParameterSpans: parameterSpan list
}

type branchRun =
    private { projectName: string<projectName>; projectConfiguration: branchProjectConfig; config: branchRunConfig }
    member this.ProjectName = this.projectName
    member this.RunName = this.config.RunName
    member this.Id = this.config.Id
    member this.Configuration = this.config
    member this.Description = this.config.Description |> Option.defaultValue this.projectConfiguration.Description
    member this.ResultsDatabaseName = this.projectConfiguration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.projectConfiguration.ExecuteFunctionName
    member this.OutputParameterSpans = this.config.OutputParameterSpans
    member this.OutputDataTypes = this.projectConfiguration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.projectConfiguration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.projectConfiguration.OutputParameterDefaults
    member this.SetupDatabaseName = this.config.SetupDatabaseName |> Option.defaultValue this.projectConfiguration.SetupDatabaseName
    member this.SetupFunctionName = this.config.SetupFunctionName |> Option.defaultValue this.projectConfiguration.SetupFunctionName
    member this.SetupParameterSpans = this.config.SetupParameterSpans
    member this.SetupOutputDataTypes = this.projectConfiguration.SetupOutputDataTypes
    member this.SetupQueryParameterKeys = this.projectConfiguration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.projectConfiguration.SetupParameterDefaults

module BranchRun =
    let create (parent: IBranchProject) (config: branchRunConfig) : branchRun =
        let settings = parent.Configuration
        ProjectConfiguration.validateBranch settings
        RunConfiguration.identity config.Id parent.ProjectName config.RunName
        if config.Description |> Option.exists isNull then nullArg "description"
        for argument, value in ["setupDatabaseName", config.SetupDatabaseName |> Option.map UMX.untag
                                "setupFunctionName", config.SetupFunctionName |> Option.map UMX.untag] do
            if value |> Option.exists System.String.IsNullOrWhiteSpace then invalidArg argument "An override must not be blank; omit it to inherit the project setting."
        RunConfiguration.spans "outputParameterSpans" settings.OutputQueryParameterKeys settings.OutputParameterDefaults config.OutputParameterSpans
        RunConfiguration.spans "setupParameterSpans" settings.SetupQueryParameterKeys settings.SetupParameterDefaults config.SetupParameterSpans
        { projectName = parent.ProjectName; projectConfiguration = settings; config = config }
    let withNames projectName runName (value: branchRun) =
        RunConfiguration.identity value.Id projectName runName
        { value with projectName = projectName; config = { value.config with RunName = runName } }
    let makeParameterSets (branch: branchRun) = ParameterSets.create branch.OutputParameterDefaults branch.OutputParameterSpans
    let makeSetupParameterSets (branch: branchRun) = ParameterSets.create branch.SetupParameterDefaults branch.SetupParameterSpans

type run =
    | RootRun of rootRun
    | BranchRun of branchRun
    member this.HasSetup = match this with RootRun _ -> false | BranchRun _ -> true
    member this.ProjectName = match this with RootRun r -> r.ProjectName | BranchRun r -> r.ProjectName
    member this.RunName = match this with RootRun r -> r.RunName | BranchRun r -> r.RunName
    member this.Id = match this with RootRun r -> r.Id | BranchRun r -> r.Id
    member this.Description = match this with RootRun r -> r.Description | BranchRun r -> r.Description
    member this.ResultsDatabaseName = match this with RootRun r -> r.ResultsDatabaseName | BranchRun r -> r.ResultsDatabaseName
    member this.ExecuteFunctionName = match this with RootRun r -> r.ExecuteFunctionName | BranchRun r -> r.ExecuteFunctionName
    member this.ParameterSpans = match this with RootRun r -> r.OutputParameterSpans | BranchRun r -> r.OutputParameterSpans
    member this.OutputDataTypes = match this with RootRun r -> r.OutputDataTypes | BranchRun r -> r.OutputDataTypes
    member this.OutputParameterDefaults = match this with RootRun r -> r.OutputParameterDefaults | BranchRun r -> r.OutputParameterDefaults
    member this.OutputQueryKeys = match this with RootRun r -> r.OutputQueryParameterKeys | BranchRun r -> r.OutputQueryParameterKeys
    member this.SetupDatabaseName = match this with RootRun _ -> None | BranchRun r -> Some r.SetupDatabaseName
    member this.SetupFunctionName = match this with RootRun _ -> None | BranchRun r -> Some r.SetupFunctionName
    member this.SetupParameterSpans = match this with RootRun _ -> None | BranchRun r -> Some r.SetupParameterSpans
    member this.SetupOutputDataTypes = match this with RootRun _ -> None | BranchRun r -> Some r.SetupOutputDataTypes
    member this.SetupParameterDefaults = match this with RootRun _ -> None | BranchRun r -> Some r.SetupParameterDefaults
    member this.SetupQueryParameterKeys = match this with RootRun _ -> None | BranchRun r -> Some r.SetupQueryParameterKeys

[<Measure>] type queryParamsId

/// Identifies results by database, project, output type, and selected parameter values.
type queryParams =
    private {
        databaseName: string<databaseName>
        projectName: string<projectName>
        outputDataType: sortopiaDataType
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
            match parameters.TryFindText(key: string<paramKeyName>) with
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
          outputDataType = outputType
          properties = properties
          id = id }

    /// Builds a result query for either run type.
    let forResults (run: run) outputType parameters =
        if not (List.contains outputType run.OutputDataTypes) then
            invalidArg "outputType" $"Run '{run.RunName}' does not declare result type '{outputType}'."
        create (UMX.cast<resultsDatabaseName, databaseName> run.ResultsDatabaseName) run.ProjectName outputType run.OutputQueryKeys parameters

    /// Builds a setup query using the keys configured for the requested output type.
    let forSetup (branch: branchRun) outputType parameters =
        if not (List.contains outputType branch.SetupOutputDataTypes) then
            invalidArg "outputType" $"Run '{branch.RunName}' does not declare setup type '{outputType}'."
        create (UMX.cast<setupDatabaseName, databaseName> branch.SetupDatabaseName) branch.ProjectName outputType branch.SetupQueryParameterKeys parameters
