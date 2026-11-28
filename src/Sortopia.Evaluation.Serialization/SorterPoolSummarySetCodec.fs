namespace Sortopia.Evaluation.Serialization

open System
open MessagePack
open MessagePack.Formatters
open MessagePack.Resolvers
open Sortopia.Core.Serialization
open Sortopia.Evaluation.Sgd

/// Histogram bins occupy three integer slots: CE length, stage length, count.
type private SorterLengthHistogramBinFormatter() =
    interface IMessagePackFormatter<sorterLengthHistogramBin> with
        member _.Serialize(writer: byref<MessagePackWriter>, value, _) =
            writer.WriteArrayHeader 3
            writer.Write value.CeLength
            writer.Write value.StageLength
            writer.Write value.Count

        member _.Deserialize(reader: byref<MessagePackReader>, options) =
            options.Security.DepthStep(&reader)
            try
                if reader.ReadArrayHeader() <> 3 then
                    raise (MessagePackSerializationException "A histogram bin must contain CE length, stage length and count.")
                { CeLength = reader.ReadInt32(); StageLength = reader.ReadInt32(); Count = reader.ReadInt32() }
            finally
                reader.Depth <- reader.Depth - 1

module SorterPoolSummarySetCodec =
    let private options =
        let resolver = CompositeResolver.Create(
            [| SorterLengthHistogramBinFormatter() :> IMessagePackFormatter |],
            [| MessagePackSetup.options.Resolver |])
        MessagePackSetup.options.WithResolver resolver

    let encode value =
        let value = SorterPoolSummarySet.validate value
        MessagePackSerializer.Serialize(value, options)

    let decode (bytes: byte array) =
        MessagePackSerializer.Deserialize<sorterPoolSummarySet>(ReadOnlyMemory<byte>(bytes), options)
        |> SorterPoolSummarySet.validate
