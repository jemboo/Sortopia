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

type dataFileInspection = { Kind: string; Description: string; Json: string }

/// Read-only projections for the file inspector. Never materializes functionData.
module FileInspection =
    let private json value = JsonSerializer.Serialize(value)
    let private maxMetadataBytes = 4 * 1024 * 1024

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
                         QueryParameterCount = queryParameterCount; DependencyCount = dependencyCount; First100Dependencies = dependencies
                         Compression = compression; CompressedPayloadBytes = payloadLength; UncompressedPayloadBytes = rawLength
                         PayloadSha256 = Convert.ToHexString(checksum).ToLowerInvariant() |} }

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
                let dto = MessagePackSetup.deserialize<runParametersDto> envelope.Payload
                if Guid(SHA256.HashData(envelope.Payload).[0..15]) <> id then invalidOp "Work item contents do not match the file ID."
                RunParameterCodec.fromDto (UMX.tag id) dto |> ignore
                let parameters = dto.Parameters |> Array.map (fun item ->
                    let binding = RunParameterCodec.readBinding item
                    {| Key = item.Key; Type = string binding.ValueType; Value = binding.FormattedValue |})
                { Kind = "Run parameters"; Description = "One immutable results invocation: typed parameter values, fixed outputs, and project-local setup inputs. Envelope checksum and work item ID verified."
                  Json = json {| SchemaVersion = dto.Version; Id = id; ProjectId = dto.ProjectId; RunId = dto.RunId
                                 FunctionName = dto.FunctionName; FunctionVersion = dto.FunctionVersion; DatabaseName = dto.DatabaseName
                                 SetNumber = dto.SetNumber; SetupSetNumber = Option.toNullable dto.SetupSetNumber
                                 Outputs = dto.Outputs; Parameters = parameters; SetupInputs = dto.SetupInputs |} }
            | kind -> invalidOp $"Unsupported execution metadata kind: {kind}"
