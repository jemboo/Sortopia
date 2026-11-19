namespace Sortopia.Evaluation.Tests

open System
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Xunit
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Evaluation

module ParallelEvaluationTests =
    let private block ces = ceBlock.create (UMX.tag (Guid.NewGuid())) 6<channelCount> ces

    let private formats values (symbols: int) =
        let id () = UMX.tag<sortableTestsId> (Guid.NewGuid())
        let ints = values |> Array.map (fun value -> sortableIntArray.create(value, 6<channelCount>, UMX.tag symbols))
        let common = [| sortableIntTests.create (id()) 6<channelCount> ints |> sortableTests.Ints
                        SortableUint8v256Tests.fromIntArrays (id()) 6<channelCount> ints |> sortableTests.Uint8v256
                        SortableUint8v512Tests.fromIntArrays (id()) 6<channelCount> ints |> sortableTests.Uint8v512 |]
        if symbols <> 2 then common else
            let bools = values |> Array.map (fun value -> sortableBoolArray.create(Array.map ((<>) 0) value, 6<channelCount>))
            Array.append common [| sortableBinaryTests.create (id()) 6<channelCount> bools |> sortableTests.Bools
                                   packedSortableIntTests.create 6<channelCount> ints |> sortableTests.PackedInts
                                   SortableBitv512Tests.fromBoolArrays (id()) 6<channelCount> bools |> sortableTests.Bitv512 |]

    let private failures = function
        | None -> Set.empty
        | Some (sortableTests.Ints tests) -> tests.SortableIntArrays |> Array.map (fun a -> Array.toList a.Values) |> Set.ofArray
        | Some (sortableTests.Bools tests) -> tests.SortableBinaryArrays |> Array.map (fun a -> a.Values |> Array.map (fun b -> if b then 1 else 0) |> Array.toList) |> Set.ofArray
        | Some (sortableTests.PackedInts tests) -> tests.PackedValues |> Array.chunkBySize (int tests.ChannelCount) |> Array.map Array.toList |> Set.ofArray
        | _ -> failwith "Unexpected residual format."

    [<Theory>]
    [<InlineData(1, false, 2)>]
    [<InlineData(1, true, 2)>]
    [<InlineData(2, false, 2)>]
    [<InlineData(2, true, 2)>]
    [<InlineData(32, false, 2)>]
    [<InlineData(32, true, 2)>]
    [<InlineData(2, false, 256)>]
    [<InlineData(2, true, 256)>]
    [<InlineData(32, false, 256)>]
    [<InlineData(32, true, 256)>]
    let ``network ownership and block fallback match scalar oracle including residuals and result order`` networkCount collect symbols =
        let rng = Random(574)
        let values = Array.init 1103 (fun _ -> Array.init 6 (fun _ -> rng.Next(symbols)))
        let before = values |> Array.map Array.copy
        let networks = Array.init networkCount (fun n ->
            Array.init (n % 9) (fun _ -> let low = rng.Next(5) in ce.create low (rng.Next(low + 1, 6))) |> block)
        let expected = networks |> Array.map (fun network ->
            let ces = network.CeArray
            let usage = Array.zeroCreate ces.Length
            let failed = ResizeArray<int list>()
            for input in values do
                let output = Array.copy input
                for i, ce in Array.indexed ces do
                    let low, high = int ce.LowChannel, int ce.HighChannel
                    if output[low] > output[high] then
                        let value = output[low]
                        output[low] <- output[high]
                        output[high] <- value
                        usage[i] <- usage[i] + 1
                if output <> Array.sort output then failed.Add(Array.toList output)
            usage, (if collect then Set.count (Set.ofSeq failed) else failed.Count), Set.ofSeq failed)
        for inputs in formats values symbols do
            for workers in [1; 2; 16] do
                let options = ParallelOptions(MaxDegreeOfParallelism = workers)
                let result = CeBlockOps.evalWithSorterTestsWithOptions options inputs networks (UMX.tag collect)
                for i in 0 .. networks.Length - 1 do
                    let usage, count, residual = expected[i]
                    Assert.Equal<int>(usage, result[i].UseCountArray)
                    Assert.Equal(count, int result[i].UnsortedCount)
                    Assert.Equal(networks[i].CeBlockId, result[i].CeBlock.CeBlockId)
                    let used = Array.zip networks[i].CeArray usage |> Array.choose (fun (ce, count) -> if count > 0 then Some ce else None)
                    Assert.Equal<ce>(used, result[i].UsedCes)
                    Assert.Equal(used.Length, int result[i].CeLength)
                    if collect then Assert.True((residual = failures result[i].SortableTests))
                    else Assert.True(result[i].SortableTests.IsNone)
        Assert.True((before = values))

    [<Fact>]
    let ``empty batches empty tests and cancellation preserve all format contracts`` () =
        for inputs in formats [||] 2 do
            let options = ParallelOptions(MaxDegreeOfParallelism = 16)
            Assert.Empty(CeBlockOps.evalWithSorterTestsWithOptions options inputs [||] (UMX.tag false))
            let result = CeBlockOps.evalWithSorterTestsWithOptions options inputs (Array.init 32 (fun _ -> block [|ce.create 0 1|])) (UMX.tag true)
            Assert.All(result, fun value -> Assert.Equal(0, int value.UnsortedCount); Assert.Equal<int>([|0|], value.UseCountArray))
            let cancelled = ParallelOptions(MaxDegreeOfParallelism = 16, CancellationToken = CancellationToken(true))
            Assert.Throws<OperationCanceledException>(fun () -> CeBlockOps.evalWithSorterTestsWithOptions cancelled inputs [|block [||]|] (UMX.tag false) |> ignore) |> ignore
