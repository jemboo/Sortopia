namespace Sortopia.Evaluation.Tests

open System
open System.Globalization
open System.Runtime.Intrinsics
open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Evaluation
open Sortopia.Evaluation.Serialization

module MigrationTests =
    [<Fact>]
    let ``counterexample index respects short blocks before the failing block`` () =
        let zero = Vector512<uint64>.Zero
        let first = sortBlockBitv512.createFromVectors [|zero; zero|] 2
        let next = sortBlockBitv512.createFromVectors [|Vector512.Create(2UL, 0UL, 0UL, 0UL, 0UL, 0UL, 0UL, 0UL); zero|] 3
        let inputs = sortableBitv512Tests.create (UMX.tag (Guid.NewGuid())) 2<channelCount> [|first; next|]
        let suffix = ceBlock.create (UMX.tag (Guid.NewGuid())) 2<channelCount> [||]
        let result, index = CeBlockOpsBitv512.evalWithCounterexampleIndex inputs suffix Threading.CancellationToken.None
        Assert.Equal(Some 3, index)
        Assert.Equal(1<sortableCount>, result.UnsortedCount)
        let _, counterexample = CeBlockOpsBitv512.evalWithCounterexample inputs suffix Threading.CancellationToken.None
        Assert.Equal<int>([|1; 0|], counterexample.Value)

    let private block (width: int) ces = ceBlock.create (UMX.tag (Guid.NewGuid())) (UMX.tag width) ces
    let private formats (values: int[][]) (width: int) =
        let width = UMX.tag<channelCount> width
        let ints = values |> Array.map (fun a -> sortableIntArray.create(a, width, 2<symbolSetSize>))
        let bools = values |> Array.map (fun a -> sortableBoolArray.create(Array.map ((<>) 0) a, width))
        let id () = UMX.tag<sortableTestsId> (Guid.NewGuid())
        [| sortableIntTests.create (id()) width ints |> sortableTests.Ints
           sortableBinaryTests.create (id()) width bools |> sortableTests.Bools
           packedSortableIntTests.create width ints |> sortableTests.PackedInts
           SortableUint8v256Tests.fromIntArrays (id()) width ints |> sortableTests.Uint8v256
           SortableUint8v512Tests.fromIntArrays (id()) width ints |> sortableTests.Uint8v512
           SortableBitv512Tests.fromBoolArrays (id()) width bools |> sortableTests.Bitv512 |]

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``execution limits preserve evaluations and cancel all input formats`` (collect: bool) =
        let rng = Random(57)
        let values = Array.init 1100 (fun _ -> Array.init 6 (fun _ -> rng.Next(2)))
        let network = block 6 [| ce.create 0 1; ce.create 2 5; ce.create 1 3 |]
        for inputs in formats values 6 do
            let evaluate options =
                CeBlockOps.evalWithSorterTestsWithOptions options inputs ceBlock.Empty [|network; network|] (UMX.tag collect)
            let serial = evaluate (Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 1))
            let concurrent = evaluate (Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 2))
            for a, b in Array.zip serial concurrent do
                Assert.Equal<int>(a.UseCountArray, b.UseCountArray)
                Assert.Equal(a.UnsortedCount, b.UnsortedCount)
            let cancelled = Threading.Tasks.ParallelOptions(MaxDegreeOfParallelism = 2, CancellationToken = Threading.CancellationToken(true))
            Assert.Throws<OperationCanceledException>(fun () -> evaluate cancelled |> ignore) |> ignore

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(31)>]
    [<InlineData(32)>]
    [<InlineData(33)>]
    [<InlineData(64)>]
    [<InlineData(65)>]
    [<InlineData(512)>]
    [<InlineData(517)>]
    let ``all formats agree with independent scalar oracle across vector boundaries`` count =
        let rng = Random(814)
        let values = Array.init count (fun _ -> Array.init 6 (fun _ -> rng.Next(2)))
        let ces = [| ce.create 0 1; ce.create 2 5; ce.create 1 3; ce.create 0 4 |]
        let network = block 6 ces
        let expectedUsage = Array.zeroCreate ces.Length
        let failures = ResizeArray<int list>()
        for input in values do
            let output = Array.copy input
            ces |> Array.iteri (fun i c ->
                let lo, hi = int c.LowChannel, int c.HighChannel
                if output[lo] > output[hi] then
                    let tmp = output[lo]
                    output[lo] <- output[hi]
                    output[hi] <- tmp
                    expectedUsage[i] <- expectedUsage[i] + 1)
            if output <> Array.sort output then failures.Add(Array.toList output)
        for inputs in formats values 6 do
            let counted = CeBlockOps.evalWithSorterTest inputs ceBlock.Empty network (UMX.tag false)
            counted.UseCountArray |> should equal expectedUsage
            int counted.UnsortedCount |> should equal failures.Count
            match inputs with
            | sortableTests.Bitv512 bitInputs ->
                let countsOnly = CeBlockOpsBitv512.evalCounts bitInputs network Threading.CancellationToken.None
                countsOnly.UseCountArray |> should equal expectedUsage
                int countsOnly.UnsortedCount |> should equal failures.Count
            | _ -> ()
            let collected = CeBlockOps.evalWithSorterTest inputs ceBlock.Empty network (UMX.tag true)
            collected.UseCountArray |> should equal expectedUsage
            int collected.UnsortedCount |> should equal (failures |> Set.ofSeq |> Set.count)
            let batch = CeBlockOps.evalWithSorterTests inputs ceBlock.Empty [| network; network |] (UMX.tag false)
            for result in batch do
                result.UseCountArray |> should equal expectedUsage
                result.UnsortedCount |> should equal counted.UnsortedCount

    [<Fact>]
    let ``counts only evaluation validates width and honors cancellation`` () =
        let zero = Vector512<uint64>.Zero
        let inputs = sortableBitv512Tests.create (UMX.tag (Guid.NewGuid())) 2<channelCount>
                         [|sortBlockBitv512.createFromVectors [|zero; zero|] 3|]
        let network = block 2 [|ce.create 0 1|]
        Assert.Throws<OperationCanceledException>(fun () ->
            CeBlockOpsBitv512.evalCounts inputs network (Threading.CancellationToken(true)) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () ->
            CeBlockOpsBitv512.evalCounts inputs (block 3 [||]) Threading.CancellationToken.None |> ignore) |> ignore

    [<Fact>]
    let ``padding lanes never contribute usage or failures`` () =
        let network = block 2 [| ce.create 0 1 |]
        let vectors256 = [| Vector256.Create(1uy); Vector256<byte>.Zero |]
        let vectors512 = [| Vector512.Create(1uy); Vector512<byte>.Zero |]
        let bits = [| Vector512.Create(UInt64.MaxValue); Vector512<uint64>.Zero |]
        let a = sortBlockUint8v256.createFromVectors vectors256 3
        let b = sortBlockUint8v512.createFromVectors vectors512 3
        let c = sortBlockBitv512.createFromVectors bits 3
        let empty = block 2 [||]
        let results = [|
            CeBlockOpsUint8v256.evalSimdSortBlocks [a] ceBlock.Empty [|network; empty|]
            CeBlockOpsUint8v512.evalSimdSortBlocks [b] ceBlock.Empty [|network; empty|]
            CeBlockOpsBitv512.evalSimdSortBlocks [c] ceBlock.Empty [|network; empty|]
            CeBlockOpsUint8v256.evalAndCollectUniqueFailures [a] ceBlock.Empty [|network; empty|]
            CeBlockOpsUint8v512.evalAndCollectUniqueFailures [b] ceBlock.Empty [|network; empty|]
            CeBlockOpsBitv512.evalAndCollectUniqueFailures [c] ceBlock.Empty [|network; empty|] |]
        results |> Array.iteri (fun i result ->
            result[0].UseCountArray |> should equal [|3|]
            int result[0].UnsortedCount |> should equal 0
            int result[1].UnsortedCount |> should equal (if i < 3 then 3 else 1))

    [<Fact>]
    let ``blocks and evaluations own their arrays and usage snapshot`` () =
        let ces = [| ce.create 0 1 |]
        let network = block 3 ces
        ces[0] <- ce.create 1 2
        network.getCe 0 |> should equal (ce.create 0 1)
        let counts = ceUseCounts.CreateFromArray [|2|]
        let evaluation = ceBlockEval.create ceBlock.Empty network counts 0<sortableCount> None
        counts.Increment 0<ceIndex>
        evaluation.UseCountArray |> should equal [|2|]
        let used = evaluation.UsedCes
        used[0] <- ce.create 1 2
        evaluation.UsedCes |> should equal [|ce.create 0 1|]
        let v2 = SorterEval.createV2 (UMX.tag (Guid.NewGuid())) evaluation
        let exposed = SorterEval.getCeUseArray v2
        exposed[0] <- ceUse.create 0<ceIndex> 999 (ce.create 1 2)
        (SorterEval.getCeUseArray v2).[0].UseCount |> should equal 2

    [<Theory>]
    [<InlineData(0, 1)>]
    [<InlineData(2, 3)>]
    let ``prefix contributes original CE offsets to all result versions`` suffixCount expected =
        let prefix = block 3 [|ce.create 0 1; ce.create 1 2|]
        let network = block 3 [|ce.create 0 2; ce.create 0 1|]
        let evaluation = ceBlockEval.create prefix network (ceUseCounts.CreateFromArray [|0;suffixCount|]) 0<sortableCount> None
        int evaluation.LastUsedIndex |> should equal expected
        let id = UMX.tag<sorterId> (Guid.NewGuid())
        SorterEval.getLastCeIndex (SorterEval.createV1 id evaluation) |> int |> should equal expected
        SorterEval.getLastCeIndex (SorterEval.createV2 id evaluation) |> int |> should equal expected

    [<Fact>]
    let ``empty evaluations consistently use minus one last index`` () =
        let network = block 3 [||]
        let evaluation = ceBlockEval.create ceBlock.Empty network (ceUseCounts.Create 0<ceLength>) 0<sortableCount> None
        let id = UMX.tag<sorterId> (Guid.NewGuid())
        int evaluation.LastUsedIndex |> should equal -1
        SorterEval.getLastCeIndex (SorterEval.createV2 id evaluation) |> int |> should equal -1

    [<Fact>]
    let ``merging preserves prefix sentinel and sums exact counts`` () =
        let prefix = block 3 [|ce.create 0 1|]
        let network = block 3 [|ce.create 1 2|]
        let id = UMX.tag<sorterId> (Guid.NewGuid())
        let make count = ceBlockEval.create prefix network (ceUseCounts.CreateFromArray [|count|]) 0<sortableCount> None |> SorterEval.createV2 id
        let merged = SorterEval.merge (make 90000) (make 80000)
        SorterEval.getCeUseArray merged |> Array.map (fun c -> c.UseCount) |> should equal [|-1;170000|]
        let other = SorterEval.withSortableTestsSubsetId (UMX.tag "Other") (make 90000)
        SorterEval.getSequenceHash other |> should equal (SorterEval.getSequenceHash (make 90000))

    [<Theory>]
    [<InlineData(-1)>]
    [<InlineData(0)>]
    let ``invalid block lengths fail instead of looping`` (length: int) =
        Assert.Throws<ArgumentException>(fun () -> CeBlock.breakUpIntoBlocks 2<channelCount> [|ce.create 0 1|] (UMX.tag length) |> ignore) |> ignore

    [<Fact>]
    let ``invalid widths endpoints and usage shapes are rejected`` () =
        Assert.Throws<ArgumentException>(fun () -> block 2 [|ce.create 0 2|] |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> ceUseCounts.CreateFromArray [|-1|] |> ignore) |> ignore
        let network = block 3 [|ce.create 0 1|]
        Assert.Throws<ArgumentException>(fun () -> ceBlockEval.create ceBlock.Empty network (ceUseCounts.Create 0<ceLength>) 0<sortableCount> None |> ignore) |> ignore
        for inputs in formats [|[|1;0|]|] 2 do
            Assert.Throws<ArgumentException>(fun () -> CeBlockOps.evalWithSorterTest inputs ceBlock.Empty network (UMX.tag false) |> ignore) |> ignore
        let ints = sortableIntTests.create (UMX.tag (Guid.NewGuid())) 2<channelCount> [||]
        Assert.Throws<ArgumentException>(fun () -> CeBlockOpsInt.eval ints ceBlock.Empty network |> ignore) |> ignore

    let private roundTrip<'T> (dto: 'T) = MessagePackSetup.serialize dto |> MessagePackSetup.deserialize<'T>

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``empty batches and datasets respect their declared widths`` (collect: bool) =
        let valid, wrong = block 2 [||], block 3 [||]
        for inputs in formats [||] 2 do
            let evaluate = CeBlockOps.evalWithSorterTests inputs
            let run prefix networks = evaluate prefix networks (UMX.tag collect)
            Assert.Empty(run ceBlock.Empty [||])
            let result = Assert.Single(run valid [| valid |])
            int result.UnsortedCount |> should equal 0
            Assert.Throws<ArgumentException>(fun () -> run ceBlock.Empty [| valid; wrong |] |> ignore) |> ignore
            Assert.Throws<ArgumentException>(fun () -> run wrong [||] |> ignore) |> ignore

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``typed SIMD evaluators validate width without any vector blocks`` collect =
        let wrong = block 3 [||]
        for inputs in formats [||] 2 do
            let check evaluate =
                Assert.Throws<ArgumentException>(fun () -> evaluate ceBlock.Empty [| wrong |] |> ignore) |> ignore
            match inputs with
            | sortableTests.Bitv512 t ->
                check (if collect then CeBlockOpsBitv512.evalAndCollectNewSortableTests t else CeBlockOpsBitv512.eval t)
            | sortableTests.Uint8v256 t ->
                check (if collect then CeBlockOpsUint8v256.evalAndCollectNewSortableTests t else CeBlockOpsUint8v256.eval t)
            | sortableTests.Uint8v512 t ->
                check (if collect then CeBlockOpsUint8v512.evalAndCollectNewSortableTests t else CeBlockOpsUint8v512.eval t)
            | _ -> ()

    [<Fact>]
    let ``atomic increments preserve the last representable count under contention`` () =
        let counts = ceUseCounts.CreateFromArray [| Int32.MaxValue - 1 |]
        let overflowed =
            [| 1; 2 |] |> Array.Parallel.map (fun _ ->
                try
                    counts.IncrementAtomic 0<ceIndex>
                    false
                with :? OverflowException -> true)
        overflowed |> Array.filter id |> Array.length |> should equal 1
        Assert.Throws<OverflowException>(fun () -> counts.IncrementAtomicBy 0<ceIndex> 2) |> ignore
        counts[0<ceIndex>] |> should equal Int32.MaxValue

    [<Fact>]
    let ``one channel sorter produces all three result versions`` () =
        let network = sorter.create (UMX.tag (Guid.NewGuid())) 1<channelCount> [||]
        let inputs = (formats [| [|1|]; [|0|] |] 1)[0]
        for version in [sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3] do
            let result = SorterSetEval.makeSorterEvals [|network|] ceBlock.Empty inputs version (UMX.tag true) |> Array.exactlyOne
            let actualVersion =
                match result with
                | sorterEval.V1 _ -> sorterEvalType.V1
                | sorterEval.V2 _ -> sorterEvalType.V2
                | sorterEval.V3 _ -> sorterEvalType.V3
            actualVersion |> should equal version
            SorterEval.getUnsortedCount result |> int |> should equal 0
            SorterEval.getStageLength result |> int |> should equal 0
            SorterEval.getCeLength result |> int |> should equal 0
            SorterEval.getLastCeIndex result |> int |> should equal -1
            SorterEval.getIsShortEnough 0<ceLength> result |> should equal true

    [<Fact>]
    let ``length cutoff excludes a required comparator just beyond the cutoff`` () =
        let inputs = (formats [| [|1;0|] |] 2)[0]
        let result = CeBlockOps.evalWithSorterTest inputs ceBlock.Empty (block 2 [|ce.create 0 0; ce.create 0 1|]) (UMX.tag true)
        for version in [sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3] do
            let evaluation = SorterEval.create version (UMX.tag (Guid.NewGuid())) result
            SorterEval.getIsShortEnough 1<ceLength> evaluation |> should equal false
            SorterEval.getIsShortEnough 2<ceLength> evaluation |> should equal true

    [<Fact>]
    let ``sequence fingerprint has a stable versioned golden value`` () =
        let network = block 3 [|ce.create 0 1|]
        let result = ceBlockEval.create ceBlock.Empty network (ceUseCounts.CreateFromArray [|1|]) 0<sortableCount> None
        let evaluation = SorterEval.createV2 (UMX.tag (Guid.NewGuid())) result
        SorterEval.getSequenceHash evaluation |> int |> should equal 992925443

    [<Fact>]
    let ``unknown failure count cannot deserialize as a successful sort`` () =
        let network = block 3 [||]
        let result = ceBlockEval.create ceBlock.Empty network (ceUseCounts.Create 0<ceLength>) 0<sortableCount> None
        let evaluation = SorterEval.createV1 (UMX.tag (Guid.NewGuid())) result
        match SorterEvalDto.fromDomain evaluation with
        | sorterEvalDto.V1 dto ->
            Assert.Throws<ArgumentException>(fun () -> SorterEvalDto.toDomain (sorterEvalDto.V1 {dto with UnsortedCount=None}) |> ignore) |> ignore
        | _ -> failwith "Expected V1"

    [<Fact>]
    let ``result constructors reject duplicate original indices and invalid weights`` () =
        let usage = ceUse.create 0<ceIndex> 1 (ce.create 0 1)
        Assert.Throws<ArgumentException>(fun () ->
            sorterEvalV2.create (UMX.tag (Guid.NewGuid())) SortableTestsSubsetId.Default 3<channelCount> 0<sortableCount>
                0<sequenceHash> 1<stageLength> [|usage;usage|] (UMX.tag false) 0<stageCrossings> 0<reflectiveCount> |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () ->
            ceStUcMeasure.create { StageWeight=UMX.tag Double.NaN; UnsortedWeight=1.0<unsortedWeight>; FilterReflectionSymmetric=UMX.tag false } |> ignore) |> ignore

    [<Theory>]
    [<InlineData(65536, 16777217)>]
    [<InlineData(1000000, 2147483647)>]
    [<InlineData(0, -1)>]
    let ``CE DTO preserves large indices counts and prefix sentinel exactly`` (index: int) count =
        let original = ceUse.create (UMX.tag index) count (ce.create 0 1)
        let restored = original |> CeDataDto.fromDomain |> roundTrip |> CeDataDto.toDomain
        restored |> should equal original

    [<Fact>]
    let ``malformed DTOs cannot bypass domain validation`` () =
        let bad = { CeIndex = -1; UseCount = 1; CeInt = Ce.toIndex (ce.create 0 1) }
        Assert.Throws<ArgumentException>(fun () -> CeDataDto.toDomain bad |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> CeDataDto.toDomain {bad with CeIndex=0; UseCount= -2} |> ignore) |> ignore
        let network = block 3 [|ce.create 0 1|]
        let evaluation = ceBlockEval.create ceBlock.Empty network (ceUseCounts.CreateFromArray [|2|]) 0<sortableCount> None
        let dto = CeBlockEvalDto.fromDomain evaluation
        Assert.Throws<ArgumentException>(fun () -> CeBlockEvalDto.toDomain {dto with CeUseCounts=[||]} |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> CeBlockEvalDto.toDomain {dto with UnsortedCount= -1} |> ignore) |> ignore

    [<Fact>]
    let ``evaluation block result versions and set serialize with explicit options`` () =
        let inputs = (formats [|[|0;1;0|]|] 3)[0]
        let network = block 3 [|ce.create 0 1|]
        let result = CeBlockOps.evalWithSorterTest inputs ceBlock.Empty network (UMX.tag true)
        let restored = result |> CeBlockEvalDto.fromDomain |> roundTrip |> CeBlockEvalDto.toDomain
        restored.UseCountArray |> should equal result.UseCountArray
        restored.UnsortedCount |> should equal result.UnsortedCount
        for version in [sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3] do
            let evaluation = SorterEval.create version (UMX.tag (Guid.NewGuid())) result
            let dto = SorterEvalDto.fromDomain evaluation
            let restored = dto |> roundTrip |> SorterEvalDto.toDomain
            SorterEvalDto.fromDomain restored |> should equal dto
            let set = sorterSetEval.create (UMX.tag (Guid.NewGuid())) (UMX.tag (Guid.NewGuid())) (UMX.tag (Guid.NewGuid())) [|evaluation|]
            let restoredSet = set |> SorterSetEvalDto.fromDomain |> roundTrip |> SorterSetEvalDto.toDomain
            SorterSetEvalDto.fromDomain restoredSet |> should equal (SorterSetEvalDto.fromDomain set)
            Assert.Throws<ArgumentException>(fun () -> sorterSetEval.create set.SorterSetEvalId set.SorterSetId set.SorterTestId [|evaluation;evaluation|] |> ignore) |> ignore

    [<Fact>]
    let ``weighted measure strings and DTOs round trip under non English culture`` () =
        let originalCulture = CultureInfo.CurrentCulture
        try
            CultureInfo.CurrentCulture <- CultureInfo.GetCultureInfo("fr-FR")
            let measures = [| sorterEvalMeasure.CeSt (ceStMeasure.stageBiased()); sorterEvalMeasure.CeStUc (ceStUcMeasure.stageBiased()) |]
            for measure in measures do
                measure |> SorterEvalMeasure.toCompactString |> SorterEvalMeasure.fromCompactString |> should equal measure
                measure |> SorterEvalMeasureDto.fromDomain |> roundTrip |> SorterEvalMeasureDto.toDomain |> should equal measure
        finally
            CultureInfo.CurrentCulture <- originalCulture

