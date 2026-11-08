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


type project =
    private { id: Guid<projectId>; projectName: string<projectName>; folderName: string; runs: run array; projects: project array }
    member this.Id = this.id
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Runs = Array.copy this.runs
    member this.Projects = Array.copy this.projects
    static member create (id: Guid<projectId>) (name: string<projectName>) (folderName: string) (runs: run array) (projects: project array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull runs then nullArg "runs"
        if isNull projects then nullArg "projects"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        EntityId.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.Id))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then
            invalidArg "runs" "Each run must name its containing project."
        { id = id; projectName = name; folderName = folderName; runs = Array.copy runs; projects = Array.copy projects }

/// Escaped because module is an F# keyword; its CLR type name is module.
type ``module`` =
    private { id: Guid<moduleId>; moduleName: string<moduleName>; folderName: string; modules: ``module`` array; projects: project array }
    member this.Id = this.id
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member create (id: Guid<moduleId>) (name: string<moduleName>) (folderName: string) (modules: ``module`` array) (projects: project array) =
        EntityId.validate (UMX.untag id)
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { id = id; moduleName = name; folderName = folderName; modules = Array.copy modules; projects = Array.copy projects }

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
