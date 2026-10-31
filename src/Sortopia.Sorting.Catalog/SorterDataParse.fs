namespace Sortopia.Sorting.Catalog

open Sortopia.Sorting.Networks

module SorterDataParse =
    /// Reads bracketed stages, preserving their order. Empty text has no stages; null and malformed text fail.
    let parseTo2dCeArray text = NetworkText.parse text

    /// Reads bracketed stages and flattens them in execution order.
    let parseToFlatCeArray text = NetworkText.parse text |> Array.concat

    /// Looks up a prefix and returns fresh comparator storage. Unknown valid keys return None.
    let getCeArrayFromPrefixLib key = PrefixLib.tryGet key |> Option.map parseToFlatCeArray

    /// Looks up a complete sorting network and returns fresh comparator storage.
    let getCeArrayFromSorterLib key = SorterLib.tryGet key |> Option.map parseToFlatCeArray

    /// Sorts each contiguous segment independently; this prepares merge inputs, not a full-width sorter.
    /// Stages contain shifted copies of the component network and have fresh storage on every call.
    let getCeArrayFromMergeLib (key: mergeLibId) =
        let sorterKey = sorterLibId.create key.FactorChannelCount key.SorterLibVariant
        SorterLib.tryGet sorterKey
        |> Option.map (parseTo2dCeArray >> Ce.mergeN key.MergeDimension key.ChannelCount)
