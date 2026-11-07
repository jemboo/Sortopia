namespace Sortopia.Models.Sorting.Simple

open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting

/// Shared ownership, identity, and materialization rules for a fixed model prefix.
module internal ModelPrefix =
    /// Fixed points are idle channels, including the final channel of an odd-width involution.
    let expandPermutation (permutation: permSi) =
        permutation.Array
        |> Array.mapi (fun low high -> if low < high then Some (ce.create low high) else None)
        |> Array.choose id

    let snapshot (channelCount: int<channelCount>) (prefix: ce array) =
        if isNull prefix then nullArg "prefix"
        let copy = Array.copy prefix
        if copy |> Array.exists (fun ce -> int ce.HighChannel >= int channelCount) then
            invalidArg "prefix" "Prefix comparator endpoints must be within the model's channel count."
        copy

    /// Include the length and ordered canonical CE codes, never an array reference hash.
    let hashValues (prefix: ce array) =
        seq {
            yield box prefix.Length
            for ce in prefix do yield box (Ce.toIndex ce)
        }

    let truncate (maxCeLength: int<ceLength> option) (ces: ce array) =
        match maxCeLength with
        | Some length when int length < 0 -> invalidArg "maxCeLength" "Suffix CE limit must be nonnegative."
        | Some length -> Array.truncate (int length) ces
        | None -> ces

    /// IDs distinguish materialized sequences while retaining the source model ID in their derivation.
    let materialize (modelId: Guid<sorterModelId>) (channelCount: int<channelCount>)
                   (prefix: ce array) includePrefix (suffix: ce array) =
        let ces = if includePrefix then Array.append prefix suffix else suffix
        let id =
            seq {
                yield box "Sortopia.Models.Sorting/materializedSorter/v1"
                yield box (UMX.untag modelId)
                yield box (int channelCount)
                // Empty prefixes have identical full and suffix forms.
                yield box (includePrefix && prefix.Length > 0)
                yield! hashValues prefix
                yield! hashValues suffix
            }
            |> GuidUtils.guidFromObjs |> UMX.tag<sorterId>
        sorter.create id channelCount ces
