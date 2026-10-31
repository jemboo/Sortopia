namespace Sortopia.Sorting.Catalog

open System
open FSharp.UMX
open Sortopia.Sorting

module MergeLibId =
    /// Every construction at this width whose component sorting network exists in the catalog.
    /// A dimension of one uses the component network unchanged. Nonpositive widths have no choices.
    let availableForChannelCount (width: int<channelCount>) =
        SorterLib.ids ()
        |> Array.choose (fun sorter ->
            if int width > 0 && int width % int sorter.ChannelCount = 0 then
                Some (mergeLibId.create width (UMX.tag (int width / int sorter.ChannelCount)) sorter.SorterVariant)
            else None)
        |> Array.sortBy (fun id -> id.MergeDimension, id.SorterLibVariant)

    let toString (key: mergeLibId) =
        FormattableString.Invariant($"MergeLibId(channelCount={int key.ChannelCount}, mergeDimension={int key.MergeDimension}, Variant={SorterLibVariant.toString key.SorterLibVariant})")

    let fromString text =
        let fields = IdText.read @"MergeLibId\(channelCount=([0-9]+),\s*mergeDimension=([0-9]+),\s*Variant=(Variant[ABC]|Batcher|BatcherBitonic)\)" text
        mergeLibId.create (UMX.tag (IdText.number fields.[1])) (UMX.tag (IdText.number fields.[2])) (SorterLibVariant.fromString fields.[3].Value)
