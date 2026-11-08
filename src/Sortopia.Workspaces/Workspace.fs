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
    let private rebuild (projectName: string<projectName>) (runName: string<runName>) (value: run) =
        match value with
        | run.RootRun root ->
            RootRun.create {
                ProjectName = projectName; RunName = runName; Description = root.Description
                Id = value.Id
                ResultsDatabaseName = root.ResultsDatabaseName; ExecuteFunctionName = root.ExecuteFunctionName
                OutputParameterSpans = root.OutputParameterSpans; OutputDataTypes = root.OutputDataTypes
                OutputQueryParameterKeys = root.OutputQueryParameterKeys } |> run.RootRun
        | run.BranchRun branch ->
            BranchRun.create {
                ProjectName = projectName; RunName = runName; Description = branch.Description
                Id = value.Id
                ResultsDatabaseName = branch.ResultsDatabaseName; ExecuteFunctionName = branch.ExecuteFunctionName
                OutputParameterSpans = branch.OutputParameterSpans; OutputDataTypes = branch.OutputDataTypes
                OutputQueryParameterKeys = branch.OutputQueryParameterKeys
                SetupDatabaseName = branch.SetupDatabaseName; SetupFunctionName = branch.SetupFunctionName
                SetupParameterSpans = branch.SetupParameterSpans; SetupOutputDataTypes = branch.SetupOutputDataTypes
                SetupQueryParameterKeys = branch.SetupQueryParameterKeys } |> run.BranchRun

    /// Renaming a project or run preserves its ID and complete execution/output configuration.
    let withNames projectName runName (value: run) =
        rebuild projectName runName value


/// The execution family shared by a module, its projects, and their runs.
type hierarchyKind =
    | Root = 0
    | Branch = 1

type rootProject =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; runs: rootRun array }
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Runs = Array.copy this.runs
    static member create (id: Guid<projectId>) (name: string<projectName>) folderName (runs: rootRun array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then
            invalidArg "runs" "Each run must name its containing project."
        { id = id; projectName = name; folderName = folderName; runs = Array.copy runs }

type branchProject =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; runs: branchRun array }
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Runs = Array.copy this.runs
    static member create (id: Guid<projectId>) (name: string<projectName>) folderName (runs: branchRun array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull runs then nullArg "runs"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then
            invalidArg "runs" "Each run must name its containing project."
        { id = id; projectName = name; folderName = folderName; runs = Array.copy runs }

type project =
    | RootProject of rootProject
    | BranchProject of branchProject
    member this.Kind = match this with RootProject _ -> hierarchyKind.Root | BranchProject _ -> hierarchyKind.Branch
    member this.Id = match this with RootProject p -> p.Id | BranchProject p -> p.Id
    member this.ProjectName = match this with RootProject p -> p.ProjectName | BranchProject p -> p.ProjectName
    member this.FolderName = match this with RootProject p -> p.FolderName | BranchProject p -> p.FolderName
    member this.Runs = match this with RootProject p -> p.Runs |> Array.map run.RootRun | BranchProject p -> p.Runs |> Array.map run.BranchRun
    /// Validates untyped input at serialization and UI boundaries.
    static member create kind id name folderName (runs: run array) =
        if isNull runs then nullArg "runs"
        match kind with
        | hierarchyKind.Root ->
            runs |> Array.map (function run.RootRun r -> r | _ -> invalidArg "runs" "A root project can only contain root runs.")
            |> rootProject.create id name folderName |> RootProject
        | hierarchyKind.Branch ->
            runs |> Array.map (function run.BranchRun r -> r | _ -> invalidArg "runs" "A branch project can only contain branch runs.")
            |> branchProject.create id name folderName |> BranchProject
        | _ -> invalidArg "kind" "Project kind must be root or branch."

type rootModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; modules: rootModule array; projects: rootProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member create (id: Guid<moduleId>) (name: string<moduleName>) folderName (modules: rootModule array) (projects: rootProject array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> p.ProjectName |> UMX.untag))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { id = id; moduleName = name; folderName = folderName; modules = Array.copy modules; projects = Array.copy projects }

type branchModule =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; modules: branchModule array; projects: branchProject array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member create (id: Guid<moduleId>) (name: string<moduleName>) folderName (modules: branchModule array) (projects: branchProject array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> p.ProjectName |> UMX.untag))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { id = id; moduleName = name; folderName = folderName; modules = Array.copy modules; projects = Array.copy projects }

/// A workspace can hold either family; each concrete module has homogeneous children.
type ``module`` =
    | RootModule of rootModule
    | BranchModule of branchModule
    member this.Kind = match this with RootModule _ -> hierarchyKind.Root | BranchModule _ -> hierarchyKind.Branch
    member this.Id = match this with RootModule m -> m.Id | BranchModule m -> m.Id
    member this.ModuleName = match this with RootModule m -> m.ModuleName | BranchModule m -> m.ModuleName
    member this.FolderName = match this with RootModule m -> m.FolderName | BranchModule m -> m.FolderName
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
          modules = Array.copy modules }
