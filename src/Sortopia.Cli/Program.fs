namespace Sortopia.Cli

open System
open System.IO
open System.Text.Json
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
  sortopia execute-results --parameters-list <json-array-file> [--jobs <count|auto>] [--cpu-percent <1-100>] [--memory <GiB|percent%>] [--memory-cap <GiB|percent%>]
  sortopia export-sorter-eval --parameters-folder <project/runParameters> [--run <run-id>] [--output <report-folder>]
  sortopia export-sorter-eval --parameters <path-to-work-item.msgpack> [--output <report-folder>]

Generate locally (including setup), then copy the complete project folder.
Folder execution considers every parameter file present, including older files;
--run restricts it to one saved run ID. Complete outputs are validated and skipped;
partial work reuses existing outputs. No manifests or execution receipts are needed.
Results execution never runs setup. --jobs is a positive maximum concurrent job count (default 1).
All execution forms accept --cpu-percent, --memory and --memory-cap independently.
--memory is a soft scheduling target: delay additional files, but allow a large file to run alone.
It does not cap allocations or guarantee free RAM. Memory examples: 8GiB or 50% of available memory at launch; minimum 0.25 GiB.
--memory-cap optionally enforces a Windows committed-memory cap, including preparation; allocations can fail at this cap.
--jobs auto allows up to four files within the CPU worker count and soft memory target.
Limits apply to this CLI batch, not other terminals. Resource-controlled execution runs at below-normal priority.
--execution-metrics <new-file.json> optionally records CPU time, whole-job peak memory, target and cap separately.
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
        let percent = Map.tryFind "--cpu-percent" values |> Option.map ExecutionResources.cpuPercent |> Option.defaultValue 100
        let available = lazy (ExecutionResources.availableMemory ())
        let memory key = Map.tryFind key values |> Option.map (fun value -> ExecutionResources.memoryBytes available.Value value) |> Option.defaultValue 0L
        let target, cap = memory "--memory", memory "--memory-cap"
        let limits =
            if ["--cpu-percent"; "--memory"; "--memory-cap"; "--execution-metrics"] |> List.exists (fun key -> Map.containsKey key values) then Some(percent, cap)
            else None
        let workers = ExecutionResources.cpuWorkers Environment.ProcessorCount percent
        let jobs = ExecutionResources.parallelJobs workers (Map.tryFind "--jobs" values |> Option.defaultValue "1")
        resultsExecutionOptions(jobs, workers, target), limits

    let private executionKeys = ["--parameters-folder"; "--run"; "--parameters"; "--parameters-list"; "--jobs"; "--cpu-percent"; "--memory"; "--memory-cap"; "--execution-metrics"]

    let private executionSelector values =
        // Reject ambiguous selectors before changing process limits or opening files.
        let selectors = ["--parameters-folder"; "--parameters"; "--parameters-list"] |> List.filter (fun key -> Map.containsKey key values)
        if selectors.Length <> 1 || (selectors.Head <> "--parameters-folder" && Map.containsKey "--run" values) then
            invalidArg "arguments" "Choose --parameters-folder (optionally --run), --parameters, or --parameters-list."
        selectors.Head

    let private executeResults values execution progress token =
        match executionSelector values with
        | "--parameters-folder" -> RunParameterExecution.executeFolderWithOptions (required "--parameters-folder" values) (runFilter values) execution progress token
        | "--parameters" -> RunParameterExecution.executeFileWithOptions (required "--parameters" values) execution progress token
        | _ ->
            let path = required "--parameters-list" values
            if FileInfo(path).Length > 32L * 1024L * 1024L then invalidArg "arguments" "The parameter selection list exceeds 32 MiB."
            let paths =
                try JsonSerializer.Deserialize<string array>(File.ReadAllText path)
                with :? JsonException -> invalidArg "arguments" "The parameter selection must be a JSON array of file paths."
            if isNull paths || paths.Length = 0 || Array.exists String.IsNullOrWhiteSpace paths then
                invalidArg "arguments" "The parameter selection must be a nonempty JSON array of file paths."
            RunParameterExecution.executeFilesWithOptions paths execution progress null token
    let private finish (report: runExecutionReport) =
        CommandLineReport.write "%A: %d setup sets, %d results work items processed." report.Status report.Setup.Length report.Results.Length
        if report.Error <> "" then CommandLineReport.error "%s" report.Error
        match report.Status with runExecutionStatus.Completed -> 0 | runExecutionStatus.Cancelled -> 130 | _ -> 1

    [<EntryPoint>]
    let main args =
        if args.Length = 0 || args = [| "--help" |] || args = [| "-h" |] then printfn "%s" help; 0
        else
            use cancellation = new CancellationTokenSource()
            let cancelHandler = ConsoleCancelEventHandler(fun _ event -> event.Cancel <- true; cancellation.Cancel())
            Console.CancelKeyPress.AddHandler cancelHandler
            use subscription = { new IDisposable with member _.Dispose() = Console.CancelKeyPress.RemoveHandler cancelHandler }
            let progress = { new IProgress<runExecutionProgress> with member _.Report value = CommandLineReport.write "%s (%d/%d)" value.Message value.Completed value.Total }
            try
                match args[0] with
                | "generate-parameters" ->
                    let values = options args[1..] ["--workspace"; "--run"]
                    let folder = required "--workspace" values
                    let id = required "--run" values |> guid
                    let generated = WorkspaceRunGeneration.generate (WorkspaceFileStore.LoadWorkspace folder) id progress cancellation.Token
                    CommandLineReport.write "Published parameter files: %d\nProject folder: %s" generated.Report.WorkItemIds.Length (WorkspaceData.scopedProjectFolder generated.Scope folder generated.Scope.Run.ParentProjectId)
                    finish generated.Report.Execution
                | "list-parameters" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"]
                    let items = RunParameterFiles.listWorkItems (required "--parameters-folder" values) cancellation.Token
                    let filter = runFilter values
                    for item in items |> Array.filter (fun item -> filter |> Option.forall ((=) item.RunId)) do
                        CommandLineReport.write "%O  run %O  %s  set %d" item.Id item.RunId (string item.Results.FunctionName) item.SetNumber
                    0
                | "check-results" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"]
                    let statuses = RunParameterExecution.inspectFolder (required "--parameters-folder" values) (runFilter values) cancellation.Token
                    for item in statuses do
                        CommandLineReport.write "%O  run %O  %A  %d/%d valid outputs" item.Id item.RunId item.Outputs.State item.Outputs.Available.Length item.Outputs.ExpectedCount
                        if item.Outputs.Error <> "" then CommandLineReport.error "%s" item.Outputs.Error
                    if statuses |> Array.exists (fun item -> item.Outputs.State = outputState.Invalid) then 1 else 0
                | "execute-results" ->
                    let values = options args[1..] executionKeys
                    executionSelector values |> ignore
                    let execution, limits = executionOptions values
                    match limits with
                    | Some(percent, bytes) ->
                        BoundedExecution.run percent bytes execution (Map.tryFind "--execution-metrics" values)
                            (fun execution token -> executeResults values execution progress token |> finish) cancellation.Token
                    | None ->
                        executeResults values execution progress cancellation.Token |> finish
                | "export-sorter-eval" ->
                    let values = options args[1..] ["--parameters-folder"; "--run"; "--parameters"; "--output"]
                    let output folder = Map.tryFind "--output" values |> Option.defaultWith (fun () -> SorterEvalReportExport.nextOutputFolder folder)
                    let exported =
                        match Map.tryFind "--parameters-folder" values, Map.tryFind "--parameters" values with
                        | Some folder, None -> SorterEvalReportExport.exportFolder folder (runFilter values) (output folder) cancellation.Token
                        | None, Some file when not (Map.containsKey "--run" values) -> SorterEvalReportExport.exportFiles [|file|] (output (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath file))) cancellation.Token
                        | _ -> invalidArg "arguments" "Choose --parameters-folder (optionally --run), or one --parameters file."
                    if exported.SummaryPath.IsSome then
                        CommandLineReport.write "Exported %d summary rows and %d detail rows.\n%s\n%s" exported.SummaryRows exported.DetailRows exported.SummaryPath.Value exported.DetailsPath.Value
                    if exported.EvolutionSummaryPath.IsSome then
                        CommandLineReport.write "Exported %d evolution summary rows.\n%s" exported.EvolutionSummaryRows exported.EvolutionSummaryPath.Value
                    0
                | _ -> invalidArg "arguments" "Unknown command. Use --help for syntax."
            with
            | :? OperationCanceledException -> CommandLineReport.error "Cancelled."; 130
            | :? ArgumentException as error when error.ParamName = "arguments" -> CommandLineReport.error "%s" error.Message; 2
            | error -> CommandLineReport.error "%s" (error.GetBaseException().Message); 1
