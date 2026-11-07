namespace Sortopia.Workspaces.Serialization

open System
open System.IO
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

[<CLIMutable>]
type parameterSpanDto = { keyName: string; values: string array }
[<CLIMutable>]
type queryKeysDto = { outputDataType: int; keys: string array }
[<CLIMutable>]
type runDto = {
    schemaVersion: int
    runKind: string
    projectName: string
    runName: string
    folderName: string
    description: string
    resultsDatabaseName: string
    executeFunctionName: string
    outputParameterSpans: parameterSpanDto array
    outputDataTypes: int array
    outputQueryParameterKeys: string array
    setupDatabaseName: string
    setupFunctionName: string
    setupParameterSpans: parameterSpanDto array
    setupOutputDataTypes: int array
    setupQueryParameterKeys: queryKeysDto array
}
[<CLIMutable>]
type projectDto = { schemaVersion: int; projectName: string; folderName: string; runs: runDto array; projects: projectDto array }
[<CLIMutable>]
type moduleDto = { schemaVersion: int; moduleName: string; folderName: string; modules: moduleDto array; projects: projectDto array }
[<CLIMutable>]
type workspaceDto = { schemaVersion: int; workspaceName: string; folderName: string; modules: moduleDto array }

/// Read-only migration contract; new snapshots never contain an absolute root path.
[<CLIMutable>]
type workspaceDtoV1 = { schemaVersion: int; workspaceName: string; rootFolder: string; modules: moduleDto array }

module private Contract =
    let version value =
        if value <> 1 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}"
    let entityVersion value =
        if value <> 1 && value <> 2 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}"
    let folder version name folderName =
        if version = 1 then name else folderName
    let array name (value: 'a array) =
        if isNull value then invalidArg name "Required collection is missing."
        value
    let output value =
        if not (Enum.IsDefined(typeof<outputDataType>, value)) then invalidArg "outputDataType" $"Unknown output type: {value}"
        enum<outputDataType> value
    let spanTo (value: parameterSpanDto) =
        ParameterSpan.create (ParamKey.text value.keyName) (array "values" value.values |> Array.toList)
    let spanFrom (value: parameterSpan) = { keyName = UMX.untag value.KeyName; values = List.toArray value.Values }
    let depth value =
        if value > 64 then invalidArg "hierarchy" "Workspace nesting must not exceed 64 levels."

module RunDto =
    let fromDomain (value: run) : runDto = {
        schemaVersion = 2; runKind = if value.HasSetup then "branch" else "root"
        projectName = UMX.untag value.ProjectName; runName = UMX.untag value.RunName
        folderName = value.FolderName
        description = value.Description; resultsDatabaseName = UMX.untag value.ResultsDatabaseName
        executeFunctionName = value.ExecuteFunctionName
        outputParameterSpans = value.ParameterSpans |> List.map Contract.spanFrom |> List.toArray
        outputDataTypes = value.OutputDataTypes |> List.map int |> List.toArray
        outputQueryParameterKeys = value.OutputQueryKeys |> List.map UMX.untag |> List.toArray
        setupDatabaseName = value.SetupDatabaseName |> Option.map UMX.untag |> Option.defaultValue ""
        setupFunctionName = value.SetupFunctionName |> Option.defaultValue ""
        setupParameterSpans = value.SetupParameterSpans |> Option.defaultValue [] |> List.map Contract.spanFrom |> List.toArray
        setupOutputDataTypes = value.SetupOutputDataTypes |> Option.defaultValue [] |> List.map int |> List.toArray
        setupQueryParameterKeys = value.SetupQueryParameterKeys |> Option.defaultValue Map.empty |> Map.toArray
                                 |> Array.map (fun (kind, keys) -> { outputDataType = int kind; keys = keys |> List.map UMX.untag |> List.toArray }) }

    let toDomain (value: runDto) : run =
        Contract.entityVersion value.schemaVersion
        EntityName.validate value.runName |> ignore
        EntityName.validate value.projectName |> ignore
        let folderName = Contract.folder value.schemaVersion value.runName value.folderName
        let outputSpans = Contract.array "outputParameterSpans" value.outputParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let outputTypes = Contract.array "outputDataTypes" value.outputDataTypes |> Array.map Contract.output |> Array.toList
        let outputKeys = Contract.array "outputQueryParameterKeys" value.outputQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let setupSpans = Contract.array "setupParameterSpans" value.setupParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let setupTypes = Contract.array "setupOutputDataTypes" value.setupOutputDataTypes |> Array.map Contract.output |> Array.toList
        let queryEntries = Contract.array "setupQueryParameterKeys" value.setupQueryParameterKeys |> Array.map (fun entry ->
            Contract.output entry.outputDataType, Contract.array "keys" entry.keys |> Array.map UMX.tag<paramKeyName> |> Array.toList)
        let queryKeys = Map.ofArray queryEntries
        if queryKeys.Count <> queryEntries.Length then invalidArg "setupQueryParameterKeys" "Duplicate output query entries."
        match value.runKind with
        | "root" ->
            if value.setupDatabaseName <> "" || value.setupFunctionName <> "" || not setupSpans.IsEmpty || not setupTypes.IsEmpty || not queryKeys.IsEmpty then
                invalidArg "runKind" "A root run cannot have setup settings."
            RootRun.create {
                ProjectName = UMX.tag value.projectName; RunName = UMX.tag value.runName; Description = value.description
                FolderName = folderName
                ResultsDatabaseName = UMX.tag value.resultsDatabaseName; ExecuteFunctionName = value.executeFunctionName
                OutputParameterSpans = outputSpans; OutputDataTypes = outputTypes; OutputQueryParameterKeys = outputKeys } |> run.RootRun
        | "branch" ->
            BranchRun.create {
                ProjectName = UMX.tag value.projectName; RunName = UMX.tag value.runName; Description = value.description
                FolderName = folderName
                ResultsDatabaseName = UMX.tag value.resultsDatabaseName; ExecuteFunctionName = value.executeFunctionName
                OutputParameterSpans = outputSpans; OutputDataTypes = outputTypes; OutputQueryParameterKeys = outputKeys
                SetupDatabaseName = UMX.tag value.setupDatabaseName; SetupFunctionName = value.setupFunctionName
                SetupParameterSpans = setupSpans; SetupOutputDataTypes = setupTypes; SetupQueryParameterKeys = queryKeys } |> run.BranchRun
        | _ -> invalidArg "runKind" "Run kind must be root or branch."

module ProjectDto =
    let rec fromDomain (value: project) : projectDto = {
        schemaVersion = 2; projectName = UMX.untag value.ProjectName; folderName = value.FolderName
        runs = value.Runs |> Array.map RunDto.fromDomain; projects = value.Projects |> Array.map fromDomain }
    let rec internal read depth (value: projectDto) =
        Contract.depth depth
        Contract.entityVersion value.schemaVersion
        project.create (UMX.tag value.projectName) (Contract.folder value.schemaVersion value.projectName value.folderName)
            (Contract.array "runs" value.runs |> Array.map RunDto.toDomain)
            (Contract.array "projects" value.projects |> Array.map (read (depth + 1)))
    let toDomain value = read 0 value

module ModuleDto =
    let rec fromDomain (value: ``module``) : moduleDto = {
        schemaVersion = 2; moduleName = UMX.untag value.ModuleName; folderName = value.FolderName
        modules = value.Modules |> Array.map fromDomain; projects = value.Projects |> Array.map ProjectDto.fromDomain }
    let rec internal read depth (value: moduleDto) =
        Contract.depth depth
        Contract.entityVersion value.schemaVersion
        ``module``.create (UMX.tag value.moduleName) (Contract.folder value.schemaVersion value.moduleName value.folderName)
            (Contract.array "modules" value.modules |> Array.map (read (depth + 1)))
            (Contract.array "projects" value.projects |> Array.map (ProjectDto.read (depth + 1)))
    let toDomain value = read 0 value

module WorkspaceDto =
    let fromDomain (value: workspace) : workspaceDto = {
        schemaVersion = 2; workspaceName = UMX.untag value.WorkspaceName; folderName = value.FolderName
        modules = value.Modules |> Array.map ModuleDto.fromDomain }
    let toDomain (value: workspaceDto) =
        if value.schemaVersion <> 2 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value.schemaVersion}"
        workspace.create (UMX.tag value.workspaceName) value.folderName
            (Contract.array "modules" value.modules |> Array.map (ModuleDto.read 1))
    let internal fromVersion1 (value: workspaceDtoV1) =
        Contract.version value.schemaVersion
        if String.IsNullOrWhiteSpace value.rootFolder || not (Path.IsPathFullyQualified value.rootFolder) then
            invalidArg "rootFolder" "Legacy workspace root folder must be absolute."
        let folder = Path.GetFileName(Path.TrimEndingDirectorySeparator value.rootFolder)
        workspace.create (UMX.tag value.workspaceName) (if String.IsNullOrEmpty folder then "Workspace" else folder)
            (Contract.array "modules" value.modules |> Array.map (ModuleDto.read 1))

module WorkspaceCodec =
    let encodeWorkspace value = value |> WorkspaceDto.fromDomain |> MessagePackSetup.serialize
    let decodeWorkspace bytes =
        let dto = MessagePackSetup.deserialize<workspaceDto> bytes
        if dto.schemaVersion = 1 then bytes |> MessagePackSetup.deserialize<workspaceDtoV1> |> WorkspaceDto.fromVersion1
        else WorkspaceDto.toDomain dto
    let encodeModule value = value |> ModuleDto.fromDomain |> MessagePackSetup.serialize
    let decodeModule bytes = bytes |> MessagePackSetup.deserialize<moduleDto> |> ModuleDto.toDomain
    let encodeProject value = value |> ProjectDto.fromDomain |> MessagePackSetup.serialize
    let decodeProject bytes = bytes |> MessagePackSetup.deserialize<projectDto> |> ProjectDto.toDomain
    let encodeRun value = value |> RunDto.fromDomain |> MessagePackSetup.serialize
    let decodeRun bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.toDomain
