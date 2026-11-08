namespace Sortopia.Workspaces.Serialization

open System
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Workspaces
open Sortopia.Core.Serialization

[<CLIMutable>]
type parameterSpanDto = { keyName: string; values: string array }
[<CLIMutable>]
type projectSettingsDto = {
    description: string
    resultsDatabaseName: string
    executeFunctionName: string
    outputDataTypes: int array
    outputQueryParameterKeys: string array
    setupDatabaseName: string
    setupFunctionName: string
    setupOutputDataTypes: int array
    setupQueryParameterKeys: string array
}
[<CLIMutable>]
type runDto = {
    schemaVersion: int
    runKind: string
    projectName: string
    runName: string
    id: Guid
    description: string
    outputParameterSpans: parameterSpanDto array
    setupDatabaseName: string
    setupFunctionName: string
    setupParameterSpans: parameterSpanDto array
}
[<CLIMutable>]
type projectDto = { schemaVersion: int; id: Guid; projectKind: string; projectName: string; folderName: string; configuration: projectSettingsDto; runs: runDto array }
[<CLIMutable>]
type moduleDto = { schemaVersion: int; id: Guid; moduleKind: string; moduleName: string; folderName: string; modules: moduleDto array; projects: projectDto array }
[<CLIMutable>]
type workspaceDto = { schemaVersion: int; id: Guid; workspaceName: string; folderName: string; modules: moduleDto array }

module private Contract =
    let version value =
        if value <> 5 then invalidArg "schemaVersion" $"Unsupported workspace format version: {value}. Create a new workspace in the current format."
    let kind value =
        match value with
        | "root" -> hierarchyKind.Root
        | "branch" -> hierarchyKind.Branch
        | _ -> invalidArg "kind" "Kind must be root or branch."
    let kindName value = if value = hierarchyKind.Root then "root" else "branch"
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
    let fromDomain (value: run) : runDto =
        let description, setupDatabase, setupFunction, setupSpans =
            match value with
            | run.RootRun r -> r.Configuration.Description, None, None, []
            | run.BranchRun r -> r.Configuration.Description, r.Configuration.SetupDatabaseName, r.Configuration.SetupFunctionName, r.SetupParameterSpans
        { schemaVersion = 5; runKind = if value.HasSetup then "branch" else "root"
          projectName = UMX.untag value.ProjectName; runName = UMX.untag value.RunName; id = UMX.untag value.Id
          description = description |> Option.toObj
          outputParameterSpans = value.ParameterSpans |> List.map Contract.spanFrom |> List.toArray
          setupDatabaseName = setupDatabase |> Option.map UMX.untag |> Option.toObj
          setupFunctionName = setupFunction |> Option.map UMX.untag |> Option.toObj
          setupParameterSpans = setupSpans |> List.map Contract.spanFrom |> List.toArray }
    let readId (value: runDto) =
        Contract.version value.schemaVersion
        Contract.kind value.runKind |> ignore
        EntityId.validate value.id
        EntityName.validate value.runName |> ignore
        UMX.tag<runId> value.id
    /// A run file contains overrides and must be resolved against its containing project.
    let toDomain (parent: project) (value: runDto) : run =
        let id = readId value
        let outputSpans = Contract.array "outputParameterSpans" value.outputParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let setupSpans = Contract.array "setupParameterSpans" value.setupParameterSpans |> Array.map Contract.spanTo |> Array.toList
        let description = Option.ofObj value.description
        match parent, value.runKind with
        | project.RootProject p, "root" ->
            if not (isNull value.setupDatabaseName) || not (isNull value.setupFunctionName) || not setupSpans.IsEmpty then
                invalidArg "runKind" "A root run cannot have setup settings."
            RootRun.create p { Id = id; RunName = UMX.tag value.runName; Description = description; OutputParameterSpans = outputSpans } |> run.RootRun
        | project.BranchProject p, "branch" ->
            BranchRun.create p { Id = id; RunName = UMX.tag value.runName; Description = description; OutputParameterSpans = outputSpans
                                 SetupDatabaseName = Option.ofObj value.setupDatabaseName |> Option.map UMX.tag<setupDatabaseName>
                                 SetupFunctionName = Option.ofObj value.setupFunctionName |> Option.map UMX.tag<setupFunctionName>
                                 SetupParameterSpans = setupSpans } |> run.BranchRun
        | _ -> invalidArg "runKind" "A project can only contain runs of its own type."

module ProjectDto =
    let settingsFromDomain (value: project) : projectSettingsDto =
        let setupDatabase, setupFunction, setupOutputs, setupKeys =
            match value with
            | project.RootProject _ -> "", "", [], []
            | project.BranchProject p -> UMX.untag p.SetupDatabaseName, UMX.untag p.SetupFunctionName, p.SetupOutputDataTypes, p.SetupQueryParameterKeys
        { description = value.Description; resultsDatabaseName = UMX.untag value.ResultsDatabaseName
          executeFunctionName = UMX.untag value.ExecuteFunctionName
          outputDataTypes = value.OutputDataTypes |> List.map int |> List.toArray
          outputQueryParameterKeys = value.OutputQueryParameterKeys |> List.map UMX.untag |> List.toArray
          setupDatabaseName = setupDatabase; setupFunctionName = setupFunction
          setupOutputDataTypes = setupOutputs |> List.map int |> List.toArray
          setupQueryParameterKeys = setupKeys |> List.map UMX.untag |> List.toArray }
    let settingsToDomain kind (value: projectSettingsDto) =
        if isNull (box value) then nullArg "configuration"
        let outputs = Contract.array "outputDataTypes" value.outputDataTypes |> Array.map Contract.output |> Array.toList
        let keys = Contract.array "outputQueryParameterKeys" value.outputQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        let setupOutputs = Contract.array "setupOutputDataTypes" value.setupOutputDataTypes |> Array.map Contract.output |> Array.toList
        let setupKeys = Contract.array "setupQueryParameterKeys" value.setupQueryParameterKeys |> Array.map UMX.tag<paramKeyName> |> Array.toList
        match kind with
        | hierarchyKind.Root ->
            if value.setupDatabaseName <> "" || value.setupFunctionName <> "" || not setupOutputs.IsEmpty || not setupKeys.IsEmpty then
                invalidArg "configuration" "A root project cannot have setup settings."
            RootProjectConfig { Description = value.description; ResultsDatabaseName = UMX.tag value.resultsDatabaseName
                                ExecuteFunctionName = UMX.tag value.executeFunctionName; OutputDataTypes = outputs; OutputQueryParameterKeys = keys }
        | hierarchyKind.Branch ->
            BranchProjectConfig { Description = value.description; ResultsDatabaseName = UMX.tag value.resultsDatabaseName
                                  ExecuteFunctionName = UMX.tag value.executeFunctionName; OutputDataTypes = outputs; OutputQueryParameterKeys = keys
                                  SetupDatabaseName = UMX.tag value.setupDatabaseName; SetupFunctionName = UMX.tag value.setupFunctionName
                                  SetupOutputDataTypes = setupOutputs; SetupQueryParameterKeys = setupKeys }
        | _ -> invalidArg "kind" "Project kind must be root or branch."
    let fromDomain (value: project) : projectDto = {
        schemaVersion = 5; id = UMX.untag value.Id; projectKind = Contract.kindName value.Kind; projectName = UMX.untag value.ProjectName; folderName = value.FolderName
        configuration = settingsFromDomain value; runs = value.Runs |> Array.map RunDto.fromDomain }
    let internal read depth (value: projectDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        let configuration = settingsToDomain (Contract.kind value.projectKind) value.configuration
        let parent = project.create (UMX.tag value.id) (UMX.tag value.projectName) value.folderName configuration [||]
        let runs = Contract.array "runs" value.runs |> Array.map (RunDto.toDomain parent)
        project.create parent.Id parent.ProjectName parent.FolderName configuration runs
    let toDomain value = read 0 value

module ModuleDto =
    let rec fromDomain (value: ``module``) : moduleDto = {
        schemaVersion = 5; id = UMX.untag value.Id; moduleKind = Contract.kindName value.Kind; moduleName = UMX.untag value.ModuleName; folderName = value.FolderName
        modules = value.Modules |> Array.map fromDomain; projects = value.Projects |> Array.map ProjectDto.fromDomain }
    let rec internal read depth (value: moduleDto) =
        Contract.depth depth
        Contract.version value.schemaVersion
        ``module``.create (Contract.kind value.moduleKind) (UMX.tag value.id) (UMX.tag value.moduleName) value.folderName
            (Contract.array "modules" value.modules |> Array.map (read (depth + 1)))
            (Contract.array "projects" value.projects |> Array.map (ProjectDto.read (depth + 1)))
    let toDomain value = read 0 value

module WorkspaceDto =
    let fromDomain (value: workspace) : workspaceDto = {
        schemaVersion = 5; id = UMX.untag value.Id; workspaceName = UMX.untag value.WorkspaceName; folderName = value.FolderName
        modules = value.Modules |> Array.map ModuleDto.fromDomain }
    let toDomain (value: workspaceDto) =
        Contract.version value.schemaVersion
        workspace.create (UMX.tag value.id) (UMX.tag value.workspaceName) value.folderName
            (Contract.array "modules" value.modules |> Array.map (ModuleDto.read 1))

module WorkspaceCodec =
    let encodeWorkspace value = value |> WorkspaceDto.fromDomain |> MessagePackSetup.serialize
    let decodeWorkspace bytes = bytes |> MessagePackSetup.deserialize<workspaceDto> |> WorkspaceDto.toDomain
    let encodeModule value = value |> ModuleDto.fromDomain |> MessagePackSetup.serialize
    let decodeModule bytes = bytes |> MessagePackSetup.deserialize<moduleDto> |> ModuleDto.toDomain
    let encodeProject value = value |> ProjectDto.fromDomain |> MessagePackSetup.serialize
    let decodeProject bytes = bytes |> MessagePackSetup.deserialize<projectDto> |> ProjectDto.toDomain
    let encodeRun value = value |> RunDto.fromDomain |> MessagePackSetup.serialize
    let decodeRun parent bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.toDomain parent
    let readRunId bytes = bytes |> MessagePackSetup.deserialize<runDto> |> RunDto.readId
