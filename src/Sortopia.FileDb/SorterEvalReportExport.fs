namespace Sortopia.FileDb

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Models.Sorting
open Sortopia.Reporting

type sorterEvalReportExport = {
    Folder: string
    SummaryPath: string
    DetailsPath: string
    SummaryRows: int
    DetailRows: int64
    EvolutionSummaryPath: string option
    SnapshotsPath: string option
    EvolutionSummaryRows: int64
    SnapshotRows: int64
}

module SorterEvalReportExport =
    /// One project report location, independent of run IDs and export time.
    let nextOutputFolder parametersFolder =
        let project = RunParameterFiles.projectFolder parametersFolder
        Path.Combine(project, "data", "results", "reports")

    let private isReportOutput (dataType: sortopiaDataType) = dataType.IsSorterSetEval || dataType.IsSorterPoolSet

    /// Report saved evaluations or evaluations embedded in final SGD pools.
    let isSupported (item: runParameters) =
        item.SetupSetNumber.IsSome && not item.SetupInputs.IsEmpty
        && (item.Results.DataTypes |> Map.exists (fun dataType _ -> isReportOutput dataType))

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

    type private source = { Store: SortopiaFileDb; Output: plannedOutput; Metadata: reportRow; ParameterFile: string; Folder: string; Item: runParameters }

    let private snapshotColumns = [|
        for name in ["Generation"; "PoolIndex"; "MemberIndex"; "Birthday"; "MutationIndex"; "MutationMod"] do
            { Name = name; ValueType = reportValueType.Integer }
        for name in ["PoolId"; "MemberId"; "ParentId"; "ModelId"] do
            { Name = name; ValueType = reportValueType.Identifier }
        { Name = "SnapshotFile"; ValueType = reportValueType.Text }
        yield! SorterEvalReports.detailColumns |]

    let private writeEvolution sources columns temporary token =
        let reporting = sources |> Array.filter (fun source -> source.Item.Reporting.IsSome) |> Array.distinctBy (fun source -> source.Folder, source.Item.Id)
        let mutable summaryRows, snapshotRows = 0L, 0L
        if reporting.Length > 0 then
            use summaryStream = File.Create(Path.Combine(temporary, "EvolutionSummary.csv"))
            use snapshotStream = File.Create(Path.Combine(temporary, "Snapshots.csv"))
            use summary = new CsvReportWriter(summaryStream, columns EvolutionReports.columns)
            use snapshots = new CsvReportWriter(snapshotStream, columns snapshotColumns)
            for source in reporting do
                let samples, rows = EvolutionReportingStore.load source.Folder source.Item token
                for row in rows do
                    summary.Write(ReportRow.combine source.Metadata (EvolutionReports.row row), token)
                    summaryRows <- summaryRows + 1L
                for generation in samples.Snapshots do
                    let snapshot = EvolutionReportingStore.loadSnapshot source.Folder source.Item generation token
                    if snapshot.Pools.Length <> samples.PoolCount then invalidOp "Snapshot pool count differs from the execution parameters."
                    for poolIndex, pool in snapshot.Pools |> Array.indexed do
                        for memberIndex, memberValue in pool.Members |> Array.indexed do
                            let metadata = ReportRow.create [
                                "Generation", integer generation; "PoolIndex", integer poolIndex; "MemberIndex", integer memberIndex
                                "PoolId", reportValue.Identifier pool.Id; "MemberId", reportValue.Identifier memberValue.Id
                                "ParentId", memberValue.ParentId |> Option.map reportValue.Identifier |> Option.defaultValue reportValue.Missing
                                "ModelId", reportValue.Identifier (%SorterModel.getId memberValue.Model)
                                "Birthday", integer memberValue.Birthday; "MutationIndex", integer memberValue.MutationIndex; "MutationMod", integer memberValue.MutationMod
                                "SortableTestsId", reportValue.Identifier (%snapshot.TestsId)
                                "SnapshotFile", reportValue.Text (EvolutionReportingStore.snapshotPath source.Folder source.Item generation) ]
                            snapshots.Write(ReportRow.combine source.Metadata (ReportRow.combine metadata (SorterEvalReports.detailRow memberValue.Evaluation)), token)
                            snapshotRows <- snapshotRows + 1L
        reporting.Length > 0, summaryRows, snapshotRows

    /// Build the selected export before replacing the project's previous report files.
    let exportFiles (parameterFiles: string array) (outputFolder: string) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        if parameterFiles.Length = 0 then invalidArg "parameterFiles" "Select at least one completed branch run parameter file with sorter evaluations."
        let destination = Path.GetFullPath outputFolder
        if File.Exists destination then invalidArg "outputFolder" "Choose a report folder."
        if Directory.Exists destination && File.GetAttributes(destination).HasFlag FileAttributes.ReparsePoint then
            invalidArg "outputFolder" "Report exports do not follow linked folders."
        let queryCache = Collections.Generic.Dictionary<string * storedDataReference, parameterBinding list>()
        let outputIds = Collections.Generic.HashSet<Guid<projectId> * Guid<sortopiaDataId>>()
        let dynamicColumns = Collections.Generic.Dictionary<string, reportValueType>(StringComparer.Ordinal)
        let sources = parameterFiles |> Array.map Path.GetFullPath |> Array.sortWith (fun left right -> StringComparer.Ordinal.Compare(left, right)) |> Array.collect (fun path ->
            token.ThrowIfCancellationRequested()
            try
                let item = RunParameterFiles.loadFile path
                if not (isSupported item) then invalidOp "Reports require saved branch run parameters with SorterSetEval or SorterPoolSet outputs."
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
                plan |> Map.toArray |> Array.filter (fun (dataType, _) -> isReportOutput dataType) |> Array.map (fun (dataType, output) ->
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
                    { Store = store; Output = output; Metadata = metadata; ParameterFile = path; Folder = folder; Item = item })
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
        let mutable removeTemporary = true
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
                        let evaluations, identities =
                            match saved.Value with
                            | functionData.SorterSetEval value ->
                                value.SorterEvals, [
                                    "SorterSetEvalId", reportValue.Identifier (%value.SorterSetEvalId)
                                    "SorterSetId", reportValue.Identifier (%value.SorterSetId)
                                    "SortableTestsId", reportValue.Identifier (%value.SorterTestId) ]
                            | functionData.SorterPoolSet value ->
                                let evaluations = value.Pools |> Array.collect _.Members |> Array.map _.Evaluation |> Array.distinctBy SorterEval.getSorterId
                                evaluations, ["SortableTestsId", reportValue.Identifier (%value.TestsId)]
                            | _ -> invalidOp "Expected saved sorter evaluations or pools."
                        let metadata = ReportRow.combine source.Metadata (ReportRow.create identities)
                        summary.Write(ReportRow.combine metadata (evaluations |> SorterEvalReports.summarizeEvaluations |> SorterEvalReports.summaryRow), token)
                        summaryRows <- summaryRows + 1
                        for evaluation in evaluations do
                            details.Write(ReportRow.combine metadata (SorterEvalReports.detailRow evaluation), token)
                            detailRows <- detailRows + 1L
                    with
                    | :? OperationCanceledException -> reraise ()
                    | error -> invalidOp $"Cannot report '{source.ParameterFile}': {error.GetBaseException().Message}"
            let hasEvolution, evolutionRows, snapshotRows = writeEvolution sources columns temporary token
            token.ThrowIfCancellationRequested()
            if not (Directory.Exists destination) then Directory.Move(temporary, destination)
            else
                let lockPath = Path.Combine(destination, ".sortopia-export.lock")
                use exportLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose)
                let names = ["Summary.csv"; "Details.csv"; "EvolutionSummary.csv"; "Snapshots.csv"]
                let previous = Path.Combine(temporary, "previous")
                Directory.CreateDirectory previous |> ignore
                let backedUp, installed = ResizeArray<string>(), ResizeArray<string>()
                try
                    for name in names do
                        let target = Path.Combine(destination, name)
                        if File.Exists target then
                            if File.GetAttributes(target).HasFlag FileAttributes.ReparsePoint then invalidOp "Report exports do not replace linked files."
                            File.Move(target, Path.Combine(previous, name))
                            backedUp.Add name
                    for name in names do
                        let staged = Path.Combine(temporary, name)
                        if File.Exists staged then
                            File.Move(staged, Path.Combine(destination, name))
                            installed.Add name
                with error ->
                    try
                        for name in installed do File.Delete(Path.Combine(destination, name))
                        for name in backedUp do File.Move(Path.Combine(previous, name), Path.Combine(destination, name))
                    with recoveryError ->
                        removeTemporary <- false
                        invalidOp $"Export failed: {error.Message}. Restore the previous reports from '{previous}': {recoveryError.Message}"
                    reraise ()
            { Folder = destination; SummaryPath = Path.Combine(destination, "Summary.csv"); DetailsPath = Path.Combine(destination, "Details.csv")
              SummaryRows = summaryRows; DetailRows = detailRows
              EvolutionSummaryPath = if hasEvolution then Some (Path.Combine(destination, "EvolutionSummary.csv")) else None
              SnapshotsPath = if hasEvolution then Some (Path.Combine(destination, "Snapshots.csv")) else None
              EvolutionSummaryRows = evolutionRows; SnapshotRows = snapshotRows }
        finally
            if removeTemporary && Directory.Exists temporary then Directory.Delete(temporary, true)

    let exportFolder parametersFolder runId outputFolder token =
        let files = RunParameterFiles.listWorkItems parametersFolder token
                    |> Array.filter (fun item -> runId |> Option.forall ((=) item.RunId))
                    |> Array.map (fun item -> Path.Combine(parametersFolder, string item.Id + ".msgpack"))
        exportFiles files outputFolder token
