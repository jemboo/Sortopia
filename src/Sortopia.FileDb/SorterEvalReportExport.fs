namespace Sortopia.FileDb

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation
open Sortopia.Reporting

type sorterEvalReportExport = {
    Folder: string
    SummaryPath: string
    DetailsPath: string
    SummaryRows: int
    DetailRows: int64
}

module SorterEvalReportExport =
    let isSupported (item: runParameters) =
        item.Results.FunctionName = ResultsFunctionNames.sorterEval && item.SetupSetNumber.IsSome && not item.SetupInputs.IsEmpty

    let private integer value = reportValue.Integer (int64 value)
    let private optionalInteger value = value |> Option.map integer |> Option.defaultValue reportValue.Missing
    let private segment (text: string) = Uri.EscapeDataString(text).Replace(".", "%2E")
    let private inputName dataType =
        "Setup." + SortopiaDataType.kindName dataType
        + (match SortopiaDataType.text dataType with "" -> "" | label -> "[" + segment label + "]")
    let private parameterValue = function
        | TextValue value -> reportValue.Text value
        | IntegerValue value -> integer value
        | Unsigned64Value value -> reportValue.Unsigned64 value
        | FloatingValue value -> reportValue.Number value
        | BooleanValue value -> reportValue.Boolean value
        | GuidValue value -> reportValue.Identifier value
        | value -> reportValue.Text (ParameterValue.format value)

    let private commonColumns = [|
        { Name = "ReportSchemaVersion"; ValueType = reportValueType.Integer }
        for name in ["ProjectId"; "RunId"; "WorkItemId"; "ResultsDataId"; "ResultsQueryId"; "SorterSetEvalId"; "SorterSetId"; "SortableTestsId"] do
            { Name = name; ValueType = reportValueType.Identifier }
        for name in ["ResultsFunction"; "ResultsFunctionVersion"; "ResultsLabel"; "ParameterFile"; "ResultsFile"] do
            { Name = name; ValueType = reportValueType.Text }
        for name in ["SetupSetNumber"; "ResultsSetNumber"; "SetupSuffixStages"; "ResultsSuffixStages"] do
            { Name = name; ValueType = reportValueType.Integer } |]

    type private source = { Store: SortopiaFileDb; Output: plannedOutput; Metadata: reportRow; ParameterFile: string }

    /// Export only existing saved evaluations. The completed pair is published as
    /// one new directory, so failure/cancellation cannot leave a partial report.
    let exportFiles (parameterFiles: string array) (outputFolder: string) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        if parameterFiles.Length = 0 then invalidArg "parameterFiles" "Select at least one completed sorter-eval branch run parameter file."
        let destination = Path.GetFullPath outputFolder
        if File.Exists destination || Directory.Exists destination then invalidArg "outputFolder" "Choose a new report folder; existing reports are not overwritten."
        let queryCache = Collections.Generic.Dictionary<string * storedDataReference, parameterBinding list>()
        let outputIds = Collections.Generic.HashSet<Guid<projectId> * Guid<sortopiaDataId>>()
        let dynamicColumns = Collections.Generic.Dictionary<string, reportValueType>(StringComparer.Ordinal)
        let sources = parameterFiles |> Array.map Path.GetFullPath |> Array.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right)) |> Array.collect (fun path ->
            token.ThrowIfCancellationRequested()
            try
                let item = RunParameterFiles.loadFile path
                if not (isSupported item) then invalidOp "Reports require saved sorter-eval.evaluate branch run parameters."
                let folder = RunParameterFiles.projectFolder (Path.GetDirectoryName path)
                let store = SortopiaFileDb(folder, item.ProjectId)
                let plan = FileResultsHost.plan item.ProjectId item.Results item.Parameters item.SetupInputs
                let setupValues = [
                    for reference in item.SetupInputs |> List.sort do
                        let prefix = inputName reference.DataType
                        let bindings =
                            match queryCache.TryGetValue((folder, reference)) with
                            | true, bindings -> bindings
                            | _ ->
                                let bindings = store.ReadQueryParameters(reference, token)
                                queryCache.Add((folder, reference), bindings)
                                bindings
                        yield prefix + ".DataId", reportValue.Identifier (%reference.Id)
                        yield prefix + ".QueryId", reportValue.Identifier (%reference.QueryId)
                        yield prefix + ".File", reportValue.Text (store.GetPath reference)
                        for binding in bindings do
                            yield prefix + ".Query." + segment (string binding.KeyName), parameterValue binding.Value ]
                let parameters = [ for KeyValue(name, binding) in item.Parameters.ParamMap -> "Results." + segment (string name), parameterValue binding.Value ]
                for name, value in setupValues @ parameters do
                    let kind = ReportValue.valueType value |> Option.get
                    match dynamicColumns.TryGetValue name with
                    | true, old when old <> kind -> invalidOp $"Report column '{name}' has inconsistent saved value types."
                    | _ -> dynamicColumns[name] <- kind
                plan |> Map.toArray |> Array.map (fun (dataType, output) ->
                    match dataType with
                    | sortopiaDataType.SorterSetEval _ -> ()
                    | _ -> invalidOp "The selected invocation has a non-evaluation output."
                    let reference = output.Key.Reference
                    if not (outputIds.Add(reference.ProjectId, reference.Id)) then invalidOp "Selected parameter files refer to the same results file. Select each results file only once."
                    let metadata = ReportRow.create ([
                        "ReportSchemaVersion", integer 1
                        "ProjectId", reportValue.Identifier (%item.ProjectId)
                        "RunId", reportValue.Identifier (%item.RunId)
                        "WorkItemId", reportValue.Identifier (%item.Id)
                        "ResultsDataId", reportValue.Identifier (%reference.Id)
                        "ResultsQueryId", reportValue.Identifier (%reference.QueryId)
                        "ResultsFunction", reportValue.Text (string item.Results.FunctionName)
                        "ResultsFunctionVersion", reportValue.Text item.Results.FunctionVersion
                        "ResultsLabel", reportValue.Text (SortopiaDataType.text dataType)
                        "ParameterFile", reportValue.Text path
                        "ResultsFile", reportValue.Text (store.GetPath reference)
                        "SetupSetNumber", optionalInteger item.SetupSetNumber
                        "ResultsSetNumber", integer item.SetNumber
                        "SetupSuffixStages", optionalInteger item.SetupSuffixStages
                        "ResultsSuffixStages", optionalInteger item.ResultsSuffixStages ] @ setupValues @ parameters)
                    { Store = store; Output = output; Metadata = metadata; ParameterFile = path })
            with
            | :? OperationCanceledException -> reraise ()
            | error -> invalidOp $"Cannot report '{path}': {error.GetBaseException().Message}")
        let queryColumns, inputColumns =
            dynamicColumns |> Seq.sortWith (fun left right -> StringComparer.Ordinal.Compare(left.Key, right.Key))
            |> Seq.map (fun entry -> { Name = entry.Key; ValueType = entry.Value }) |> Seq.toArray
            |> Array.partition (fun column -> column.Name.StartsWith("Results.", StringComparison.Ordinal) || column.Name.Contains(".Query.", StringComparison.Ordinal))
        // Put the comparison fields and metrics first; keep full provenance to the right.
        let setupColumns, resultColumns = queryColumns |> Array.partition (fun column -> column.Name.StartsWith("Setup.", StringComparison.Ordinal))
        let columns metrics = Array.concat [setupColumns; resultColumns; metrics; commonColumns; inputColumns]
        let parent = Path.GetDirectoryName destination
        if String.IsNullOrEmpty parent then invalidArg "outputFolder" "Choose a new report folder below an existing directory."
        Directory.CreateDirectory parent |> ignore
        let temporary = Path.Combine(parent, ".sortopia-report-" + Guid.NewGuid().ToString("N") + ".tmp")
        Directory.CreateDirectory temporary |> ignore
        let mutable summaryRows = 0
        let mutable detailRows = 0L
        try
            do
                use summaryStream = new FileStream(Path.Combine(temporary, "Summary.csv"), FileMode.CreateNew, FileAccess.Write, FileShare.None)
                use detailsStream = new FileStream(Path.Combine(temporary, "Details.csv"), FileMode.CreateNew, FileAccess.Write, FileShare.None)
                use summary = new CsvReportWriter(summaryStream, columns SorterEvalReports.summaryColumns)
                use details = new CsvReportWriter(detailsStream, columns SorterEvalReports.detailColumns)
                for source in sources do
                    token.ThrowIfCancellationRequested()
                    try
                        let saved =
                            match (source.Store :> ISortopiaDb).TryLoad(source.Output.Key, token) |> HostStorage.require with
                            | Some saved -> saved
                            | None -> invalidOp "Results have not been generated. Execute this file before exporting reports."
                        let evaluations = match saved.Value with functionData.SorterSetEval value -> value | _ -> invalidOp "Expected a saved sorter evaluation."
                        let metadata = ReportRow.combine source.Metadata (ReportRow.create [
                            "SorterSetEvalId", reportValue.Identifier (%evaluations.SorterSetEvalId)
                            "SorterSetId", reportValue.Identifier (%evaluations.SorterSetId)
                            "SortableTestsId", reportValue.Identifier (%evaluations.SorterTestId) ])
                        summary.Write(ReportRow.combine metadata (evaluations |> SorterEvalReports.summarize |> SorterEvalReports.summaryRow), token)
                        summaryRows <- summaryRows + 1
                        for evaluation in evaluations.SorterEvals do
                            details.Write(ReportRow.combine metadata (SorterEvalReports.detailRow evaluation), token)
                            detailRows <- detailRows + 1L
                    with
                    | :? OperationCanceledException -> reraise ()
                    | error -> invalidOp $"Cannot report '{source.ParameterFile}': {error.GetBaseException().Message}"
            token.ThrowIfCancellationRequested()
            Directory.Move(temporary, destination)
            { Folder = destination; SummaryPath = Path.Combine(destination, "Summary.csv"); DetailsPath = Path.Combine(destination, "Details.csv")
              SummaryRows = summaryRows; DetailRows = detailRows }
        finally
            if Directory.Exists temporary then Directory.Delete(temporary, true)

    let exportFolder parametersFolder runId outputFolder token =
        let files = RunParameterFiles.listWorkItems parametersFolder token
                    |> Array.filter (fun item -> runId |> Option.forall ((=) item.RunId))
                    |> Array.map (fun item -> Path.Combine(parametersFolder, string item.Id + ".msgpack"))
        exportFiles files outputFolder token
