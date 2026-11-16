namespace Sortopia.FileDb

open System
open System.IO
open System.Text
open System.Security.Cryptography
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.Core
open Sortopia.Sorting.Catalog

type internal decodedQueryKey = {
    ProjectId: Guid<projectId>
    IsSetup: bool
    DataType: sortopiaDataType
    Parameters: parameterBinding list
}

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
                | OpsWeightsValue _ | Seed6WeightsValue _ | UnfoldingWeightsValue _ | SorterSuccessFractionValue _ -> writer.Write binding.FormattedValue)
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

    /// Decode only the parameters actually included in a saved query projection.
    let internal readQuery (bytes: byte array) =
        use stream = new MemoryStream(bytes, false)
        use reader = new BinaryReader(stream, Encoding.UTF8)
        let requireTag expected =
            if reader.ReadString() <> expected then invalidOp "Unsupported stored query format."
        requireTag "sortopia-data-key/1"
        let projectId = Guid(reader.ReadBytes 16) |> UMX.tag<projectId>
        let database = reader.ReadString()
        let setup = reader.ReadBoolean()
        let functionName = reader.ReadString()
        let version = reader.ReadString()
        let kind = reader.ReadString()
        let dataType = SortopiaDataType.create kind (reader.ReadString())
        let count = reader.ReadInt32()
        if count < 0 || count > 4096 then invalidOp "Invalid stored query parameter count."
        let bindings = [ for _ in 1..count do
                            let name = reader.ReadString() |> UMX.tag<paramKeyName>
                            let valueType = enum<parameterValueType> (reader.ReadInt32())
                            let value =
                                match valueType with
                                | parameterValueType.Text -> reader.ReadString() |> TextValue
                                | parameterValueType.Integer -> reader.ReadInt32() |> IntegerValue
                                | parameterValueType.Unsigned64 -> reader.ReadUInt64() |> Unsigned64Value
                                | parameterValueType.Floating -> reader.ReadDouble() |> FloatingValue
                                | parameterValueType.Boolean -> reader.ReadBoolean() |> BooleanValue
                                | parameterValueType.Guid -> Guid(reader.ReadBytes 16) |> GuidValue
                                | parameterValueType.SorterLibId ->
                                    requireTag "Sortopia.Sorting.Catalog.SorterLibId/v1"
                                    let width = reader.ReadInt32() |> UMX.tag
                                    sorterLibId.create width (reader.ReadString() |> SorterLibVariant.fromString) |> SorterLibIdValue
                                | parameterValueType.PrefixLibId ->
                                    requireTag "Sortopia.Sorting.Catalog.PrefixLibId/v1"
                                    let width = reader.ReadInt32() |> UMX.tag
                                    let stages = reader.ReadInt32() |> UMX.tag
                                    prefixLibId.create width stages (reader.ReadString() |> PrefixLibVariant.fromString) |> PrefixLibIdValue
                                | parameterValueType.MergeLibId ->
                                    requireTag "Sortopia.Sorting.Catalog.MergeLibId/v1"
                                    let width = reader.ReadInt32() |> UMX.tag
                                    let dimension = reader.ReadInt32() |> UMX.tag
                                    mergeLibId.create width dimension (reader.ReadString() |> SorterLibVariant.fromString) |> MergeLibIdValue
                                | parameterValueType.OpsWeights | parameterValueType.Seed6Weights
                                | parameterValueType.UnfoldingWeights | parameterValueType.SorterSuccessFraction ->
                                    reader.ReadString() |> ParameterValue.parse valueType
                                | _ -> invalidOp "Unknown stored query parameter type."
                            yield ParameterBinding.fromValue name value ]
        if stream.Position <> stream.Length then invalidOp "Unexpected stored query metadata."
        let parameters = parameterSet.fromBindings bindings
        let canonical = create projectId database setup functionName version dataType (bindings |> List.map _.KeyName) parameters []
        if canonical.QueryBytes <> bytes then invalidOp "Stored query metadata is not canonical."
        { ProjectId = projectId; IsSetup = setup; DataType = dataType; Parameters = bindings }
