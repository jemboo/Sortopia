namespace Sortopia.Sorting.Inputs

open System
open FSharp.UMX
open Sortopia.Sorting

[<Struct; StructuralEquality; NoComparison>]
type sortableBinaryTests =
    private { id: Guid<sortableTestsId>
              channelCount: int<channelCount>
              sortableBinaryArrays: sortableBoolArray[]
            }

    static member create 
                    (id: Guid<sortableTestsId>) 
                    (channelCount:int<channelCount>)
                    (arrays: sortableBoolArray[]) : sortableBinaryTests =
        if isNull arrays then nullArg "arrays"
        if int channelCount < 0 then invalidArg "channelCount" "Channel count must be non-negative."
        if arrays |> Array.exists (fun a -> a.ChannelCount <> channelCount) then
            invalidArg "arrays" "Every input must match the dataset channel count."
        { 
            id = id; 
            channelCount = channelCount;
            sortableBinaryArrays = Array.copy arrays
        }

    static member Empty =
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        sortableBinaryTests.create id 0<channelCount> [||]

    member this.SortableCount with get() = this.sortableBinaryArrays.Length  |> UMX.tag<sortableCount>

    member this.Id with get() = this.id
    
    member this.SortableArrayType with get() = sortableDataFormat.BoolArray

    member this.ChannelCount with get() = this.channelCount

    member this.SortableBinaryArrays with get() = Array.copy this.sortableBinaryArrays

