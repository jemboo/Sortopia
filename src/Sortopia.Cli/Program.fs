namespace Sortopia.Cli

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.FileDb
open Sortopia.Functions
open Sortopia.Workspaces.Persistence

module Program =
    let private help = """Sortopia prepared execution
  sortopia prepare --workspace <folder> --run <run-id>
  sortopia list-preparations --project <folder>
  sortopia execute-results --project <folder> --preparation <preparation-id>
  sortopia execute-results --project <folder> --parameters <work-item-id-or-file>

Prepare locally, then copy the complete project folder to the execution machine.
execute-results reads only that folder and never runs setup. Retrying reuses saved
outputs. Ctrl+C cancels at database operations and between function calls.
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
    let private workItemId folder (text: string) =
        match Guid.TryParse text with
        | true, id when id <> Guid.Empty -> UMX.tag id
        | _ ->
            let id = Path.GetFileNameWithoutExtension text |> guid
            let expected = Path.GetFullPath(Path.Combine(folder, "runParameters", string id + ".msgpack"))
            let actual = Path.GetFullPath text
            let comparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
            if not (String.Equals(expected, actual, comparison)) then invalidArg "arguments" "The parameter file must be inside this project's runParameters folder."
            UMX.tag id
    let private finish (report: runExecutionReport) =
        printfn "%A: %d setup, %d results invocations." report.Status report.Setup.Length report.Results.Length
        if report.Error <> "" then eprintfn "%s" report.Error
        match report.Status with
        | runExecutionStatus.Completed -> 0
        | runExecutionStatus.Cancelled -> 130
        | _ -> 1

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
                | "prepare" ->
                    let values = options args[1..] ["--workspace"; "--run"]
                    let folder = required "--workspace" values
                    let runId = required "--run" values |> guid |> UMX.tag
                    let workspace = WorkspaceFileStore.LoadDefinition folder
                    let value = workspace.ProjectIndex.Values |> Seq.collect _.Runs |> Seq.tryFind (fun value -> value.Id = runId)
                                |> Option.defaultWith (fun () -> invalidOp "The saved run was not found in this workspace.")
                    let result = PreparedRunExecution.prepare workspace folder value progress cancellation.Token
                    match result.Preparation with
                    | Some ready -> printfn "Preparation: %O\nProject folder: %s\nWork items: %d" ready.Id (WorkspaceData.projectFolder workspace folder ready.ProjectId) ready.WorkItemIds.Length
                    | None -> ()
                    finish result.Execution
                | "list-preparations" ->
                    let values = options args[1..] ["--project"]
                    let folder = required "--project" values
                    for ready in PreparedRunFiles.listPreparations folder do
                        printfn "%O  %s  %s  %d work items" ready.Id (ready.CreatedUtc.ToString("u")) ready.RunName ready.WorkItemIds.Length
                        for id in ready.WorkItemIds do printfn "  %O" id
                    0
                | "execute-results" ->
                    let values = options args[1..] ["--project"; "--preparation"; "--parameters"]
                    let folder = required "--project" values
                    match Map.tryFind "--preparation" values, Map.tryFind "--parameters" values with
                    | Some id, None -> PreparedRunExecution.executePreparation folder (guid id |> UMX.tag) progress cancellation.Token |> finish
                    | None, Some id -> PreparedRunExecution.executeWorkItem folder (workItemId folder id) progress cancellation.Token |> finish
                    | _ -> invalidArg "arguments" "Choose exactly one of --preparation or --parameters."
                | _ -> invalidArg "arguments" "Unknown command. Use --help for syntax."
            with
            | :? OperationCanceledException -> eprintfn "Cancelled."; 130
            | :? ArgumentException as error when error.ParamName = "arguments" -> eprintfn "%s" error.Message; 2
            | error -> eprintfn "%s" (error.GetBaseException().Message); 1
