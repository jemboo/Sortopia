namespace Sortopia.FileDb

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Security.Cryptography
open MessagePack
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Core.Serialization

type storedData = { Reference: storedDataReference; Value: functionData }

type ISortopiaDb =
    abstract TryLoad: dataKey * CancellationToken -> Result<storedData option, string>
    abstract Load: storedDataReference * CancellationToken -> Result<storedData, string>
    abstract FindResults: dataKey * CancellationToken -> Result<storedData list, string>
    abstract GetOrCreate: dataKey * (unit -> Result<functionData, string>) * CancellationToken -> Result<storedData * bool, string>

/// Immutable data files. Each writer holds a project lock only for get-or-create;
/// readers need no lock, and incomplete temporary files are never considered data.
type SortopiaFileDb(projectFolder: string, projectId: Guid<projectId>) =
    let projectFolder = Path.GetFullPath projectFolder
    let dataFolder = Path.Combine(projectFolder, "data")
    let protect action =
        try Ok (action ()) with
        | :? OperationCanceledException -> reraise ()
        | error -> Error (error.GetBaseException().Message)
    let require = function Ok value -> value | Error message -> invalidOp message
    let checkPath (path: string) =
        let full = Path.GetFullPath path
        if not (full.StartsWith(projectFolder + string Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) then
            invalidOp "Data path must be inside its project folder."
        let mutable current = full
        while current.Length >= projectFolder.Length do
            if (File.Exists current || Directory.Exists current) && File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint) then
                invalidOp "Data storage does not follow filesystem links."
            current <- Path.GetDirectoryName current
    let typeFolder (reference: storedDataReference) =
        if reference.ProjectId <> projectId then invalidArg "reference" "The data belongs to a different project."
        Path.Combine(dataFolder, (if reference.IsSetup then "setup" else "results"), SortopiaDataType.kindName reference.DataType)
    let filePath reference =
        let path = Path.Combine(typeFolder reference, string reference.Id + ".msgpack")
        checkPath path
        path
    let read reference (expectedQuery: byte array option) (expectedIdentity: byte array option) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let path = filePath reference
        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
        if stream.Length > int64 DataCodec.maxBytes then invalidOp $"Stored data is too large: {path}"
        let bytes = Array.zeroCreate<byte> (int stream.Length)
        stream.ReadExactly(bytes)
        let dto = MessagePackSetup.deserialize<dataEnvelopeDto> bytes
        if isNull (box dto) || dto.Version <> 1 || dto.Compression <> "messagepack-lz4-array/1" then
            invalidOp $"Unsupported stored-data schema or compression: {path}"
        if dto.ProjectId <> UMX.untag reference.ProjectId || dto.Id <> UMX.untag reference.Id
           || dto.QueryId <> UMX.untag reference.QueryId || dto.IsSetup <> reference.IsSetup
           || dto.Kind <> SortopiaDataType.kindName reference.DataType || dto.Label <> SortopiaDataType.text reference.DataType then
            invalidOp $"Stored data identity does not match its reference: {path}"
        if isNull dto.QueryBytes || isNull dto.IdentityBytes || isNull dto.Payload || isNull dto.Checksum then
            invalidOp $"Incomplete stored data: {path}"
        if DataKey.hashId dto.QueryBytes <> reference.QueryId || DataKey.hashId dto.IdentityBytes <> reference.Id
           || dto.IdentityBytes.Length < dto.QueryBytes.Length
           || not (dto.IdentityBytes.AsSpan().StartsWith(dto.QueryBytes.AsSpan())) then
            invalidOp $"Stored data key is corrupt: {path}"
        expectedQuery |> Option.iter (fun query ->
            if query <> dto.QueryBytes then
                invalidOp $"Stored query key collision: {path}")
        expectedIdentity |> Option.iter (fun identity ->
            if identity <> dto.IdentityBytes then
                invalidOp $"Stored data key collision: {path}")
        if SHA256.HashData dto.Payload <> dto.Checksum then invalidOp $"Stored data checksum failed: {path}"
        token.ThrowIfCancellationRequested()
        { Reference = reference; Value = DataCodec.decode dto.Kind dto.RawLength dto.Payload }
    let tryRead (key: dataKey) token =
        try Some (read key.Reference (Some key.QueryBytes) (Some key.IdentityBytes) token) with
        | :? FileNotFoundException | :? DirectoryNotFoundException -> None
    let acquireWriter (token: CancellationToken) =
        let lockPath = Path.Combine(dataFolder, ".write.lock")
        checkPath lockPath
        Directory.CreateDirectory dataFolder |> ignore
        let timer = Stopwatch.StartNew()
        let mutable handle = None
        while handle.IsNone do
            token.ThrowIfCancellationRequested()
            try handle <- Some (new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            with :? IOException when timer.Elapsed < TimeSpan.FromSeconds 30.0 ->
                token.WaitHandle.WaitOne(25) |> ignore
        handle.Value
    let publish (key: dataKey) value (token: CancellationToken) =
        if FunctionData.kind value <> SortopiaDataType.kindName key.Reference.DataType then
            invalidOp $"Function returned '{FunctionData.kind value}' for '{key.Reference.DataType}'."
        let payload, rawLength = DataCodec.encode value
        let reference = key.Reference
        let dto = {
            Version = 1; ProjectId = UMX.untag projectId; Id = UMX.untag key.Id; QueryId = UMX.untag key.QueryId
            IsSetup = reference.IsSetup; Kind = SortopiaDataType.kindName reference.DataType; Label = SortopiaDataType.text reference.DataType
            QueryBytes = key.QueryBytes; IdentityBytes = key.IdentityBytes; Compression = "messagepack-lz4-array/1"
            RawLength = rawLength; Checksum = SHA256.HashData payload; Payload = payload }
        let bytes = MessagePackSetup.serialize dto
        if bytes.Length > DataCodec.maxBytes then invalidOp "The stored data exceeds the 256 MiB file limit."
        let destination = filePath reference
        Directory.CreateDirectory(Path.GetDirectoryName destination) |> ignore
        let temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            do
                use stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)
                stream.Write bytes
                stream.Flush(true)
            token.ThrowIfCancellationRequested()
            File.Move(temporary, destination, false)
        finally
            if File.Exists temporary then File.Delete temporary
        { Reference = reference; Value = value }

    member _.ProjectFolder = projectFolder
    member _.GetPath reference = filePath reference

    interface ISortopiaDb with
        member _.TryLoad(key, token) = protect (fun () -> tryRead key token)
        member _.Load(reference, token) = protect (fun () -> read reference None None token)
        member _.FindResults(key, token) = protect (fun () ->
            if key.Reference.IsSetup then invalidArg "key" "Source queries address results data."
            let folder = typeFolder key.Reference
            checkPath folder
            if not (Directory.Exists folder) then []
            else
                Directory.EnumerateFiles(folder, "*.msgpack") |> Seq.sort |> Seq.choose (fun path ->
                    token.ThrowIfCancellationRequested()
                    checkPath path
                    let header = (DataEnvelope.readHeader path token).Envelope
                    let id = Guid.Parse(Path.GetFileNameWithoutExtension path)
                    if header.ProjectId <> UMX.untag projectId || header.IsSetup || header.Id <> id
                       || header.Kind <> SortopiaDataType.kindName key.Reference.DataType then
                        invalidOp $"Stored data does not match its type folder: {path}"
                    if header.QueryId <> UMX.untag key.QueryId then None
                    else
                        let reference = { key.Reference with Id = UMX.tag<sortopiaDataId> id }
                        Some (read reference (Some key.QueryBytes) None token)) |> Seq.toList)
        member _.GetOrCreate(key, factory, token) = protect (fun () ->
            match tryRead key token with
            | Some value -> value, true
            | None when key.Reference.IsSetup ->
                use writer = acquireWriter token
                // Another process may have published while this writer waited.
                match tryRead key token with
                | Some value -> value, true
                | None ->
                    token.ThrowIfCancellationRequested()
                    let value = factory () |> require
                    token.ThrowIfCancellationRequested()
                    publish key value token, false
            | None ->
                // Results jobs own distinct outputs. Do not hold the setup writer
                // lock during computation, compression, or independent publication.
                token.ThrowIfCancellationRequested()
                let value = factory () |> require
                token.ThrowIfCancellationRequested()
                publish key value token, false)
