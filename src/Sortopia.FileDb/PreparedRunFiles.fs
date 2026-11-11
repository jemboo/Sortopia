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
[<Measure>] type preparationId

/// One immutable results invocation. No paths or live project settings are needed.
type runParameters = {
    Id: Guid<workItemId>
    ProjectId: Guid<projectId>
    RunId: Guid<runId>
    Results: resultsConfig
    Parameters: parameterSet
    SetupInputs: storedDataReference list
    SetNumber: int
    SetupSetNumber: int option
}

/// Published only after all work items and their local setup inputs are ready.
type preparedRun = {
    Id: Guid<preparationId>
    ProjectId: Guid<projectId>
    RunId: Guid<runId>
    RunName: string
    CreatedUtc: DateTime
    SetupSetCount: int
    WorkItemIds: Guid<workItemId> array
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
}

[<MessagePackObject>]
type preparedRunDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Id: Guid
    [<Key(2)>] ProjectId: Guid
    [<Key(3)>] RunId: Guid
    [<Key(4)>] RunName: string
    [<Key(5)>] CreatedUtc: DateTime
    [<Key(6)>] SetupSetCount: int
    [<Key(7)>] WorkItemIds: Guid array
}

[<MessagePackObject>]
type preparationEnvelopeDto = {
    [<Key(0)>] Version: int
    [<Key(1)>] Kind: string
    [<Key(2)>] Checksum: byte array
    [<Key(3)>] Payload: byte array
}

module internal PreparedCodec =
    let reference (value: storedDataReference) : dataReferenceDto = {
        ProjectId = UMX.untag value.ProjectId; Id = UMX.untag value.Id; QueryId = UMX.untag value.QueryId
        IsSetup = value.IsSetup; Kind = SortopiaDataType.kindName value.DataType; Label = SortopiaDataType.text value.DataType; Reused = false }
    let readReference (value: dataReferenceDto) : storedDataReference =
        if value.ProjectId = Guid.Empty || value.Id = Guid.Empty || value.QueryId = Guid.Empty then invalidOp "Invalid saved setup reference."
        { ProjectId = UMX.tag value.ProjectId; Id = UMX.tag value.Id; QueryId = UMX.tag value.QueryId
          IsSetup = value.IsSetup; DataType = SortopiaDataType.create value.Kind value.Label }

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
            | SorterLibIdValue _ | PrefixLibIdValue _ | MergeLibIdValue _ -> MessagePackSetup.serialize value.FormattedValue
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
            | parameterValueType.SorterLibId | parameterValueType.PrefixLibId | parameterValueType.MergeLibId as kind ->
                MessagePackSetup.deserialize<string> value.Value |> ParameterValue.parse kind
            | _ -> invalidOp "Unknown prepared parameter value type."
        ParameterBinding.fromValue (UMX.tag value.Key) typed

    let toDto (value: runParameters) : runParametersDto = {
        Version = 1; ProjectId = UMX.untag value.ProjectId; RunId = UMX.untag value.RunId
        FunctionName = string value.Results.FunctionName; FunctionVersion = value.Results.FunctionVersion; DatabaseName = string value.Results.DatabaseName
        Outputs = value.Results.DataTypes |> Map.toArray |> Array.map (fun (dataType, keys) ->
            { Kind = SortopiaDataType.kindName dataType; Label = SortopiaDataType.text dataType; QueryKeys = keys |> List.map string |> List.sort |> List.toArray })
        Parameters = value.Parameters.ParamMap |> Map.toArray |> Array.map (snd >> binding)
        SetupInputs = value.SetupInputs |> List.sort |> List.map reference |> List.toArray
        SetNumber = value.SetNumber; SetupSetNumber = value.SetupSetNumber }

    let fromDto id (dto: runParametersDto) : runParameters =
        if dto.Version <> 1 then invalidOp "Unsupported runParameters schema."
        if dto.ProjectId = Guid.Empty || dto.RunId = Guid.Empty || dto.SetNumber < 1
           || (dto.SetupSetNumber |> Option.exists (fun n -> n < 1)) then invalidOp "Invalid prepared work item identity."
        if String.IsNullOrWhiteSpace dto.DatabaseName then invalidOp "A prepared work item needs a results database name."
        let outputs = dto.Outputs |> Array.map (fun entry ->
            { DataType = SortopiaDataType.create entry.Kind entry.Label; QueryParameterKeys = entry.QueryKeys |> Array.map UMX.tag |> Array.toList }) |> Array.toList
        if (outputs |> List.map (fun entry -> entry.DataType) |> List.distinct).Length <> outputs.Length then invalidOp "Duplicate prepared output type."
        { Id = id; ProjectId = UMX.tag dto.ProjectId; RunId = UMX.tag dto.RunId
          Results = { DatabaseName = UMX.tag dto.DatabaseName; FunctionName = UMX.tag dto.FunctionName; FunctionVersion = dto.FunctionVersion
                      Outputs = outputs; ParameterDefaults = [] }
          Parameters = dto.Parameters |> Array.map readBinding |> parameterSet.fromBindings
          SetupInputs = dto.SetupInputs |> Array.map readReference |> Array.toList
          SetNumber = dto.SetNumber; SetupSetNumber = dto.SetupSetNumber }

/// Small immutable metadata files, atomically published beside project-local data.
module internal PreparedStorage =
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
        let envelope = MessagePackSerializer.Deserialize<preparationEnvelopeDto>(ReadOnlyMemory<byte>(bytes), options)
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
            if read folder category id kind <> payload then invalidOp $"An immutable {kind} file already exists with different content: {file}"
        if File.Exists file then verifyExisting ()
        else
            let temporary = file + "." + string (Guid.NewGuid()) + ".tmp"
            try
                do
                    use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                    stream.Write bytes
                    stream.Flush true
                token.ThrowIfCancellationRequested()
                try File.Move(temporary, file, false)
                with :? IOException when File.Exists file -> verifyExisting ()
            finally
                if File.Exists temporary then File.Delete temporary
    let ids folder category =
        let directory = Path.GetDirectoryName(path folder category Guid.Empty)
        if not (Directory.Exists directory) then [||]
        else Directory.GetFiles(directory, "*.msgpack") |> Array.map (fun file -> Guid.Parse(Path.GetFileNameWithoutExtension file))

module PreparedRunFiles =
    let private hash (bytes: byte array) : Guid<workItemId> = UMX.tag (Guid(SHA256.HashData(bytes).[0..15]))
    let internal saveWorkItem folder (value: runParameters) token =
        let payload = PreparedCodec.toDto value |> MessagePackSetup.serialize
        let id = hash payload
        PreparedStorage.publish folder "runParameters" (UMX.untag id) "runParameters" payload token
        id
    let loadWorkItem folder (id: Guid<workItemId>) =
        let payload = PreparedStorage.read folder "runParameters" (UMX.untag id) "runParameters"
        if hash payload <> id then invalidOp "The runParameters contents do not match their file ID."
        let value = MessagePackSetup.deserialize<runParametersDto> payload |> PreparedCodec.fromDto id
        PreparedStorage.requireProject folder value.ProjectId
        value
    let internal savePreparation folder (value: preparedRun) token =
        let dto: preparedRunDto = {
            Version = 1; Id = UMX.untag value.Id; ProjectId = UMX.untag value.ProjectId; RunId = UMX.untag value.RunId
            RunName = value.RunName; CreatedUtc = value.CreatedUtc; SetupSetCount = value.SetupSetCount
            WorkItemIds = value.WorkItemIds |> Array.map UMX.untag }
        PreparedStorage.publish folder "preparations" dto.Id "preparation" (MessagePackSetup.serialize dto) token
    let private readPreparation folder (id: Guid<preparationId>) =
        let dto = PreparedStorage.read folder "preparations" (UMX.untag id) "preparation" |> MessagePackSetup.deserialize<preparedRunDto>
        if dto.Version <> 1 || dto.Id <> UMX.untag id || dto.Id = Guid.Empty || dto.ProjectId = Guid.Empty || dto.RunId = Guid.Empty
           || isNull dto.RunName || dto.SetupSetCount < 0 || isNull dto.WorkItemIds || dto.WorkItemIds.Length = 0
           || (dto.WorkItemIds |> Array.exists ((=) Guid.Empty)) || (Array.distinct dto.WorkItemIds).Length <> dto.WorkItemIds.Length then
            invalidOp "Invalid or unsupported preparation manifest."
        let value = { Id = id; ProjectId = UMX.tag dto.ProjectId; RunId = UMX.tag dto.RunId; RunName = dto.RunName
                      CreatedUtc = dto.CreatedUtc; SetupSetCount = dto.SetupSetCount; WorkItemIds = dto.WorkItemIds |> Array.map UMX.tag }
        value
    let loadPreparation folder id =
        let value = readPreparation folder id
        PreparedStorage.requireProject folder value.ProjectId
        value
    let listPreparations folder =
        PreparedStorage.ids folder "preparations"
        |> Array.map (UMX.tag >> readPreparation folder)
        // Importing a project assigns new IDs but retains historical files. Those
        // preparations cannot execute here and must not hide new preparations.
        |> Array.filter (fun value -> File.Exists(PreparedStorage.path folder "" (UMX.untag value.ProjectId)))
        |> Array.sortByDescending (fun value -> value.CreatedUtc)
