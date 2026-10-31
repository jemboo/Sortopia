namespace Sortopia.Sorting.Inputs

open System
open FSharp.UMX
open Sortopia.Sorting

/// Row-major integer inputs. Each consecutive ChannelCount values is one case.
[<Struct; StructuralEquality; NoComparison>]
type packedSortableIntTests =
    private { id: Guid<sortableTestsId>; channelCount: int<channelCount>; packedValues: int[] }

    /// Restores an identified dataset; a positive width is required even for an empty dataset.
    static member createWithId (id: Guid<sortableTestsId>) (width: int<channelCount>) (values: int[]) =
        if isNull values then nullArg "values"
        if int width < 1 then invalidArg "width" "Channel count must be positive."
        if values.Length % int width <> 0 then invalidArg "values" "Packed length must be a multiple of channel count."
        { id = id; channelCount = width; packedValues = Array.copy values }

    static member createFromPackedValues width values =
        packedSortableIntTests.createWithId (Guid.NewGuid() |> UMX.tag) width values

    static member create (width: int<channelCount>) (arrays: sortableIntArray[]) =
        if isNull arrays then nullArg "arrays"
        if arrays |> Array.exists (fun a -> a.ChannelCount <> width) then
            invalidArg "arrays" "Every input must match the dataset channel count."
        arrays |> Array.collect (fun a -> a.Values) |> packedSortableIntTests.createFromPackedValues width

    member this.Id = this.id
    member this.ChannelCount = this.channelCount
    member this.SortableDataFormat = sortableDataFormat.PackedIntArray
    member this.SortableCount = (this.packedValues.Length / int this.channelCount) |> UMX.tag<sortableCount>
    member this.PackedValues = Array.copy this.packedValues
