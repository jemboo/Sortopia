namespace Sortopia.Evaluation.Tests

open System
open System.Threading
open System.Threading.Tasks
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Serialization
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Evaluation.Serialization

module StandardSgdTests =
    let private guid name = GuidUtils.guidFromObjs [box name]
    let private prefix = [|ce.create 0 1; ce.create 2 3|]
    let private suffix = [|ce.create 0 2; ce.create 1 3; ce.create 1 2|]
    let private inputs (ces: ce array) =
        let arrays = Array.init 16 (fun mask ->
            let values = Array.init 4 (fun bit -> (mask &&& (1 <<< bit)) <> 0)
            for ce in ces do
                let low, high = int ce.LowChannel, int ce.HighChannel
                if values[low] && not values[high] then values[low] <- false; values[high] <- true
            sortableBoolArray.create(values, 4<channelCount>))
        sortableBinaryTests.create (UMX.tag (guid "sgd-tests")) 4<channelCount> arrays |> sortableTests.Bools
    let private models prefix suffix =
        let models = Array.init 4 (fun index ->
            msce.create (UMX.tag (guid (string index))) 4<channelCount> prefix (suffix |> Array.map Ce.toIndex)
            |> simpleSorterModel.Msce |> sorterModel.Simple)
        sorterModelSet.create (UMX.tag (guid "sgd-models")) models (UMX.tag suffix.Length)
    let private settings = {
        PoolCount = 1; SortersPerPool = 4; RetainedParentsPerPool = 4; ChildrenPerParent = 1
        Generations = 3; MutationMod = 0; AdjustCeLengthFrequency = 2; SortedFraction = 0.99
        PrioritizeNewMutants = false; DistinctSorterHashes = false
        SelectionMeasure = SorterEvalMeasure.stageBiasedFilterUnsorted }
    let private mutator =
        SimpleSorterModelMutator.getMsceModelMutator (RngFactory.create rngType.Lcg) (UMX.tag true)
            (UMX.tag 0.0) (UMX.tag 1.0) (UMX.tag 0.0) (UMX.tag 0.0) mutatorVariant.V1 |> sorterModelMutator.Simple
    let private run settings models tests prefix = StandardSgd.run (ParallelOptions(MaxDegreeOfParallelism = 1)) settings mutator models tests prefix

    [<Fact>]
    let ``standard SGD preserves source models and prefix and advances retained mutation cursors`` () =
        let source = models prefix suffix
        let before = source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize
        let result = run settings source (inputs prefix) prefix
        Assert.Equal(3, result.Generation)
        Assert.Equal(4, result.History.Length)
        Assert.Equal<int>([|0; 1; 2; 3|], result.History |> Array.map _.Generation)
        Assert.Equal<byte>(before, source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize)
        for m in result.Pools[0].Members do
            Assert.Equal(3, m.MutationIndex)
            Assert.True(SorterEval.getIsSorted m.Evaluation)
            Assert.Equal(5, int (SorterEval.getCeLength m.Evaluation))
            Assert.Equal<ce>(prefix, SorterModel.getPrefix m.Model)

    [<Fact>]
    let ``replaying the same inputs produces the same complete population and history`` () =
        let source, tests = models prefix suffix, inputs prefix
        let first = run settings source tests prefix |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let second = run settings source tests prefix |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        Assert.Equal<byte>(first, second)

    [<Fact>]
    let ``observing every generation preserves final bytes and snapshots retain their own state`` () =
        let source, tests = models prefix suffix, inputs prefix
        let expected = run settings source tests prefix |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let snapshots = ResizeArray<sorterPoolSet * byte array>()
        let observe snapshot = snapshots.Add(snapshot, snapshot |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        let result = StandardSgd.runObserved observe (ParallelOptions(MaxDegreeOfParallelism = 2)) settings mutator source tests prefix
        Assert.Equal<byte>(expected, result |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.Equal<int>([|0; 1; 2; 3|], snapshots |> Seq.map (fst >> _.Generation))
        for snapshot, bytes in snapshots do
            Assert.Equal<byte>(bytes, snapshot |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.All((fst snapshots[0]).Pools[0].Members, fun m -> Assert.Equal(0, m.MutationIndex))

    [<Fact>]
    let ``worker budgets and prepared SIMD inputs preserve complete SGD history and models`` () =
        let small = match inputs prefix with sortableTests.Bools value -> value | _ -> failwith "Expected Bools."
        let arrays = Array.init 70 (fun _ -> small.SortableBinaryArrays) |> Array.concat
        let bools = sortableBinaryTests.create small.Id small.ChannelCount arrays |> sortableTests.Bools
        let bits = SortableBitv512Tests.fromBoolArrays small.Id small.ChannelCount arrays |> sortableTests.Bitv512
        let source = models prefix suffix
        let serialize workers tests =
            StandardSgd.run (ParallelOptions(MaxDegreeOfParallelism = workers)) settings mutator source tests prefix
            |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let expected = serialize 1 bools
        for workers in [2; 4; 8; 16] do
            Assert.Equal<byte>(expected, serialize workers bools)
            Assert.Equal<byte>(expected, serialize workers bits)

    [<Fact>]
    let ``SGD final state round trips all models evaluations mutation cursors and summaries`` () =
        let value = run settings (models prefix suffix) (inputs prefix) prefix
        let bytes = value |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let restored = bytes |> MessagePackSetup.deserialize<sorterPoolSetDto> |> SorterPoolSetDto.toDomain
        Assert.Equal<byte>(bytes, restored |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.Throws<ArgumentException>(fun () -> { restored with Generation = -1 } |> SorterPoolSet.validate |> ignore) |> ignore

    [<Fact>]
    let ``strict pruning recovers rather than losing an all-unsorted pool`` () =
        let bad = models [||] [|ce.create 0 1|]
        let result = run settings bad (inputs [||]) [||]
        Assert.NotEmpty(result.Pools[0].Members)
        Assert.All(result.Pools[0].Members, fun m -> Assert.False(SorterEval.getIsSorted m.Evaluation))
        Assert.All(result.History, fun h -> Assert.Equal(0, h.SuccessfulCount))

    [<Fact>]
    let ``duplicate suppression keeps a live representative and multiple pools remain independent`` () =
        let result = run { settings with PoolCount = 2; SortersPerPool = 2; RetainedParentsPerPool = 2; DistinctSorterHashes = true }
                         (models prefix suffix) (inputs prefix) prefix
        Assert.Equal(2, result.Pools.Length)
        Assert.All(result.Pools, fun pool -> Assert.Single(pool.Members) |> ignore)
        Assert.Equal(8, result.History.Length)
        Assert.NotEqual(result.Pools[0].Id, result.Pools[1].Id)

    [<Fact>]
    let ``generation zero evaluates the seed and does not mutate it`` () =
        let source = models prefix suffix
        let result = run { settings with Generations = 0 } source (inputs prefix) prefix
        Assert.Single(result.History) |> ignore
        Assert.All(result.Pools[0].Members, fun m -> Assert.Equal(0, m.MutationIndex); Assert.True(m.ParentId.IsNone))
        Assert.Equal<Guid>(source.SorterModels |> Array.map (SorterModel.getId >> UMX.untag), result.Pools[0].Members |> Array.map (fun m -> UMX.untag (SorterModel.getId m.Model)))

    [<Fact>]
    let ``insufficient seeds incompatible prefixes and invalid settings fail before evolution`` () =
        let source = models prefix suffix
        Assert.Throws<ArgumentException>(fun () -> run { settings with SortersPerPool = 5 } source (inputs prefix) prefix |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run settings source (inputs prefix) [|ce.create 0 3|] |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run { settings with AdjustCeLengthFrequency = 0 } source (inputs prefix) prefix |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run { settings with SortedFraction = Double.NaN } source (inputs prefix) prefix |> ignore) |> ignore

    [<Fact>]
    let ``cancelled evolution never publishes a final population`` () =
        let options = ParallelOptions(CancellationToken = CancellationToken(true))
        Assert.Throws<OperationCanceledException>(fun () -> StandardSgd.run options settings mutator (models prefix suffix) (inputs prefix) prefix |> ignore) |> ignore
