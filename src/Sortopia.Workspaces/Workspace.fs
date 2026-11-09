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
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; configuration: rootProjectConfig; localOverrides: rootDefaultOverrides; runs: rootRun array }
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.configuration.ExecuteFunctionName
    member this.OutputDataTypes = this.configuration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.configuration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.configuration.OutputParameterDefaults
    member this.Runs = Array.copy this.runs
    interface IRootProject with
        member this.ProjectName = this.projectName
        member this.Configuration = this.configuration
    static member createInherited (id: Guid<projectId>) (name: string<projectName>) folderName parent (local: rootDefaultOverrides) (runs: rootRun array) : rootProject =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateRoot local
        let configuration = Defaults.resolveRoot parent local
        ProjectConfiguration.validateRoot configuration
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then invalidArg "runs" "Each run must name its containing project."
        let value = { id = id; projectName = name; folderName = folderName; configuration = configuration; localOverrides = local; runs = [||] }
        { value with runs = runs |> Array.map (fun r -> RootRun.create value r.Configuration) }
    static member create id name folderName configuration runs =
        rootProject.createInherited id name folderName Defaults.root (Defaults.localRoot configuration) runs
    static member rebind parent (value: rootProject) =
        if Defaults.resolveRoot parent value.LocalOverrides = value.Configuration then value
        else rootProject.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs

type branchProject =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; configuration: branchProjectConfig; localOverrides: branchDefaultOverrides; runs: branchRun array }
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.configuration.ExecuteFunctionName
    member this.OutputDataTypes = this.configuration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.configuration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.configuration.OutputParameterDefaults
    member this.SetupDatabaseName = this.configuration.SetupDatabaseName
    member this.SetupFunctionName = this.configuration.SetupFunctionName
    member this.SetupOutputDataTypes = this.configuration.SetupOutputDataTypes
    member this.SetupQueryParameterKeys = this.configuration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.configuration.SetupParameterDefaults
    member this.Runs = Array.copy this.runs
    interface IBranchProject with
        member this.ProjectName = this.projectName
        member this.Configuration = this.configuration
    static member createInherited (id: Guid<projectId>) (name: string<projectName>) folderName parent (local: branchDefaultOverrides) (runs: branchRun array) : branchProject =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateBranch local
        let configuration = Defaults.resolveBranch parent local
        ProjectConfiguration.validateBranch configuration
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then invalidArg "runs" "Each run must name its containing project."
        let value = { id = id; projectName = name; folderName = folderName; configuration = configuration; localOverrides = local; runs = [||] }
        { value with runs = runs |> Array.map (fun r -> BranchRun.create value r.Configuration) }
    static member create id name folderName configuration runs =
        branchProject.createInherited id name folderName Defaults.branch (Defaults.localBranch configuration) runs
    static member rebind parent (value: branchProject) =
        if Defaults.resolveBranch parent value.LocalOverrides = value.Configuration then value
        else branchProject.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs

type project =
    | RootProject of rootProject
    | BranchProject of branchProject
    member this.Kind = match this with RootProject _ -> hierarchyKind.Root | BranchProject _ -> hierarchyKind.Branch
    member this.Id = match this with RootProject p -> p.Id | BranchProject p -> p.Id
    member this.ProjectName = match this with RootProject p -> p.ProjectName | BranchProject p -> p.ProjectName
    member this.FolderName = match this with RootProject p -> p.FolderName | BranchProject p -> p.FolderName
    member this.Configuration = match this with RootProject p -> RootProjectConfig p.Configuration | BranchProject p -> BranchProjectConfig p.Configuration
    member this.LocalOverrides = match this with RootProject p -> RootOverrides p.LocalOverrides | BranchProject p -> BranchOverrides p.LocalOverrides
    member this.Description = match this with RootProject p -> p.Description | BranchProject p -> p.Description
    member this.ResultsDatabaseName = match this with RootProject p -> p.ResultsDatabaseName | BranchProject p -> p.ResultsDatabaseName
    member this.ExecuteFunctionName = match this with RootProject p -> p.ExecuteFunctionName | BranchProject p -> p.ExecuteFunctionName
    member this.OutputDataTypes = match this with RootProject p -> p.OutputDataTypes | BranchProject p -> p.OutputDataTypes
    member this.OutputQueryParameterKeys = match this with RootProject p -> p.OutputQueryParameterKeys | BranchProject p -> p.OutputQueryParameterKeys
    member this.OutputParameterDefaults = match this with RootProject p -> p.OutputParameterDefaults | BranchProject p -> p.OutputParameterDefaults
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
    static member rebind parent (value: project) =
        project.createInherited value.Id value.ProjectName value.FolderName parent value.LocalOverrides value.Runs

type rootModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; configuration: rootProjectConfig; localOverrides: rootDefaultOverrides; modules: rootModule array; projects: rootProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.configuration.ExecuteFunctionName
    member this.OutputDataTypes = this.configuration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.configuration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.configuration.OutputParameterDefaults
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member createInherited (id: Guid<moduleId>) (name: string<moduleName>) folderName parent (local: rootDefaultOverrides) (modules: rootModule array) (projects: rootProject array) : rootModule =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateRoot local
        let configuration = Defaults.resolveRoot parent local
        if isNull modules then nullArg "modules"
        Defaults.validateRootModule configuration
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { id = id; moduleName = name; folderName = folderName; configuration = configuration; localOverrides = local
          modules = modules |> Array.map (rootModule.rebind configuration)
          projects = projects |> Array.map (rootProject.rebind configuration) }
    static member create id name folderName modules projects =
        rootModule.createInherited id name folderName Defaults.root Defaults.inheritRoot modules projects
    static member rebind parent (value: rootModule) =
        if Defaults.resolveRoot parent value.LocalOverrides = value.Configuration then value
        else rootModule.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

type branchModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; configuration: branchProjectConfig; localOverrides: branchDefaultOverrides; modules: branchModule array; projects: branchProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Configuration = this.configuration
    member this.LocalOverrides = this.localOverrides
    member this.Description = this.configuration.Description
    member this.ResultsDatabaseName = this.configuration.ResultsDatabaseName
    member this.ExecuteFunctionName = this.configuration.ExecuteFunctionName
    member this.OutputDataTypes = this.configuration.OutputDataTypes
    member this.OutputQueryParameterKeys = this.configuration.OutputQueryParameterKeys
    member this.OutputParameterDefaults = this.configuration.OutputParameterDefaults
    member this.SetupDatabaseName = this.configuration.SetupDatabaseName
    member this.SetupFunctionName = this.configuration.SetupFunctionName
    member this.SetupOutputDataTypes = this.configuration.SetupOutputDataTypes
    member this.SetupQueryParameterKeys = this.configuration.SetupQueryParameterKeys
    member this.SetupParameterDefaults = this.configuration.SetupParameterDefaults
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member createInherited (id: Guid<moduleId>) (name: string<moduleName>) folderName parent (local: branchDefaultOverrides) (modules: branchModule array) (projects: branchProject array) : branchModule =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        Defaults.validateBranch local
        let configuration = Defaults.resolveBranch parent local
        if isNull modules then nullArg "modules"
        Defaults.validateBranchModule configuration
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { id = id; moduleName = name; folderName = folderName; configuration = configuration; localOverrides = local
          modules = modules |> Array.map (branchModule.rebind configuration)
          projects = projects |> Array.map (branchProject.rebind configuration) }
    static member create id name folderName modules projects =
        branchModule.createInherited id name folderName Defaults.branch Defaults.inheritBranch modules projects
    static member rebind parent (value: branchModule) =
        if Defaults.resolveBranch parent value.LocalOverrides = value.Configuration then value
        else branchModule.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

/// A workspace can hold either family; each concrete module has homogeneous children.
type ``module`` =
    | RootModule of rootModule
    | BranchModule of branchModule
    member this.Kind = match this with RootModule _ -> hierarchyKind.Root | BranchModule _ -> hierarchyKind.Branch
    member this.Id = match this with RootModule m -> m.Id | BranchModule m -> m.Id
    member this.ModuleName = match this with RootModule m -> m.ModuleName | BranchModule m -> m.ModuleName
    member this.FolderName = match this with RootModule m -> m.FolderName | BranchModule m -> m.FolderName
    member this.Configuration = match this with RootModule m -> RootProjectConfig m.Configuration | BranchModule m -> BranchProjectConfig m.Configuration
    member this.LocalOverrides = match this with RootModule m -> RootOverrides m.LocalOverrides | BranchModule m -> BranchOverrides m.LocalOverrides
    member this.Description = match this with RootModule m -> m.Description | BranchModule m -> m.Description
    member this.ResultsDatabaseName = match this with RootModule m -> m.ResultsDatabaseName | BranchModule m -> m.ResultsDatabaseName
    member this.ExecuteFunctionName = match this with RootModule m -> m.ExecuteFunctionName | BranchModule m -> m.ExecuteFunctionName
    member this.OutputDataTypes = match this with RootModule m -> m.OutputDataTypes | BranchModule m -> m.OutputDataTypes
    member this.OutputQueryParameterKeys = match this with RootModule m -> m.OutputQueryParameterKeys | BranchModule m -> m.OutputQueryParameterKeys
    member this.OutputParameterDefaults = match this with RootModule m -> m.OutputParameterDefaults | BranchModule m -> m.OutputParameterDefaults
    member this.Modules = match this with RootModule m -> m.Modules |> Array.map RootModule | BranchModule m -> m.Modules |> Array.map BranchModule
    member this.Projects = match this with RootModule m -> m.Projects |> Array.map project.RootProject | BranchModule m -> m.Projects |> Array.map project.BranchProject
    static member create kind id name folderName (modules: ``module`` array) (projects: project array) =
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        match kind with
        | hierarchyKind.Root ->
            let children = modules |> Array.map (function RootModule m -> m | _ -> invalidArg "modules" "A root module can only contain root modules.")
            let leaves = projects |> Array.map (function project.RootProject p -> p | _ -> invalidArg "projects" "A root module can only contain root projects.")
            rootModule.create id name folderName children leaves |> RootModule
        | hierarchyKind.Branch ->
            let children = modules |> Array.map (function BranchModule m -> m | _ -> invalidArg "modules" "A branch module can only contain branch modules.")
            let leaves = projects |> Array.map (function project.BranchProject p -> p | _ -> invalidArg "projects" "A branch module can only contain branch projects.")
            branchModule.create id name folderName children leaves |> BranchModule
        | _ -> invalidArg "kind" "Module kind must be root or branch."

    static member createInherited id name folderName parent local (modules: ``module`` array) (projects: project array) =
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        match parent, local with
        | RootProjectConfig p, RootOverrides l ->
            let children = modules |> Array.map (function RootModule m -> m | _ -> invalidArg "modules" "A root module can only contain root modules.")
            let leaves = projects |> Array.map (function project.RootProject x -> x | _ -> invalidArg "projects" "A root module can only contain root projects.")
            rootModule.createInherited id name folderName p l children leaves |> RootModule
        | BranchProjectConfig p, BranchOverrides l ->
            let children = modules |> Array.map (function BranchModule m -> m | _ -> invalidArg "modules" "A branch module can only contain branch modules.")
            let leaves = projects |> Array.map (function project.BranchProject x -> x | _ -> invalidArg "projects" "A branch module can only contain branch projects.")
            branchModule.createInherited id name folderName p l children leaves |> BranchModule
        | _ -> invalidArg "local" "Module defaults must match the parent's root/branch type."
    static member rebind parent (value: ``module``) =
        ``module``.createInherited value.Id value.ModuleName value.FolderName parent value.LocalOverrides value.Modules value.Projects

type workspace =
    private { id: Guid<workspaceId>; workspaceName: string<workspaceName>; folderName: string; modules: ``module`` array }
    member this.Id = this.id
    member this.WorkspaceName = this.workspaceName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    static member create (id: Guid<workspaceId>) (name: string<workspaceName>) (folderName: string) (modules: ``module`` array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        { id = id; workspaceName = name; folderName = folderName
          modules = modules |> Array.map (fun m -> ``module``.rebind (Defaults.configuration (m.Kind = hierarchyKind.Branch)) m) }
