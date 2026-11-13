namespace Sortopia.FileDb

open System
open System.IO
open System.Text
open System.Security.Cryptography
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Core

/// Query identity selects a bucket; dependency identity selects an immutable datum.
type dataKey = private {
    reference: storedDataReference
    queryBytes: byte array
    identityBytes: byte array
} with
    member this.Reference = this.reference
    member this.Id = this.reference.Id
    member this.QueryId = this.reference.QueryId
    member this.QueryBytes = Array.copy this.queryBytes
    member this.IdentityBytes = Array.copy this.identityBytes

module DataKey =
    let internal hashId (bytes: byte array) : Guid<sortopiaDataId> =
        SHA256.HashData bytes |> Array.take 16 |> Guid |> UMX.tag

    let private encode action =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream, UTF8Encoding(false, true), true)
        action writer
        writer.Flush()
        stream.ToArray()

    /// Versioned canonical bytes are independent of culture, map insertion order,
    /// parameter formatting and the MessagePack implementation.
    let create (projectId: Guid<projectId>) (database: string) (isSetup: bool) (functionName: string) (functionVersion: string) dataType
               (keys: string<paramKeyName> list) (parameters: parameterSet) (inputs: storedDataReference list) =
        if UMX.untag projectId = Guid.Empty then invalidArg "projectId" "Project ID must not be empty."
        for value in [database; functionName; functionVersion] do
            if String.IsNullOrWhiteSpace value then invalidArg "scope" "Database and function names/version must not be blank."
        SortopiaDataType.validate dataType
        if keys.Length <> (Set.ofList keys).Count then invalidArg "keys" "Duplicate query key."
        let bindings =
            keys |> List.map (fun key ->
                parameters.ParamMap |> Map.tryFind key
                |> Option.defaultWith (fun () -> invalidArg "parameters" $"Data '{dataType}' requires query parameter '{key}'."))
            |> List.sortWith (fun a b -> StringComparer.Ordinal.Compare(UMX.untag a.KeyName, UMX.untag b.KeyName))
        let query = encode (fun writer ->
            writer.Write "sortopia-data-key/1"
            writer.Write((UMX.untag projectId).ToByteArray())
            writer.Write database
            writer.Write isSetup
            writer.Write functionName
            writer.Write functionVersion
            writer.Write(SortopiaDataType.kindName dataType)
            writer.Write(SortopiaDataType.text dataType)
            writer.Write bindings.Length
            for binding in bindings do
                writer.Write(UMX.untag binding.KeyName)
                writer.Write(int binding.ValueType)
                match binding.Value with
                | TextValue value -> writer.Write value
                | IntegerValue value -> writer.Write value
                | Unsigned64Value value -> writer.Write value
                | FloatingValue value -> writer.Write(if value = 0.0 then 0.0 else value)
                | BooleanValue value -> writer.Write value
                | GuidValue value -> writer.Write(value.ToByteArray())
                | SorterLibIdValue value -> (value :> IStableSerializable).WriteStableBytes writer
                | PrefixLibIdValue value -> (value :> IStableSerializable).WriteStableBytes writer
                | MergeLibIdValue value -> (value :> IStableSerializable).WriteStableBytes writer
                | OpsWeightsValue _ | Seed6WeightsValue _ | UnfoldingWeightsValue _ -> writer.Write binding.FormattedValue)
        let dependencies = inputs |> List.distinct |> List.sortBy (fun value -> value.ProjectId, value.IsSetup, value.QueryId, value.Id, value.DataType)
        let identity = encode (fun writer ->
            writer.Write query
            writer.Write dependencies.Length
            for input in dependencies do
                writer.Write((UMX.untag input.ProjectId).ToByteArray())
                writer.Write((UMX.untag input.Id).ToByteArray())
                writer.Write((UMX.untag input.QueryId).ToByteArray())
                writer.Write input.IsSetup
                writer.Write(SortopiaDataType.kindName input.DataType)
                writer.Write(SortopiaDataType.text input.DataType))
        { reference = { ProjectId = projectId; Id = hashId identity; QueryId = hashId query; IsSetup = isSetup; DataType = dataType }
          queryBytes = query; identityBytes = identity }
