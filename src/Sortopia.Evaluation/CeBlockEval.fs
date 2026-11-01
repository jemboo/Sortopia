namespace Sortopia.Evaluation

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks

type ceBlockEval =
    private {
        prefix: ceBlock
        ceBlock: ceBlock
        ceUseCounts: ceUseCounts // Does not track the prefix CEs
        usedCes: Lazy<ce array>
        unsortedCount: int<sortableCount>
        sortableTests: sortableTests option
    }

    static member create
            (prefix: ceBlock)
            (ceBlock: ceBlock)
            (ceUseCts: ceUseCounts)
            (unsortedCount: int<sortableCount>)
            (sortableTests: sortableTests option) =
        if ceUseCts.Length <> %ceBlock.CeLength then invalidArg "ceUseCts" "There must be one usage count per CE."
        if %unsortedCount < 0 then invalidArg "unsortedCount" "Count must be nonnegative."
        if %prefix.ChannelCount <> 0 && prefix.ChannelCount <> ceBlock.ChannelCount then invalidArg "prefix" "Prefix and block widths must match."
        sortableTests |> Option.iter (fun tests ->
            if SortableTests.getChannelCount tests <> ceBlock.ChannelCount then invalidArg "sortableTests" "Residual input width must match the network.")
        let ceUseCts = ceUseCounts.CreateFromArray(ceUseCts.ToArray())
        {
            prefix = prefix
            ceBlock = ceBlock
            ceUseCounts = ceUseCts
            usedCes = Lazy<ce[]>(fun () -> ceBlockEval.getUsedCes prefix ceBlock ceUseCts)
            unsortedCount = unsortedCount
            sortableTests = sortableTests
        }

    member this.Prefix with get() = this.prefix

    member this.CeBlock with get() = this.ceBlock

    member this.LastUsedIndex with get() : int<ceIndex> =
        (%this.prefix.CeLength + %this.ceUseCounts.LastUsedCeIndex) |> UMX.tag<ceIndex>

    member this.UseCountArray with get() : int array = this.ceUseCounts.ToArray()

    // Includes the prefix CEs with all the used CEs from the CE block
    member this.CeLength with get() : int<ceLength> =
        this.prefix.CeLength + this.ceUseCounts.UsedCeCount

    // Includes the prefix CEs
    static member getUsedCes (prefix: ceBlock) (ceb: ceBlock) (useCounts: ceUseCounts) : ce[] =
        let blockCes =
            let used = ResizeArray<ce>()
            for i in 0 .. (%ceb.CeLength - 1) do
                if useCounts.[i |> UMX.tag<ceIndex>] <> 0 then
                    used.Add(ceb.getCe i)
            used.ToArray()
        Array.append prefix.CeArray blockCes

    member this.SortableTests with get() = this.sortableTests

    member this.UnsortedCount with get() = this.unsortedCount

    member this.UsedCes with get() : ce array = Array.copy this.usedCes.Value

    // Includes the prefix CEs with all the used CEs from the CE block
    member this.extractCeUseArray : ceUse array =
        let results = ResizeArray<ceUse>()
        let prefixLength = this.Prefix.CeLength
        for i in 0 .. (%prefixLength - 1) do
            let idx = i |> UMX.tag<ceIndex>
            let count = -1 // Prefix CEs are always considered used
            results.Add(ceUse.create idx count (this.Prefix.getCe i))

        for i in 0 .. (%this.CeBlock.CeLength - 1) do
            let idx = i |> UMX.tag<ceIndex>
            let count = this.ceUseCounts.[idx]
            if count > 0 then
                let offsetPos = idx + (prefixLength |> UMX.cast<ceLength,ceIndex>)
                results.Add(ceUse.create offsetPos count (this.CeBlock.getCe i))
        results.ToArray()
