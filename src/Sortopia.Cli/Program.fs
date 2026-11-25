namespace Sortopia.Cli

open System
open System.Threading
open FSharp.UMX
open Sortopia.FileDb
open Sortopia.Functions
open Sortopia.Workspaces.Persistence
open Sortopia.Workspaces.Serialization

module Program =
    let private help = """Sortopia results execution
  sortopia generate-parameters --workspace <folder> --run <run-id>
  sortopia list-parameters --parameters-folder <project/runParameters> [--run <run-id>]
  sortopia check-results --parameters-folder <project/runParameters> [--run <run-id>]
  sortopia execute-results --parameters-folder <project/runParameters> [--run <run-id>] [--jobs <count>]
  sortopia execute-results --parameters <path-to-work-item.msgpack> [--jobs <count>]
  sortopia export-sorter-eval --parameters-folder <project/runParameters> [--run <run-id>] [--output <report-folder>]
  sortopia export-sorter-eval --parameters <path-to-work-item.msgpack> [--output <report-folder>]

Generate locally (including setup), then copy the complete project folder.
Folder execution considers every parameter file present, including older files;
--run restricts it to one saved run ID. Complete outputs are validated and skipped;
partial work reuses existing outputs. No manifests or execution receipts are needed.
Results execution never runs setup. --jobs is a positive maximum concurrent job count (default 1).
Selected files must own distinct outputs. Failed jobs are reported while other jobs continue.
Export combines saved branch evaluations or sampled SGD population statistics.
Details includes V1 statistics for each network, results parameters, and inherited setup query metadata.
Ordinary evaluations export Summary.csv and Details.csv. SGD runs export only EvolutionSummary.csv from saved population summaries.
Reports go directly to the project's data/results/reports by default, replacing the previous CSV export.
Ctrl+C cancels running jobs cooperatively and leaves unstarted files pending.
Exit codes: 0 completed, 1 failed, 2 invalid command, 130 cancelled."""

    let private options (args: string array) allowed =
        if args.Length % 2 <> 0 then invalidArg "arguments" "Each option requires a value."
        let pairs = args |> Array.chunkBySize 2 |> Array.map (fun pair -> pair[0], pair[1])
        if pairs |> Array.exists (fun (key, _) -> not (List.contains key allowed)) then invalidArg "arguments" "Unknown option. Use --help for syntax."
        if (pairs |> Array.map fst |> Array.distinct).Length <> pairs.Length then invalidArg "arguments" "Supply each option only once."
        Map.ofArray pairs
    let private required key values =
        values |> Map.tryFind key |> Option.filter (String.IsNullOrWhiteSpace >> not)
        |> Option.defaultWith (fun () -> invalidArg "arguments" $"Missing {key}.")
    let private guid (text: string) =
        match Guid.TryParse text with
        | true, id when id <> Guid.Empty -> id
        | _ -> invalidArg "arguments" $"Expected a nonempty GUID: {text}"
    let private runFilter values = values |> Map.tryFind "--run" |> Option.map (guid >> UMX.tag)
    let private executionOptions (values: Map<string, string>) =
        match values |> Map.tryFind "--jobs" with
        | None -> resultsExecutionOptions.Default
        | Some text ->
            match Int32.TryParse text with
            | true, count when count > 0 -> { MaxParallelJobs = count }
            | _ -> invalidArg "arguments" "--jobs requires a positive whole number."
    let private finish (report: runExecutionReport) =
        printfn "%A: %d setup sets, %d results work items processed." report.Status report.Setup.Length report.Results.Length
        if report.Error <> "" then eprintfn "%s" report.Error
        match report.Status with runExecutionStatus.Completed -> 0 | runExecutionStatus.Cancelled -> 130 | _ -> 1

    [<EntryPoint>]
    let main args =
        if args.Length = 0 || args = [| "--help" |] || args = [| "-h" |] then printfn "%s" help; 0
        else
            use cancellation = new CancellationTokenSource()
            let cancelHandler = ConsoleCancelEventHandler(fun _ event -> event.Cancel <- true; cancellation.Cancel())
            Console.CancelKeyPress.AddHandler cancelHandler
            use subscription = { new IDisposable with member _.Dispose() = Console.CancelKeyPress.RemoveHandler cancelHandler }
            let progress = { new IProgress<runExecutionProgress> with member _.Report value = printfn "%s (%d/%d)" value.Message value.Completed value.Total }
            try
                match args[0] with
                | "generate-parameters" ->
                    let values = options args[1..] ["--workspace"; "--run"]
                    let folder = required "--workspace" values
                    let id = required "--run" values |> guid
                    let generated = WorkspaceRunGeneration.generate (WorkspaceFileStore.LoadWorkspace folder) id progress cancellation.Token
                    printfn "Published parameter files: %d\nProject folder: %s" generated.Report.WorkItemIds.Length (WorkspaceData.scopedProjectFolder generated.Scope folder generated.Scope.Run.ParentProjectId)
                    finish generated.Report.Execution
                | "list-parameters" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"]
                    let items = RunParameterFiles.listWorkItems (required "--parameters-folder" values) cancellation.Token
                    let filter = runFilter values
                    for item in items |> Array.filter (fun item -> filter |> Option.forall ((=) item.RunId)) do
                        printfn "%O  run %O  %s  set %d" item.Id item.RunId (string item.Results.FunctionName) item.SetNumber
                    0
                | "check-results" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"]
                    let statuses = RunParameterExecution.inspectFolder (required "--parameters-folder" values) (runFilter values) cancellation.Token
                    for item in statuses do
                        printfn "%O  run %O  %A  %d/%d valid outputs" item.Id item.RunId item.Outputs.State item.Outputs.Available.Length item.Outputs.ExpectedCount
                        if item.Outputs.Error <> "" then eprintfn "%s" item.Outputs.Error
                    if statuses |> Array.exists (fun item -> item.Outputs.State = outputState.Invalid) then 1 else 0
                | "execute-results" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"; "--parameters"; "--jobs"]
                    let execution = executionOptions values
                    match Map.tryFind "--parameters-folder" values, Map.tryFind "--parameters" values with
                    | Some folder, None -> RunParameterExecution.executeFolderWithOptions folder (runFilter values) execution progress cancellation.Token |> finish
                    | None, Some file when not (Map.containsKey "--run" values) -> RunParameterExecution.executeFileWithOptions file execution progress cancellation.Token |> finish
                    | _ -> invalidArg "arguments" "Choose --parameters-folder (optionally --run), or one --parameters file."
                | "export-sorter-eval" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"; "--parameters"; "--output"]
                    let output folder = Map.tryFind "--output" values |> Option.defaultWith (fun () -> SorterEvalReportExport.nextOutputFolder folder)
                    let exported =
                        match Map.tryFind "--parameters-folder" values, Map.tryFind "--parameters" values with
                        | Some folder, None -> SorterEvalReportExport.exportFolder folder (runFilter values) (output folder) cancellation.Token
                        | None, Some file when not (Map.containsKey "--run" values) -> SorterEvalReportExport.exportFiles [|file|] (output (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath file))) cancellation.Token
                        | _ -> invalidArg "arguments" "Choose --parameters-folder (optionally --run), or one --parameters file."
                    if exported.SummaryPath.IsSome then
                        printfn "Exported %d summary rows and %d detail rows.\n%s\n%s" exported.SummaryRows exported.DetailRows exported.SummaryPath.Value exported.DetailsPath.Value
                    if exported.EvolutionSummaryPath.IsSome then
                        printfn "Exported %d evolution summary rows.\n%s" exported.EvolutionSummaryRows exported.EvolutionSummaryPath.Value
                    0
                | _ -> invalidArg "arguments" "Unknown command. Use --help for syntax."
            with
            | :? OperationCanceledException -> eprintfn "Cancelled."; 130
            | :? ArgumentException as error when error.ParamName = "arguments" -> eprintfn "%s" error.Message; 2
            | error -> eprintfn "%s" (error.GetBaseException().Message); 1
