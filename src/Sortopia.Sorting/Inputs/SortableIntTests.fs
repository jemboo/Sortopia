namespace Sortopia.Sorting.Inputs

open System
open FSharp.UMX
open Sortopia.Sorting

[<Struct; StructuralEquality; NoComparison>]
type sortableIntTests =
    private { id: Guid<sortableTestsId>
              channelCount: int<channelCount>
              sortableIntArrays: sortableIntArray[]
            }

    static member create 
                    (id: Guid<sortableTestsId>) 
                    (channelCount:int<channelCount>)
                    (arrays: sortableIntArray[]) : sortableIntTests =
        if isNull arrays then nullArg "arrays"
        if int channelCount < 0 then invalidArg "channelCount" "Channel count must be non-negative."
        if arrays |> Array.exists (fun a -> a.ChannelCount <> channelCount) then
            invalidArg "arrays" "Every input must match the dataset channel count."
        { 
            id = id; 
            channelCount = channelCount;
            sortableIntArrays = Array.copy arrays
        }

    static member Empty =
        let id = Guid.NewGuid() |> UMX.tag<sortableTestsId>
        sortableIntTests.create id 0<channelCount> [||]

    member this.SortableDataFormat with get() = sortableDataFormat.IntArray

    member this.SortableCount with get() = this.sortableIntArrays.Length |> UMX.tag<sortableCount>

    member this.Id with get() = this.id
    
    member this.ChannelCount with get() = this.channelCount

    member this.SortableIntArrays with get() = Array.copy this.sortableIntArrays

