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

module WorkspaceRun =
    let private rebuild (projectName: string<projectName>) (runName: string<runName>) folderName (value: run) =
        match value with
        | run.RootRun root ->
            RootRun.create {
                ProjectName = projectName; RunName = runName; Description = root.Description
                FolderName = folderName
                ResultsDatabaseName = root.ResultsDatabaseName; ExecuteFunctionName = root.ExecuteFunctionName
                OutputParameterSpans = root.OutputParameterSpans; OutputDataTypes = root.OutputDataTypes
                OutputQueryParameterKeys = root.OutputQueryParameterKeys } |> run.RootRun
        | run.BranchRun branch ->
            BranchRun.create {
                ProjectName = projectName; RunName = runName; Description = branch.Description
                FolderName = folderName
                ResultsDatabaseName = branch.ResultsDatabaseName; ExecuteFunctionName = branch.ExecuteFunctionName
                OutputParameterSpans = branch.OutputParameterSpans; OutputDataTypes = branch.OutputDataTypes
                OutputQueryParameterKeys = branch.OutputQueryParameterKeys
                SetupDatabaseName = branch.SetupDatabaseName; SetupFunctionName = branch.SetupFunctionName
                SetupParameterSpans = branch.SetupParameterSpans; SetupOutputDataTypes = branch.SetupOutputDataTypes
                SetupQueryParameterKeys = branch.SetupQueryParameterKeys } |> run.BranchRun

    /// Renaming a project or run preserves its folder and complete execution/output configuration.
    let withNames projectName runName (value: run) =
        rebuild projectName runName value.FolderName value

    /// Import a folder location while retaining the run's saved display name and configuration.
    let withLocation projectName folderName (value: run) =
        rebuild projectName value.RunName folderName value

type project =
    private { projectName: string<projectName>; folderName: string; runs: run array; projects: project array }
    member this.ProjectName = this.projectName
    member this.FolderName = this.folderName
    member this.Runs = Array.copy this.runs
    member this.Projects = Array.copy this.projects
    static member create (name: string<projectName>) (folderName: string) (runs: run array) (projects: project array) =
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull runs then nullArg "runs"
        if isNull projects then nullArg "projects"
        EntityName.unique "runs" (runs |> Seq.map (fun r -> UMX.untag r.RunName))
        FolderName.unique "runs" (runs |> Seq.map (fun r -> r.FolderName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        if runs |> Array.exists (fun r -> r.ProjectName <> name) then
            invalidArg "runs" "Each run must name its containing project."
        { projectName = name; folderName = folderName; runs = Array.copy runs; projects = Array.copy projects }

/// Escaped because module is an F# keyword; its CLR type name is module.
type ``module`` =
    private { moduleName: string<moduleName>; folderName: string; modules: ``module`` array; projects: project array }
    member this.ModuleName = this.moduleName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    member this.Projects = Array.copy this.projects
    static member create (name: string<moduleName>) (folderName: string) (modules: ``module`` array) (projects: project array) =
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        if isNull projects then nullArg "projects"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        EntityName.unique "projects" (projects |> Seq.map (fun p -> UMX.untag p.ProjectName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        FolderName.unique "projects" (projects |> Seq.map (fun p -> p.FolderName))
        { moduleName = name; folderName = folderName; modules = Array.copy modules; projects = Array.copy projects }

type workspace =
    private { workspaceName: string<workspaceName>; folderName: string; modules: ``module`` array }
    member this.WorkspaceName = this.workspaceName
    member this.FolderName = this.folderName
    member this.Modules = Array.copy this.modules
    static member create (name: string<workspaceName>) (folderName: string) (modules: ``module`` array) =
        EntityName.validate (UMX.untag name) |> ignore
        FolderName.validate folderName |> ignore
        if isNull modules then nullArg "modules"
        EntityName.unique "modules" (modules |> Seq.map (fun m -> UMX.untag m.ModuleName))
        FolderName.unique "modules" (modules |> Seq.map (fun m -> m.FolderName))
        { workspaceName = name; folderName = folderName
          modules = Array.copy modules }
