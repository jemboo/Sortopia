namespace Sortopia.Sorting.Networks

open System
open System.Collections.Generic
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks

[<Struct; StructuralEquality; NoComparison>]
type sorter =
    private { 
        sorterId: Guid<sorterId>
        channelCount: int<channelCount>
        ces: ce array

    }

    static member create (sorterId: Guid<sorterId>) (width: int<channelCount>) (ces: ce array) : sorter =
        if %sorterId = Guid.Empty then
            invalidArg "sorterId" "Sorter ID must not be empty"
        if %width < 1 then
            invalidArg "width" "Width must be at least 1"
        if isNull ces then nullArg "ces"
        if ces |> Array.exists (fun c -> int c.HighChannel >= int width) then
            invalidArg "ces" "Comparator endpoints must be within the network width."
        { sorterId = sorterId; channelCount = width; ces = Array.copy ces }

    override this.ToString() : string =
        sprintf "Sorter(Id=%A, Width=%d)" (%this.sorterId) (%this.channelCount)

    member this.SorterId with get() = this.sorterId
    member this.ChannelCount with get() = this.channelCount
    member this.Ce (dex : int) = this.ces.[dex]
    member this.ceLength with get() : int<ceLength> = this.ces.Length |> UMX.tag<ceLength>
    member this.Ces with get() = Array.copy this.ces

