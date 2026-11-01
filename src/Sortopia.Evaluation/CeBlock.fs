namespace Sortopia.Evaluation

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks

type ceBlock =
    private {
        ces: ce array
        ceBlockId: Guid<ceBlockId>
        channelCount: int<channelCount>
    }

    static member create
            (ceBlockId: Guid<ceBlockId>)
            (channelCount: int<channelCount>)
            (ces: ce array)  =
            if isNull ces then nullArg "ces"
            if %channelCount < 0 || (%channelCount = 0 && ces.Length > 0) then invalidArg "channelCount" "A nonempty block requires positive width."
            if ces |> Array.exists (fun c -> int c.HighChannel >= %channelCount) then invalidArg "ces" "CE endpoint exceeds the channel count."
            {
                ceBlockId = ceBlockId;
                ces = Array.copy ces;
                channelCount = channelCount
            }

    static member Empty = ceBlock.create (Guid.Empty |> UMX.tag<ceBlockId>) (0 |> UMX.tag<channelCount>) [||]

    member this.getCe (dex:int) =
        this.ces.[dex]

    member this.CeArray with get() = Array.copy this.ces

    member this.CeBlockId with get() = this.ceBlockId

    member this.CeLength with get() = this.ces.Length |> UMX.tag<ceLength>

    member this.ChannelCount with get() = this.channelCount

    member this.permute (perm:permutation) : ceBlock =
        ceBlock.create
            (Guid.NewGuid() |> UMX.tag)
            this.channelCount
            (this.ces |> Array.map(Ce.permute perm))


module CeBlock =

    let internal validateWidth (width: int<channelCount>) (prefix: ceBlock) (block: ceBlock) =
        if block.ChannelCount <> width then invalidArg "block" "Network and input widths must match."
        if %prefix.ChannelCount <> 0 && prefix.ChannelCount <> width then invalidArg "prefix" "Prefix and input widths must match."

    let breakUpIntoBlocks (channelCount: int<channelCount>) (ces:ce[]) (blockLength:int<ceLength>) =
        if %blockLength <= 0 then invalidArg "blockLength" "Block length must be positive."
        if isNull ces then nullArg "ces"
        let totalCes = ces.Length
        let blocks =
            [|
                let mutable index = 0
                while index < totalCes do
                    let blockCes = ces.[index .. Math.Min(index + %blockLength - 1, totalCes - 1)]
                    yield ceBlock.create (Guid.NewGuid() |> UMX.tag) channelCount blockCes
                    index <- index + %blockLength
            |]
        blocks


    let fromSorterSuffix (sorter:sorter) (offset:int<ceLength>) =
        if %offset < 0 || offset > sorter.ceLength then invalidArg "offset" "Offset must lie within the network."
        ceBlock.create
            (UMX.tag<ceBlockId> (Guid.NewGuid()))
            sorter.ChannelCount
            (sorter.Ces.[%offset .. %sorter.ceLength - 1 |> int])


    let fromSorter (sorter:sorter) =
        ceBlock.create
            (UMX.tag<ceBlockId> (Guid.NewGuid()))
            sorter.ChannelCount
            sorter.Ces
