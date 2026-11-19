namespace Sortopia.Evaluation

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks

type ceBlockEval =
    private {
        ceBlock: ceBlock
        ceUseCounts: ceUseCounts
        usedCes: Lazy<ce array>
        unsortedCount: int<sortableCount>
        sortableTests: sortableTests option
    }

    static member create
            (ceBlock: ceBlock)
            (ceUseCts: ceUseCounts)
            (unsortedCount: int<sortableCount>)
            (sortableTests: sortableTests option) =
        if ceUseCts.Length <> %ceBlock.CeLength then invalidArg "ceUseCts" "There must be one usage count per CE."
        if %unsortedCount < 0 then invalidArg "unsortedCount" "Count must be nonnegative."
        sortableTests |> Option.iter (fun tests ->
            if SortableTests.getChannelCount tests <> ceBlock.ChannelCount then invalidArg "sortableTests" "Residual input width must match the network.")
        let ceUseCts = ceUseCounts.CreateFromArray(ceUseCts.ToArray())
        {
            ceBlock = ceBlock
            ceUseCounts = ceUseCts
            usedCes = Lazy<ce[]>(fun () -> ceBlockEval.getUsedCes ceBlock ceUseCts)
            unsortedCount = unsortedCount
            sortableTests = sortableTests
        }

    member this.CeBlock with get() = this.ceBlock

    member this.LastUsedIndex with get() : int<ceIndex> =
        this.ceUseCounts.LastUsedCeIndex

    member this.UseCountArray with get() : int array = this.ceUseCounts.ToArray()

    member this.CeLength with get() : int<ceLength> =
        this.ceUseCounts.UsedCeCount

    static member getUsedCes (ceb: ceBlock) (useCounts: ceUseCounts) : ce[] =
        let used = ResizeArray<ce>()
        for i in 0 .. (%ceb.CeLength - 1) do
            if useCounts.[i |> UMX.tag<ceIndex>] <> 0 then
                used.Add(ceb.getCe i)
        used.ToArray()

    member this.SortableTests with get() = this.sortableTests

    member this.UnsortedCount with get() = this.unsortedCount

    member this.UsedCes with get() : ce array = Array.copy this.usedCes.Value

    member this.extractCeUseArray : ceUse array =
        let results = ResizeArray<ceUse>()
        for i in 0 .. (%this.CeBlock.CeLength - 1) do
            let idx = i |> UMX.tag<ceIndex>
            let count = this.ceUseCounts.[idx]
            if count > 0 then
                results.Add(ceUse.create idx count (this.CeBlock.getCe i))
        results.ToArray()
