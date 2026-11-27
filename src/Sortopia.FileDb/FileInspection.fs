namespace Sortopia.FileDb

open System
open System.Buffers
open System.IO
open System.Security.Cryptography
open System.Text.Json
open System.Threading
open MessagePack
open FSharp.UMX
open Sortopia.Core.Serialization
open Sortopia.Runs
open Sortopia.Functions

type dataFileInspection = { Kind: string; Description: string; Json: string }
type dataFileQuery = { DataType: sortopiaDataType; Parameters: parameterBinding list }

/// Read-only file inspection. Metadata stays lightweight; pool histograms have an explicit payload reader.
module FileInspection =
    let private json value = JsonSerializer.Serialize(value)
    let private maxMetadataBytes = 4 * 1024 * 1024

    /// Typed column values from the saved query, without decoding the data payload.
    let readDataQuery path (token: CancellationToken) =
        let header = (DataEnvelope.readHeader path token).Envelope
        let query = DataKey.readQuery header.QueryBytes
        if UMX.untag query.ProjectId <> header.ProjectId || RunExecutionPhase.toIsSetup query.Phase <> header.IsSetup
           || SortopiaDataType.kindName query.DataType <> header.Kind || SortopiaDataType.text query.DataType <> header.Label then
            invalidOp "Stored query metadata does not match its envelope."
        token.ThrowIfCancellationRequested()
        { DataType = query.DataType; Parameters = query.Parameters }

    let inspectData path (token: CancellationToken) =
        let header = DataEnvelope.readHeader path token
        let dto = header.Envelope
        let version, projectId, id, queryId = dto.Version, dto.ProjectId, dto.Id, dto.QueryId
        let isSetup, kind, label = dto.IsSetup, dto.Kind, dto.Label
        let query, identity = dto.QueryBytes, dto.IdentityBytes
        let compression, rawLength, checksum = dto.Compression, dto.RawLength, dto.Checksum
        let payloadLength = header.PayloadLength
        use queryStream = new MemoryStream(query, false)
        use queryReader = new BinaryReader(queryStream)
        if queryReader.ReadString() <> "sortopia-data-key/1" then invalidOp "Unsupported query key format."
        if Guid(queryReader.ReadBytes 16) <> projectId then invalidOp "Query project ID does not match the envelope."
        let database = queryReader.ReadString()
        if queryReader.ReadBoolean() <> isSetup then invalidOp "Query phase does not match the envelope."
        let functionName = queryReader.ReadString()
        let functionVersion = queryReader.ReadString()
        if queryReader.ReadString() <> kind || queryReader.ReadString() <> label then invalidOp "Query data type does not match the envelope."
        let queryParameterCount = queryReader.ReadInt32()
        let parameters = (DataKey.readQuery query).Parameters |> List.map (fun binding ->
            {| Key = binding.KeyName; Type = string binding.ValueType; Value = binding.FormattedValue |})
        use identityStream = new MemoryStream(identity, false)
        identityStream.Position <- int64 query.Length
        use identityReader = new BinaryReader(identityStream)
        let dependencyCount = identityReader.ReadInt32()
        if dependencyCount < 0 || queryParameterCount < 0 then invalidOp "Invalid key metadata counts."
        let dependencies = [|
            for _ in 1 .. min dependencyCount 100 do
                let project = Guid(identityReader.ReadBytes 16)
                let data = Guid(identityReader.ReadBytes 16)
                let query = Guid(identityReader.ReadBytes 16)
                let setup = identityReader.ReadBoolean()
                let kind = identityReader.ReadString()
                let label = identityReader.ReadString()
                {| ProjectId = project; DataId = data; QueryId = query; Phase = if setup then "Setup" else "Results"
                   DataType = kind; Label = label |} |]
        { Kind = if isSetup then "Setup data" else "Results data"
          Description = "Summary only. Envelope length and identity hashes checked. The compressed payload was not decoded or checksum-verified; use Check outputs to validate execution data."
          Json = json {| SchemaVersion = version; ProjectId = projectId; DataId = id; QueryId = queryId
                         DataType = kind; Label = label; Database = database; FunctionName = functionName; FunctionVersion = functionVersion
                         QueryParameterCount = queryParameterCount; QueryParameters = parameters
                         DependencyCount = dependencyCount; First100Dependencies = dependencies
                         Compression = compression; CompressedPayloadBytes = payloadLength; UncompressedPayloadBytes = rawLength
                         PayloadSha256 = Convert.ToHexString(checksum).ToLowerInvariant() |} }

    /// Decode only a selected pool-summary payload using the database's identity, checksum and domain validation.
    let loadPoolSummary path (token: CancellationToken) =
        let path = Path.GetFullPath path
        let header = (DataEnvelope.readHeader path token).Envelope
        if header.Kind <> "SorterPoolSummarySet" then invalidArg "path" "Select a SorterPoolSummarySet data file."
        let reference =
            { ProjectId = UMX.tag header.ProjectId; Id = UMX.tag header.Id; QueryId = UMX.tag header.QueryId
              Phase = RunExecutionPhase.fromIsSetup header.IsSetup; DataType = sortopiaDataType.SorterPoolSummarySet header.Label }
        let project = path |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName
        let store = SortopiaFileDb(project, reference.ProjectId)
        if not (String.Equals(store.GetPath reference, path, StringComparison.OrdinalIgnoreCase)) then
            invalidArg "path" "The summary filename and data folder must match its stored identity."
        let loaded = (store :> ISortopiaDb).Load(reference, token)
        token.ThrowIfCancellationRequested()
        match loaded with
        | Ok { Value = sortopiaStorageData.SorterPoolSummarySet summary } -> summary
        | Ok _ -> invalidOp "Expected a pool summary set."
        | Error message -> invalidOp message

    let inspectPrepared path (token: CancellationToken) =
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length > int64 maxMetadataBytes then
            { Kind = "Large execution metadata"; Description = "Detailed decoding is limited to 4 MiB files. The contents were not loaded."; Json = "" }
        else
            let bytes = Array.zeroCreate<byte> (int stream.Length)
            stream.ReadExactly bytes
            token.ThrowIfCancellationRequested()
            let options = MessagePackSetup.options.WithCompression(MessagePackCompression.Lz4BlockArray)
                            .WithSecurity(MessagePackSetup.options.Security.WithMaximumDecompressedSize(16 * 1024 * 1024).WithMaximumObjectGraphDepth(64))
            let envelope = MessagePackSerializer.Deserialize<runParametersEnvelopeDto>(ReadOnlyMemory<byte>(bytes), options, token)
            if isNull (box envelope) || envelope.Version <> 1 || isNull envelope.Payload || isNull envelope.Checksum
               || SHA256.HashData envelope.Payload <> envelope.Checksum then invalidOp "Invalid execution metadata envelope or checksum."
            let id = Guid.Parse(Path.GetFileNameWithoutExtension path)
            match envelope.Kind with
            | "runParameters" ->
                if Guid(SHA256.HashData(envelope.Payload).[0..15]) <> id then invalidOp "Work item contents do not match the file ID."
                let item = RunParameterCodec.decode (UMX.tag id) envelope.Payload
                let dto = RunParameterCodec.toDto item
                let parameters = dto.Parameters |> Array.map (fun item ->
                    let binding = RunParameterCodec.readBinding item
                    {| Key = item.Key; Type = string binding.ValueType; Value = binding.FormattedValue |})
                { Kind = "Run parameters"; Description = "One immutable results invocation: typed parameter values, fixed outputs, and project-local setup inputs. Envelope checksum and work item ID verified."
                  Json = json {| SchemaVersion = (if item.Sgd.IsSome then 4 elif item.Reporting.IsSome then 3 else dto.Version)
                                 SnapshotInterval = item.Sgd |> Option.map _.SnapshotInterval |> Option.toNullable
                                 SnapshotCountSource = if item.Sgd.IsSome then $"runs/{dto.RunId}.msgpack" else ""
                                 Reporting = item.Reporting |> Option.defaultValue Unchecked.defaultof<_>; Id = id; ProjectId = dto.ProjectId; RunId = dto.RunId
                                 FunctionName = dto.FunctionName; FunctionVersion = dto.FunctionVersion; DatabaseName = dto.DatabaseName
                                 SetNumber = dto.SetNumber; SetupSetNumber = Option.toNullable dto.SetupSetNumber
                                 SetupSuffixStages = Option.toNullable dto.SetupSuffixStages; ResultsSuffixStages = Option.toNullable dto.ResultsSuffixStages
                                 Outputs = dto.Outputs; Parameters = parameters; SetupInputs = dto.SetupInputs |} }
            | kind -> invalidOp $"Unsupported execution metadata kind: {kind}"

    /// Match output identities, not filenames or the run that merely consumes a setup file.
    /// Equivalent invocations may share an output, so retain every matching invocation.
    let producingParameters path (token: CancellationToken) =
        let path = Path.GetFullPath path
        let category = Path.GetFileName(Path.GetDirectoryName path)
        if category.Equals("runParameters", StringComparison.OrdinalIgnoreCase) then
            [| RunParameterFiles.loadFile path |]
        else
            let project = path |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName
            let parameters = Path.Combine(project, "runParameters")
            if not (Directory.Exists parameters) then [||] else
            Directory.EnumerateFiles(parameters, "*.msgpack") |> Seq.choose (fun file ->
                token.ThrowIfCancellationRequested()
                try
                    let item = RunParameterFiles.loadFile file
                    let store = SortopiaFileDb(project, item.ProjectId)
                    let matches =
                        if category.Equals("temporarySnapshots", StringComparison.OrdinalIgnoreCase) then
                            item.Sgd.IsSome && String.Equals(RunOutputFiles.temporarySnapshotPath project item, path, StringComparison.OrdinalIgnoreCase)
                        else RunOutputFiles.keys item |> Array.exists (fun key -> String.Equals(store.GetPath key.Reference, path, StringComparison.OrdinalIgnoreCase))
                    if matches then Some item else None
                with
                | :? OperationCanceledException -> reraise ()
                | _ -> None) |> Seq.toArray

    let inspectTemporary path (token: CancellationToken) =
        let items = producingParameters path token
        if Array.isEmpty items then
            { Kind = "Temporary snapshot"; Description = "No matching saved invocation was found. This replaceable checkpoint is used when resuming evolution."; Json = "" }
        else
            let item = items[0]
            let project = path |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName |> Path.GetDirectoryName
            let anchor = snd (RunOutputFiles.samples item).Snapshots[0]
            let generation = TemporarySnapshots.tryGeneration project (TemporarySnapshots.identity anchor item.Reporting) token
            { Kind = "Temporary snapshot"; Description = "Replaceable evolution checkpoint. Snapshot header checked; population payload was not decoded."
              Json = json {| Generation = Option.toNullable generation; ProjectId = UMX.untag item.ProjectId
                             RunIds = items |> Array.map (fun item -> UMX.untag item.RunId) |> Array.distinct |} }
