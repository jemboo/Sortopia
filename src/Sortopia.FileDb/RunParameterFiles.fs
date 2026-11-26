namespace Sortopia.FileDb

open System
open System.IO
open System.Security.Cryptography
open System.Threading
open MessagePack
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Core.Serialization

[<Measure>] type workItemId

module RunParameterSchema =
    let version = 2

/// An immutable results invocation, with the current SGD target resolved from its run definition.
/// No workspace or upstream project settings are needed.
type runParameters = {
    Id: Guid<workItemId>
    ProjectId: Guid<projectId>
    RunId: Guid<runId>
    Results: resultsConfig
    Parameters: parameterSet
    SetupInputs: storedDataReference list
    SetNumber: int
    SetupSetNumber: int option
    SetupSuffixStages: int option
    ResultsSuffixStages: int option
    Reporting: sgdReportingConfig option
    Sgd: sgdRunConfig option
}

[<MessagePackObject>]
type dataReferenceDto = {
    [<Key(0)>] ProjectId: Guid
    [<Key(1)>] Id: Guid
    [<Key(2)>] QueryId: Guid
    [<Key(3)>] IsSetup: bool
    [<Key(4)>] Kind: string
    [<Key(5)>] Label: string
    [<Key(6)>] Reused: bool
}

[<MessagePackObject>]
type preparedBindingDto = {
    [<Key(0)>] Key: string
    [<Key(1)>] Type: int
    [<Key(2)>] Value: byte array
}

[<MessagePackObject>]
type preparedOutputDto = {
    [<Key(0)>] Kind: string
    [<Key(1)>] Label: string
    [<Key(2)>] QueryKeys: string array
}

[<MessagePackObject>]
type runParametersDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] ProjectId: Guid
    [<Key(2)>] RunId: Guid
    [<Key(3)>] FunctionName: string
    [<Key(4)>] FunctionVersion: string
    [<Key(5)>] DatabaseName: string
    [<Key(6)>] Outputs: preparedOutputDto array
    [<Key(7)>] Parameters: preparedBindingDto array
    [<Key(8)>] SetupInputs: dataReferenceDto array
    [<Key(9)>] SetNumber: int
    [<Key(10)>] SetupSetNumber: int option
    // None means that the invocation has no single applicable suffix length.
    [<Key(11)>] SetupSuffixStages: int option
    [<Key(12)>] ResultsSuffixStages: int option
}

[<MessagePackObject>]
type runParametersEnvelopeDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Kind: string
    [<Key(2)>] Checksum: byte array
    [<Key(3)>] Payload: byte array
}

/// Reporting-enabled work items wrap the existing invocation. Unconfigured
/// work items retain their exact bytes and identities.
[<MessagePackObject>]
type reportingRunParametersDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Invocation: runParametersDto
    [<Key(2)>] SnapshotSchedule: string
    [<Key(3)>] SummarySchedule: string
}

/// The count lives in the run definition; changing it never rewrites an invocation.
[<MessagePackObject>]
type sgdRunParametersDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Invocation: runParametersDto
    [<Key(2)>] SnapshotInterval: int
    [<Key(3)>] SummarySchedule: string
}

/// Read only the run control fields, without loading its workspace or project configuration.
[<CLIMutable>]
type sgdRunTargetDto = { id: Guid; parentProjectId: Guid; sgd: sgdRunConfig option }

module internal RunParameterCodec =
    let reference (value: storedDataReference) : dataReferenceDto = {
        ProjectId = UMX.untag value.ProjectId; Id = UMX.untag value.Id; QueryId = UMX.untag value.QueryId
        IsSetup = RunExecutionPhase.toIsSetup value.Phase; Kind = SortopiaDataType.kindName value.DataType; Label = SortopiaDataType.text value.DataType; Reused = false }
    let readReference (value: dataReferenceDto) : storedDataReference =
        if value.ProjectId = Guid.Empty || value.Id = Guid.Empty || value.QueryId = Guid.Empty then invalidOp "Invalid saved setup reference."
        { ProjectId = UMX.tag value.ProjectId; Id = UMX.tag value.Id; QueryId = UMX.tag value.QueryId
          Phase = RunExecutionPhase.fromIsSetup value.IsSetup; DataType = SortopiaDataType.create value.Kind value.Label }

    // Primitive values retain their native MessagePack representation. Catalog IDs
    // use their canonical, validated wire text, just as workspace definitions do.
    let binding (value: parameterBinding) =
        let bytes =
            match value.Value with
            | TextValue v -> MessagePackSetup.serialize v
            | IntegerValue v -> MessagePackSetup.serialize v
            | Unsigned64Value v -> MessagePackSetup.serialize v
            | FloatingValue v -> MessagePackSetup.serialize v
            | BooleanValue v -> MessagePackSetup.serialize v
            | GuidValue v -> MessagePackSetup.serialize v
            | SorterLibIdValue _ | PrefixLibIdValue _ | MergeLibIdValue _
            | OpsWeightsValue _ | Seed6WeightsValue _ | UnfoldingWeightsValue _ | SorterSuccessFractionValue _ -> MessagePackSetup.serialize value.FormattedValue
        { Key = string value.KeyName; Type = int value.ValueType; Value = bytes }
    let readBinding (value: preparedBindingDto) =
        let typed =
            match enum<parameterValueType> value.Type with
            | parameterValueType.Text -> MessagePackSetup.deserialize<string> value.Value |> TextValue
            | parameterValueType.Integer -> MessagePackSetup.deserialize<int> value.Value |> IntegerValue
            | parameterValueType.Unsigned64 -> MessagePackSetup.deserialize<uint64> value.Value |> Unsigned64Value
            | parameterValueType.Floating -> MessagePackSetup.deserialize<float> value.Value |> FloatingValue
            | parameterValueType.Boolean -> MessagePackSetup.deserialize<bool> value.Value |> BooleanValue
            | parameterValueType.Guid -> MessagePackSetup.deserialize<Guid> value.Value |> GuidValue
            | parameterValueType.SorterLibId | parameterValueType.PrefixLibId | parameterValueType.MergeLibId
            | parameterValueType.OpsWeights | parameterValueType.Seed6Weights | parameterValueType.UnfoldingWeights | parameterValueType.SorterSuccessFraction as kind ->
                MessagePackSetup.deserialize<string> value.Value |> ParameterValue.parse kind
            | _ -> invalidOp "Unknown prepared parameter value type."
        ParameterBinding.fromValue (UMX.tag value.Key) typed

    let toDto (value: runParameters) : runParametersDto = {
        Version = RunParameterSchema.version; ProjectId = UMX.untag value.ProjectId; RunId = UMX.untag value.RunId
        FunctionName = string value.Results.FunctionName; FunctionVersion = value.Results.FunctionVersion; DatabaseName = string value.Results.DatabaseName
        Outputs = value.Results.DataTypes |> Map.toArray |> Array.map (fun (dataType, keys) ->
            { Kind = SortopiaDataType.kindName dataType; Label = SortopiaDataType.text dataType; QueryKeys = keys |> List.map string |> List.sort |> List.toArray })
        Parameters = value.Parameters.ParamMap |> Map.toArray |> Array.map (snd >> binding)
        SetupInputs = value.SetupInputs |> List.sort |> List.map reference |> List.toArray
        SetNumber = value.SetNumber; SetupSetNumber = value.SetupSetNumber
        SetupSuffixStages = value.SetupSuffixStages; ResultsSuffixStages = value.ResultsSuffixStages }

    let fromDto id (dto: runParametersDto) : runParameters =
        if dto.Version <> RunParameterSchema.version then invalidOp "Unsupported runParameters schema. Regenerate the run parameter files."
        if dto.ProjectId = Guid.Empty || dto.RunId = Guid.Empty || dto.SetNumber < 1
           || (dto.SetupSetNumber |> Option.exists (fun n -> n < 1)) then invalidOp "Invalid prepared work item identity."
        if String.IsNullOrWhiteSpace dto.DatabaseName then invalidOp "A prepared work item needs a results database name."
        if [dto.SetupSuffixStages; dto.ResultsSuffixStages] |> List.exists (Option.exists (fun stages -> stages < 1)) then
            invalidOp "Saved suffix stage lengths must be positive."
        let outputs = dto.Outputs |> Array.map (fun entry ->
            { DataType = SortopiaDataType.create entry.Kind entry.Label; QueryParameterKeys = entry.QueryKeys |> Array.map UMX.tag |> Array.toList }) |> Array.toList
        if (outputs |> List.map (fun entry -> entry.DataType) |> List.distinct).Length <> outputs.Length then invalidOp "Duplicate prepared output type."
        { Id = id; ProjectId = UMX.tag dto.ProjectId; RunId = UMX.tag dto.RunId
          Results = { DatabaseName = UMX.tag dto.DatabaseName; FunctionName = UMX.tag dto.FunctionName; FunctionVersion = dto.FunctionVersion
                      Outputs = outputs; ParameterDefaults = [] }
          Parameters = dto.Parameters |> Array.map readBinding |> parameterSet.fromBindings
          SetupInputs = dto.SetupInputs |> Array.map readReference |> Array.toList
          SetNumber = dto.SetNumber; SetupSetNumber = dto.SetupSetNumber
          SetupSuffixStages = dto.SetupSuffixStages; ResultsSuffixStages = dto.ResultsSuffixStages; Reporting = None; Sgd = None }

    let encode value =
        let invocation = toDto value
        match value.Sgd, value.Reporting with
        | Some controls, reporting ->
            SgdRun.validate controls |> ignore
            MessagePackSetup.serialize { Version = 4; Invocation = invocation; SnapshotInterval = controls.SnapshotInterval
                                         SummarySchedule = reporting |> Option.map _.SummarySchedule |> Option.defaultValue "Constant(1)" }
        | None, None -> MessagePackSetup.serialize invocation
        | None, Some reporting ->
            SgdReporting.validate reporting |> ignore
            MessagePackSetup.serialize { Version = 3; Invocation = invocation; SnapshotSchedule = reporting.SnapshotSchedule; SummarySchedule = reporting.SummarySchedule }

    let decode id (payload: byte array) =
        let mutable reader = MessagePackReader(ReadOnlyMemory<byte>(payload))
        reader.ReadArrayHeader() |> ignore
        match reader.ReadInt32() with
        | 2 -> MessagePackSetup.deserialize<runParametersDto> payload |> fromDto id
        | 3 ->
            let dto = MessagePackSetup.deserialize<reportingRunParametersDto> payload
            let reporting = SgdReporting.validate { SnapshotSchedule = dto.SnapshotSchedule; SummarySchedule = dto.SummarySchedule }
            { fromDto id dto.Invocation with Reporting = Some reporting }
        | 4 ->
            let dto = MessagePackSetup.deserialize<sgdRunParametersDto> payload
            let controls = SgdRun.validate { SnapshotInterval = dto.SnapshotInterval; SnapshotCount = 0; TempSnapshotInterval = None }
            let reporting = SgdReporting.validate { SnapshotSchedule = $"Constant({dto.SnapshotInterval})"; SummarySchedule = dto.SummarySchedule }
            { fromDto id dto.Invocation with Reporting = Some reporting; Sgd = Some controls }
        | _ -> invalidOp "Unsupported runParameters schema. Regenerate the run parameter files."

/// Small immutable metadata files, atomically published beside project-local data.
module internal RunParameterStorage =
    let private maxBytes = 67108864
    let private options = MessagePackSetup.options.WithCompression(MessagePackCompression.Lz4BlockArray)
                             .WithSecurity(MessagePackSetup.options.Security.WithMaximumDecompressedSize(maxBytes))
    let path folder category (id: Guid) =
        let root = Path.TrimEndingDirectorySeparator(Path.GetFullPath folder)
        if not (Directory.Exists root) then invalidOp $"Project folder does not exist: {root}"
        let full = Path.Combine(root, category, string id + ".msgpack")
        let mutable current = full
        while not (isNull current) && current.Length >= root.Length do
            if (File.Exists current || Directory.Exists current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint) then
                invalidOp "Prepared execution storage does not follow filesystem links."
            current <- Path.GetDirectoryName current
        full
    let requireProject folder (id: Guid<projectId>) =
        let definition = path folder "" (UMX.untag id)
        if not (File.Exists definition) then invalidOp $"This folder does not contain project definition '{id}.msgpack'. Copy the complete project folder without changing its IDs."
    let read folder category id kind =
        let file = path folder category id
        use stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length > int64 maxBytes then invalidOp "Prepared execution metadata exceeds the 64 MiB limit."
        let bytes = Array.zeroCreate<byte> (int stream.Length)
        stream.ReadExactly bytes
        let envelope = MessagePackSerializer.Deserialize<runParametersEnvelopeDto>(ReadOnlyMemory<byte>(bytes), options)
        if isNull (box envelope) || envelope.Version <> 1 || envelope.Kind <> kind || isNull envelope.Payload
           || isNull envelope.Checksum || SHA256.HashData envelope.Payload <> envelope.Checksum then
            invalidOp $"Invalid or damaged {kind} file: {file}"
        envelope.Payload
    let publish folder category id kind (payload: byte array) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let file = path folder category id
        if payload.Length > maxBytes - 1024 then invalidOp "Prepared execution metadata exceeds the 64 MiB limit."
        let bytes = MessagePackSerializer.Serialize({ Version = 1; Kind = kind; Checksum = SHA256.HashData payload; Payload = payload }, options)
        if bytes.Length > maxBytes then invalidOp "Prepared execution metadata exceeds the 64 MiB limit."
        Directory.CreateDirectory(Path.GetDirectoryName file) |> ignore
        let verifyExisting () =
            let existing = read folder category id kind
            if existing <> payload then invalidOp $"An immutable {kind} file already exists with different content: {file}"
            existing
        if File.Exists file then verifyExisting ()
        else
            let temporary = file + "." + string (Guid.NewGuid()) + ".tmp"
            try
                do
                    use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    stream.Write bytes
                    stream.Flush true
                token.ThrowIfCancellationRequested()
                try
                    File.Move(temporary, file, false)
                    payload
                with :? IOException when File.Exists file -> verifyExisting ()
            finally
                if File.Exists temporary then File.Delete temporary
module RunParameterFiles =
    let private pathComparison = if OperatingSystem.IsWindows() then StringComparison.OrdinalIgnoreCase else StringComparison.Ordinal
    let private hash (bytes: byte array) : Guid<workItemId> = UMX.tag (Guid(SHA256.HashData(bytes).[0..15]))
    let internal saveWorkItem folder (value: runParameters) token =
        let payload = RunParameterCodec.encode value
        let id = hash payload
        RunParameterStorage.publish folder "runParameters" (UMX.untag id) "runParameters" payload token |> ignore
        id
    let loadWorkItem folder (id: Guid<workItemId>) =
        let payload = RunParameterStorage.read folder "runParameters" (UMX.untag id) "runParameters"
        if hash payload <> id then invalidOp "The runParameters contents do not match their file ID."
        let value = RunParameterCodec.decode id payload
        RunParameterStorage.requireProject folder value.ProjectId
        match value.Sgd with
        | None -> value
        | Some frozen ->
            let path = RunParameterStorage.path folder "runs" (UMX.untag value.RunId)
            let info = FileInfo path
            if info.Length > 67108864L then invalidOp "Run definition exceeds the 64 MiB limit."
            let run = MessagePackSetup.deserialize<sgdRunTargetDto> (File.ReadAllBytes path)
            if run.id <> UMX.untag value.RunId || run.parentProjectId <> UMX.untag value.ProjectId then
                invalidOp "The SGD run definition does not match this parameter file."
            let controls = run.sgd |> Option.defaultWith (fun () -> invalidOp "The SGD run definition is missing its snapshot controls.") |> SgdRun.validate
            SgdRun.validateExtension frozen controls
            { value with Sgd = Some controls }

    /// The selected folder stays inside its project; copied projects need no workspace.
    let projectFolder (parametersFolder: string) =
        let folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath parametersFolder)
        if not (String.Equals(Path.GetFileName folder, "runParameters", pathComparison)) then
            invalidArg "parametersFolder" "Select the project's runParameters folder."
        if not (Directory.Exists folder) then invalidOp $"Run parameters folder does not exist: {folder}"
        let mutable current = folder
        while not (String.IsNullOrEmpty current) do
            if File.GetAttributes(current).HasFlag FileAttributes.ReparsePoint then invalidOp "Run parameter storage does not follow filesystem links."
            current <- Path.GetDirectoryName current
        Path.GetDirectoryName folder

    let workItemId (path: string) : Guid<workItemId> =
        match Guid.TryParse(Path.GetFileNameWithoutExtension path) with
        | true, id when id <> Guid.Empty && String.Equals(Path.GetExtension path, ".msgpack", StringComparison.OrdinalIgnoreCase) -> UMX.tag id
        | _ -> invalidArg "path" $"Expected a GUID-named .msgpack parameter file: {path}"

    let loadFile (path: string) =
        let full = Path.GetFullPath path
        let project = projectFolder (Path.GetDirectoryName full)
        try
            let id = workItemId full
            let expected = Path.Combine(project, "runParameters", string id + ".msgpack")
            if not (String.Equals(full, expected, pathComparison)) then invalidOp "Use the canonical GUID filename from parameter generation."
            loadWorkItem project id
        with error -> invalidOp $"Cannot read parameter file '{full}': {error.GetBaseException().Message}"

    /// Enumerate once, ignoring unpublished temporary files. Every discovered file
    /// is independently usable; there is no separate batch-membership record.
    let listWorkItems parametersFolder (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        projectFolder parametersFolder |> ignore
        let items = Directory.GetFiles(parametersFolder, "*.msgpack") |> Array.map (fun path ->
            token.ThrowIfCancellationRequested()
            loadFile path)
        if (items |> Array.map _.ProjectId |> Array.distinct).Length > 1 then invalidOp "The runParameters folder contains files from different projects."
        if (items |> Array.map _.Id |> Array.distinct).Length <> items.Length then invalidOp "The runParameters folder contains duplicate work item IDs."
        items |> Array.sortBy (fun item -> item.RunId, item.SetupSetNumber, item.SetNumber, item.Id)
