namespace Sortopia.Workspaces

open System
open FSharp.UMX
open Sortopia.Runs

/// Display names are independent of file-system names.
module EntityName =
    let validate (name: string) =
        if String.IsNullOrWhiteSpace name || name <> name.Trim() || (name |> Seq.exists Char.IsControl) then
            invalidArg "name" "Use a nonblank name without leading/trailing whitespace or control characters."
        name

    let internal unique argument (names: string seq) =
        let seen = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for name in names do
            validate name |> ignore
            if not (seen.Add name) then invalidArg argument $"Duplicate name in {argument}: {name}"

module EntityId =
    let validate (id: System.Guid) =
        if id = System.Guid.Empty then invalidArg "id" "Entity ID must not be empty."
    let internal unique argument (ids: System.Guid seq) =
        let seen = Collections.Generic.HashSet<System.Guid>()
        for id in ids do
            validate id
            if not (seen.Add id) then invalidArg argument $"Duplicate ID in {argument}: {id}"

module WorkspaceRun =
    /// Renaming preserves the run's overrides and effective project settings.
    let withNames projectName runName (value: run) =
        match value with
        | run.RootRun root -> RootRun.withNames projectName runName root |> run.RootRun
        | run.BranchRun branch -> BranchRun.withNames projectName runName branch |> run.BranchRun

type hierarchyKind =
    | Root = 0
    | Branch = 1

type rootProject =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; configuration: rootProjectConfig; inheritedDefaults: rootProjectConfig; localOverrides: rootDefaultOverrides; isSealed: bool; runs: rootRun array }
    member this.IsSealed = this.isSealed
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.InheritedDefaults = this.inheritedDefaults
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ResultsFunctionName = this.configuration.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = this.configuration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.configuration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.configuration.ResultsParameterDefaults
    member this.Runs = Array.copy this.runs
    interface IRootProject with
        member this.Id = this.id
        member this.IsSealed = this.isSealed
        member this.ProjectName = this.projectName
        member this.Configuration = this.configuration
    static member private createState isSealed (id: Guid<projectId>) (name: string<projectName>) folderName parent (local: rootDefaultOverrides) (runs: rootRun array) : rootProject =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateRoot local
        let configuration = if isSealed then parent else Defaults.resolveRoot parent local
        ProjectConfiguration.validateRoot configuration
        if not isSealed then Defaults.validateRootModule configuration
        else Sortopia.Functions.FunctionConfiguration.validateResults configuration.Results
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ParentProjectId <> id) then invalidArg "runs" "Each run must reference its containing project ID."
        if runs |> Array.exists (fun r -> r.ProjectConfiguration <> configuration || r.ProjectName <> name) then
            invalidArg "configuration" "Existing runs require their original sealed project settings and name."
        let isSealed = isSealed || runs.Length > 0
        let local = if isSealed then Defaults.localRoot configuration else { local with ResultsDatabaseName = Some configuration.ResultsDatabaseName }
        let value = { isSealed = isSealed; id = id; projectName = name; folderName = folderName; configuration = configuration; inheritedDefaults = (if isSealed then configuration else parent); localOverrides = local; runs = [||] }
        { value with runs = runs |> Array.map (fun r -> RootRun.create value r.Configuration) }
    static member createInherited id name folderName parent local runs =
        rootProject.createState false id name folderName parent local runs
    static member restore id name folderName configuration local isSealed runs =
        rootProject.createState isSealed id name folderName configuration (if isSealed then Defaults.localRoot configuration else local) runs
    static member seal (value: rootProject) =
        { value with isSealed = true; inheritedDefaults = value.Configuration; localOverrides = Defaults.localRoot value.Configuration }
    static member withRuns (value: rootProject) runs =
        rootProject.restore value.Id value.ProjectName value.FolderName (if value.IsSealed then value.Configuration else value.InheritedDefaults) value.LocalOverrides value.IsSealed runs
    static member create id name folderName configuration runs =
        rootProject.createInherited id name folderName configuration (Defaults.localRoot configuration) runs
    static member rebind parent (value: rootProject) =
        if value.IsSealed || parent = value.InheritedDefaults then value
        else rootProject.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs

type branchProject =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; configuration: branchProjectConfig; inheritedDefaults: branchProjectConfig; localOverrides: branchDefaultOverrides; isSealed: bool; runs: branchRun array }
    member this.IsSealed = this.isSealed
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.InheritedDefaults = this.inheritedDefaults
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ResultsFunctionName = this.configuration.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = this.configuration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.configuration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.configuration.ResultsParameterDefaults
    member this.SetupDatabaseName = this.configuration.SetupDatabaseName
    member this.SetupFunctionName = this.configuration.SetupFunctionName
    member this.SetupDataTypes = this.configuration.SetupDataTypes
    member this.SourceProjectIds = this.configuration.SourceProjectIds
    member this.SetupSortopiaDataTypes = this.configuration.SetupSortopiaDataTypes
    member this.SetupQueryParameterKeys = this.configuration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.configuration.SetupParameterDefaults
    member this.Runs = Array.copy this.runs
    interface IBranchProject with
        member this.Id = this.id
        member this.IsSealed = this.isSealed
        member this.ProjectName = this.projectName
        member this.Configuration = this.configuration
    static member private createState isSealed (id: Guid<projectId>) (name: string<projectName>) folderName parent (local: branchDefaultOverrides) (runs: branchRun array) : branchProject =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateBranch local
        let configuration = if isSealed then parent else Defaults.resolveBranch parent local
        ProjectConfiguration.validateBranch configuration
        if not isSealed then Defaults.validateBranchModule configuration
        else Sortopia.Functions.FunctionConfiguration.validateResults configuration.Results
        Sortopia.Functions.FunctionConfiguration.validateSetup true configuration.Setup
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ParentProjectId <> id) then invalidArg "runs" "Each run must reference its containing project ID."
        if runs |> Array.exists (fun r -> r.ProjectConfiguration <> configuration || r.ProjectName <> name) then
            invalidArg "configuration" "Existing runs require their original sealed project settings and name."
        let isSealed = isSealed || runs.Length > 0
        let local = if isSealed then Defaults.localBranch configuration else { local with ResultsDatabaseName = Some configuration.ResultsDatabaseName; SetupDatabaseName = Some configuration.SetupDatabaseName }
        let value = { isSealed = isSealed; id = id; projectName = name; folderName = folderName; configuration = configuration; inheritedDefaults = (if isSealed then configuration else parent); localOverrides = local; runs = [||] }
        { value with runs = runs |> Array.map (fun r -> BranchRun.create value r.Configuration) }
    static member createInherited id name folderName parent local runs =
        branchProject.createState false id name folderName parent local runs
    static member restore id name folderName configuration local isSealed runs =
        branchProject.createState isSealed id name folderName configuration (if isSealed then Defaults.localBranch configuration else local) runs
    static member seal (value: branchProject) =
        { value with isSealed = true; inheritedDefaults = value.Configuration; localOverrides = Defaults.localBranch value.Configuration }
    static member withRuns (value: branchProject) runs =
        branchProject.restore value.Id value.ProjectName value.FolderName (if value.IsSealed then value.Configuration else value.InheritedDefaults) value.LocalOverrides value.IsSealed runs
    static member create id name folderName configuration runs =
        branchProject.createInherited id name folderName configuration (Defaults.localBranch configuration) runs
    static member rebind parent (value: branchProject) =
        if value.IsSealed || parent = value.InheritedDefaults then value
        else branchProject.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs

type project =
    | RootProject of rootProject
    | BranchProject of branchProject
    member this.IsSealed = match this with RootProject p -> p.IsSealed | BranchProject p -> p.IsSealed
    member this.Kind = match this with RootProject _ -> hierarchyKind.Root | BranchProject _ -> hierarchyKind.Branch
    member this.Id = match this with RootProject p -> p.Id | BranchProject p -> p.Id
    member this.ProjectName = match this with RootProject p -> p.ProjectName | BranchProject p -> p.ProjectName
    member this.FolderName = match this with RootProject p -> p.FolderName | BranchProject p -> p.FolderName
    member this.Configuration = match this with RootProject p -> RootProjectConfig p.Configuration | BranchProject p -> BranchProjectConfig p.Configuration
    member this.InheritedDefaults = match this with RootProject p -> RootProjectConfig p.InheritedDefaults | BranchProject p -> BranchProjectConfig p.InheritedDefaults
    member this.LocalOverrides = match this with RootProject p -> RootOverrides p.LocalOverrides | BranchProject p -> BranchOverrides p.LocalOverrides
    member this.Description = match this with RootProject p -> p.Description | BranchProject p -> p.Description
    member this.ResultsDatabaseName = match this with RootProject p -> p.ResultsDatabaseName | BranchProject p -> p.ResultsDatabaseName
    member this.ResultsFunctionName = match this with RootProject p -> p.ResultsFunctionName | BranchProject p -> p.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = match this with RootProject p -> p.ResultsSortopiaDataTypes | BranchProject p -> p.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = match this with RootProject p -> p.ResultsQueryParameterKeys | BranchProject p -> p.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = match this with RootProject p -> p.ResultsParameterDefaults | BranchProject p -> p.ResultsParameterDefaults
    member this.Runs = match this with RootProject p -> p.Runs |> Array.map run.RootRun | BranchProject p -> p.Runs |> Array.map run.BranchRun
    /// Rebuilds child runs using this project's shared settings and their saved overrides.
    static member create id name folderName configuration (runs: run array) =
        if isNull runs then nullArg "runs"
        match configuration with
        | RootProjectConfig config ->
            runs |> Array.map (function run.RootRun r -> r | _ -> invalidArg "runs" "A root project can only contain root runs.")
            |> rootProject.create id name folderName config |> RootProject
        | BranchProjectConfig config ->
            runs |> Array.map (function run.BranchRun r -> r | _ -> invalidArg "runs" "A branch project can only contain branch runs.")
            |> branchProject.create id name folderName config |> BranchProject

    static member createInherited id name folderName parent local (runs: run array) =
        if isNull runs then nullArg "runs"
        match parent, local with
        | RootProjectConfig p, RootOverrides l ->
            runs |> Array.map (function run.RootRun r -> r | _ -> invalidArg "runs" "A root project can only contain root runs.")
            |> rootProject.createInherited id name folderName p l |> RootProject
        | BranchProjectConfig p, BranchOverrides l ->
            runs |> Array.map (function run.BranchRun r -> r | _ -> invalidArg "runs" "A branch project can only contain branch runs.")
            |> branchProject.createInherited id name folderName p l |> BranchProject
        | _ -> invalidArg "local" "Project defaults must match the parent's root/branch type."
    static member seal = function RootProject p -> rootProject.seal p |> RootProject | BranchProject p -> branchProject.seal p |> BranchProject
    static member restore id name folderName configuration local isSealed (runs: run array) =
        match configuration, local with
        | RootProjectConfig config, RootOverrides overrides ->
            runs |> Array.map (function run.RootRun r -> r | _ -> invalidArg "runs" "Root projects require root runs.")
            |> rootProject.restore id name folderName config overrides isSealed |> RootProject
        | BranchProjectConfig config, BranchOverrides overrides ->
            runs |> Array.map (function run.BranchRun r -> r | _ -> invalidArg "runs" "Branch projects require branch runs.")
            |> branchProject.restore id name folderName config overrides isSealed |> BranchProject
        | _ -> invalidArg "configuration" "Project configuration and overrides must have the same family."
    static member withRuns (value: project) runs =
        project.restore value.Id value.ProjectName value.FolderName (if value.IsSealed then value.Configuration else value.InheritedDefaults) value.LocalOverrides value.IsSealed runs
    static member rebind parent (value: project) =
        if value.IsSealed then value else project.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs
    member this.Snapshot =
        let results = match this.Configuration with RootProjectConfig c -> c.Results | BranchProjectConfig c -> c.Results
        { ProjectId = this.Id; ProjectName = this.ProjectName; Results = results; IsSealed = this.IsSealed }

type rootModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; configuration: rootModuleConfig; inheritedDefaults: rootModuleConfig; localOverrides: rootModuleOverrides; modules: rootModule array; projects: rootProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.InheritedDefaults = this.inheritedDefaults
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsFunctionName = this.configuration.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = this.configuration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.configuration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.configuration.ResultsParameterDefaults
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member createInherited (id: Guid<moduleId>) (name: string<moduleName>) folderName parent (local: rootModuleOverrides) (modules: rootModule array) (projects: rootProject array) : rootModule =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        ModuleDefaults.validateRoot local
        let configuration = ModuleDefaults.resolveRoot parent local
        if isNull modules then nullArg "modules"
        ModuleDefaults.validateRootConfiguration configuration
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "module and project children" (Seq.append (modules |> Seq.map (fun m -> m.FolderName)) (projects |> Seq.map (fun p -> p.FolderName)))
        { id = id; moduleName = name; folderName = folderName; configuration = configuration; inheritedDefaults = parent; localOverrides = local
          modules = modules |> Array.map (rootModule.rebind configuration)
          projects = projects |> Array.map (rootProject.rebind (ModuleDefaults.toRootProject configuration)) }
    static member create id name folderName modules projects =
        rootModule.createInherited id name folderName ModuleDefaults.root ModuleDefaults.inheritRoot modules projects
    static member rebind parent (value: rootModule) =
        if parent = value.InheritedDefaults then value
        else rootModule.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

type branchModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; configuration: branchModuleConfig; inheritedDefaults: branchModuleConfig; localOverrides: branchModuleOverrides; modules: branchModule array; projects: branchProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.InheritedDefaults = this.inheritedDefaults
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsFunctionName = this.configuration.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = this.configuration.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = this.configuration.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = this.configuration.ResultsParameterDefaults
    member this.SetupFunctionName = this.configuration.SetupFunctionName
    member this.SetupDataTypes = this.configuration.SetupDataTypes
    member this.SourceProjectIds = this.configuration.SourceProjectIds
    member this.SetupSortopiaDataTypes = this.configuration.SetupSortopiaDataTypes
    member this.SetupQueryParameterKeys = this.configuration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.configuration.SetupParameterDefaults
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member createInherited (id: Guid<moduleId>) (name: string<moduleName>) folderName parent (local: branchModuleOverrides) (modules: branchModule array) (projects: branchProject array) : branchModule =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        ModuleDefaults.validateBranch local
        let configuration = ModuleDefaults.resolveBranch parent local
        if isNull modules then nullArg "modules"
        ModuleDefaults.validateBranchConfiguration configuration
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "module and project children" (Seq.append (modules |> Seq.map (fun m -> m.FolderName)) (projects |> Seq.map (fun p -> p.FolderName)))
        { id = id; moduleName = name; folderName = folderName; configuration = configuration; inheritedDefaults = parent; localOverrides = local
          modules = modules |> Array.map (branchModule.rebind configuration)
          projects = projects |> Array.map (branchProject.rebind (ModuleDefaults.toBranchProject configuration)) }
    static member create id name folderName modules projects =
        branchModule.createInherited id name folderName ModuleDefaults.branch ModuleDefaults.inheritBranch modules projects
    static member rebind parent (value: branchModule) =
        if parent = value.InheritedDefaults then value
        else branchModule.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

/// A workspace can hold either family; each concrete module has homogeneous children.
[<RequireQualifiedAccess>]
type workspaceModule =
    | Root of rootModule
    | Branch of branchModule
    member this.Kind = match this with workspaceModule.Root _ -> hierarchyKind.Root | workspaceModule.Branch _ -> hierarchyKind.Branch
    member this.Id = match this with workspaceModule.Root m -> m.Id | workspaceModule.Branch m -> m.Id
    member this.ModuleName = match this with workspaceModule.Root m -> m.ModuleName | workspaceModule.Branch m -> m.ModuleName
    member this.FolderName = match this with workspaceModule.Root m -> m.FolderName | workspaceModule.Branch m -> m.FolderName
    member this.Configuration = match this with workspaceModule.Root m -> RootModuleConfig m.Configuration | workspaceModule.Branch m -> BranchModuleConfig m.Configuration
    member this.InheritedDefaults = match this with workspaceModule.Root m -> RootModuleConfig m.InheritedDefaults | workspaceModule.Branch m -> BranchModuleConfig m.InheritedDefaults
    member this.LocalOverrides = match this with workspaceModule.Root m -> RootModuleOverrides m.LocalOverrides | workspaceModule.Branch m -> BranchModuleOverrides m.LocalOverrides
    member this.Description = match this with workspaceModule.Root m -> m.Description | workspaceModule.Branch m -> m.Description
    member this.ResultsFunctionName = match this with workspaceModule.Root m -> m.ResultsFunctionName | workspaceModule.Branch m -> m.ResultsFunctionName
    member this.ResultsSortopiaDataTypes = match this with workspaceModule.Root m -> m.ResultsSortopiaDataTypes | workspaceModule.Branch m -> m.ResultsSortopiaDataTypes
    member this.ResultsQueryParameterKeys = match this with workspaceModule.Root m -> m.ResultsQueryParameterKeys | workspaceModule.Branch m -> m.ResultsQueryParameterKeys
    member this.ResultsParameterDefaults = match this with workspaceModule.Root m -> m.ResultsParameterDefaults | workspaceModule.Branch m -> m.ResultsParameterDefaults
    member this.Modules = match this with workspaceModule.Root m -> m.Modules |> Array.map workspaceModule.Root | workspaceModule.Branch m -> m.Modules |> Array.map workspaceModule.Branch
    member this.Projects = match this with workspaceModule.Root m -> m.Projects |> Array.map project.RootProject | workspaceModule.Branch m -> m.Projects |> Array.map project.BranchProject
    static member create kind id name folderName (modules: workspaceModule array) (projects: project array) =
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        match kind with
        | hierarchyKind.Root ->
            let children = modules |> Array.map (function workspaceModule.Root m -> m | _ -> invalidArg "modules" "A root module can only contain root modules.")
            let leaves = projects |> Array.map (function project.RootProject p -> p | _ -> invalidArg "projects" "A root module can only contain root projects.")
            rootModule.create id name folderName children leaves |> workspaceModule.Root
        | hierarchyKind.Branch ->
            let children = modules |> Array.map (function workspaceModule.Branch m -> m | _ -> invalidArg "modules" "A branch module can only contain branch modules.")
            let leaves = projects |> Array.map (function project.BranchProject p -> p | _ -> invalidArg "projects" "A branch module can only contain branch projects.")
            branchModule.create id name folderName children leaves |> workspaceModule.Branch
        | _ -> invalidArg "kind" "Module kind must be root or branch."

    static member createInherited id name folderName parent local (modules: workspaceModule array) (projects: project array) =
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        match parent, local with
        | RootModuleConfig p, RootModuleOverrides l ->
            let children = modules |> Array.map (function workspaceModule.Root m -> m | _ -> invalidArg "modules" "A root module can only contain root modules.")
            let leaves = projects |> Array.map (function project.RootProject x -> x | _ -> invalidArg "projects" "A root module can only contain root projects.")
            rootModule.createInherited id name folderName p l children leaves |> workspaceModule.Root
        | BranchModuleConfig p, BranchModuleOverrides l ->
            let children = modules |> Array.map (function workspaceModule.Branch m -> m | _ -> invalidArg "modules" "A branch module can only contain branch modules.")
            let leaves = projects |> Array.map (function project.BranchProject x -> x | _ -> invalidArg "projects" "A branch module can only contain branch projects.")
            branchModule.createInherited id name folderName p l children leaves |> workspaceModule.Branch
        | _ -> invalidArg "local" "Module defaults must match the parent's root/branch type."
    static member rebind parent (value: workspaceModule) =
        workspaceModule.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

module WorkspaceProjects =
    let rec inModule (value: workspaceModule) = seq {
        yield! value.Projects
        for child in value.Modules do yield! inModule child }
    let index (modules: workspaceModule array) =
        let rec moduleIds (values: workspaceModule array) = seq {
            for value in values do
                yield UMX.untag value.Id
                yield! moduleIds value.Modules }
        EntityId.unique "modules" (moduleIds modules)
        let projects = modules |> Seq.collect inModule |> Seq.toArray
        EntityId.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.Id))
        EntityId.unique "runs" (projects |> Seq.collect _.Runs |> Seq.map (fun r -> UMX.untag r.Id))
        projects |> Seq.map (fun p -> p.Id, p) |> Map.ofSeq
    let resolve (modules: workspaceModule array) =
        let projects = index modules
        let find id = projects |> Map.tryFind id |> Option.map (fun p -> p.Snapshot)
        let validateSource isSealed (entry: setupDataTypeConfig) =
            let source = Map.tryFind entry.SourceProjectId projects |> Option.defaultWith (fun () -> invalidArg "sourceProjectId" $"Source project '{entry.SourceProjectId}' was not found.")
            if not (List.contains entry.DataType source.ResultsSortopiaDataTypes) then invalidArg "sourceProjectId" $"Source project '{source.ProjectName}' does not provide '{entry.DataType}'."
            if isSealed && not source.IsSealed then invalidArg "sourceProjectId" $"Source project '{source.ProjectName}' must be sealed before this project can create runs."
            source
        let rec validateModule (m: workspaceModule) =
            match m.Configuration with
            | BranchModuleConfig config -> config.SetupDataTypes |> List.filter (fun entry -> UMX.untag entry.SourceProjectId <> System.Guid.Empty) |> List.iter (validateSource false >> ignore)
            | RootModuleConfig _ -> ()
            m.Modules |> Array.iter validateModule
        modules |> Array.iter validateModule
        let dependencies = projects |> Map.map (fun _ p ->
            match p.Configuration with
            | RootProjectConfig _ -> []
            | BranchProjectConfig config ->
                config.SetupDataTypes |> List.filter (fun entry -> UMX.untag entry.SourceProjectId <> System.Guid.Empty) |> List.map (fun entry -> (validateSource p.IsSealed entry).Id) |> List.distinct)
        // Topological validation avoids recursive stack growth for long source chains.
        let incoming = Collections.Generic.Dictionary<Guid<projectId>, int>()
        projects |> Map.iter (fun id _ -> incoming[id] <- 0)
        dependencies |> Map.iter (fun _ sources -> sources |> List.iter (fun id -> incoming[id] <- incoming[id] + 1))
        let ready = Collections.Generic.Queue<Guid<projectId>>()
        for KeyValue(id, count) in incoming do if count = 0 then ready.Enqueue id
        let mutable processed = 0
        while ready.Count > 0 do
            let id = ready.Dequeue()
            processed <- processed + 1
            for source in dependencies[id] do
                incoming[source] <- incoming[source] - 1
                if incoming[source] = 0 then ready.Enqueue source
        if processed <> projects.Count then invalidArg "sourceProjectId" "Source-project dependencies must not contain cycles."
        let rec root (m: rootModule) = { m with modules = m.Modules |> Array.map root }
        let rec branch (m: branchModule) =
            { m with modules = m.Modules |> Array.map branch
                     projects = m.Projects |> Array.map (fun p -> { p with runs = p.Runs |> Array.map (BranchRunSources.resolve find) }) }
        modules |> Array.map (function workspaceModule.Root m -> root m |> workspaceModule.Root | workspaceModule.Branch m -> branch m |> workspaceModule.Branch)

type workspace =
    private { id: Guid<workspaceId>; workspaceName: string<workspaceName>; folderName: string; modules: workspaceModule array }
    member this.Id = this.id
    member this.WorkspaceName = this.workspaceName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    static member create (id: Guid<workspaceId>) (name: string<workspaceName>) (folderName: string) (modules: workspaceModule array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        { id = id; workspaceName = name; folderName = folderName
          modules = modules |> Array.map (fun m -> workspaceModule.rebind (ModuleDefaults.configuration (m.Kind = hierarchyKind.Branch)) m) |> WorkspaceProjects.resolve }
    member this.ProjectIndex = WorkspaceProjects.index this.modules
    member this.FindProject id = Map.tryFind id this.ProjectIndex
