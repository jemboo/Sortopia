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
        Generations = 3; MutationMod = 0; InitialPoolSeed = 0; AdjustCeLengthFrequency = 2; SortedFraction = 0.99
        InitialPoolSelection = initialPoolSelection.Random
        PrioritizeNewMutants = false; DistinctSorterHashes = false
        SelectionMeasure = SorterEvalMeasure.stageBiasedFilterUnsorted }
    let private mutator =
        SimpleSorterModelMutator.getMsceModelMutator (RngFactory.create rngType.Lcg) (UMX.tag true)
            (UMX.tag 0.0) (UMX.tag 1.0) (UMX.tag 0.0) (UMX.tag 0.0) mutatorVariant.V1 |> sorterModelMutator.Simple
    let private run settings models tests = StandardSgd.run (ParallelOptions(MaxDegreeOfParallelism = 1)) settings mutator models tests

    let private samplingSource () =
        Array.init 40 (fun index ->
            msce.create (UMX.tag (guid ("sample-" + string index))) 4<channelCount> prefix (suffix |> Array.map Ce.toIndex)
            |> simpleSorterModel.Msce |> sorterModel.Simple)
        |> fun models -> sorterModelSet.create (UMX.tag (guid "sampling-source")) models (UMX.tag suffix.Length)

    let private sampledIds (value: sorterPoolSet) =
        value.Pools |> Array.map (fun pool -> pool.Members |> Array.map (fun memberValue -> UMX.untag (SorterModel.getId memberValue.Model)))

    [<Fact>]
    let ``initial pool sampling is stable without replacement and does not change the source`` () =
        let source = samplingSource ()
        let before = source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize
        let sampled = run { settings with Generations = 0; PoolCount = 2 } source (inputs prefix)
        let ids = sampledIds sampled |> Array.concat
        let sourceIds = source.SorterModels |> Array.map (SorterModel.getId >> UMX.untag)
        // Fixed SplitMix64 / partial Fisher-Yates vector protects saved replay identities.
        Assert.Equal<Guid>([|33; 36; 4; 20; 22; 2; 14; 29|] |> Array.map (fun index -> sourceIds[index]), ids)
        Assert.Equal(8, ids |> Array.distinct |> Array.length)
        Assert.Equal<byte>(before, source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize)
        let smaller = run { settings with Generations = 0 } source (inputs prefix)
        Assert.Equal<Guid>((sampledIds smaller)[0], (sampledIds sampled)[0])

    [<Fact>]
    let ``initial pool seed is independent of mutation settings source insertion and workers`` () =
        let source = samplingSource ()
        let reversed = sorterModelSet.create source.Id (source.SorterModels |> Array.rev) source.RawCeLength
        let initial = { settings with Generations = 0; InitialPoolSeed = 7 }
        let expected = run initial source (inputs prefix)
        let otherMutator = SimpleSorterModelMutator.getMsceModelMutator (RngFactory.create rngType.Smx) (UMX.tag true)
                               (UMX.tag 0.1) (UMX.tag 0.8) (UMX.tag 0.1) (UMX.tag 0.1) mutatorVariant.V1 |> sorterModelMutator.Simple
        let actual = StandardSgd.run (ParallelOptions(MaxDegreeOfParallelism = 4)) { initial with MutationMod = 15 }
                         otherMutator reversed (inputs prefix)
        Assert.Equal<Guid>((sampledIds expected)[0], (sampledIds actual)[0])
        Assert.Equal(expected.Pools[0].Id, actual.Pools[0].Id)
        let samples = [|0..8|] |> Array.map (fun seed -> run { initial with InitialPoolSeed = seed } source (inputs prefix))
        Assert.Equal(9, samples |> Array.map (fun value -> (sampledIds value)[0] |> Array.sort) |> Array.distinct |> Array.length)
        Assert.Equal(9, samples |> Array.map (fun value -> value.Pools[0].Id) |> Array.distinct |> Array.length)

    [<Fact>]
    let ``best selection ranks the full source and is independent of mutation and sampling seeds`` () =
        let network = Array.append prefix suffix
        let arrays = [| [|ce.create 0 1|]; Array.append [|ce.create 0 3; ce.create 1 2|] network
                        network; network; [|ce.create 0 2; ce.create 1 3; ce.create 0 1; ce.create 2 3; ce.create 1 2|] |]
        let models = arrays |> Array.mapi (fun i ces ->
            msce.create (UMX.tag (Guid(sprintf "00000000-0000-0000-0000-%012d" (i+1)))) 4<channelCount> [||] (ces |> Array.map Ce.toIndex)
            |> simpleSorterModel.Msce |> sorterModel.Simple)
        let source = sorterModelSet.create (UMX.tag (guid "ranked-source")) models 7<ceLength>
        let baseline = run { settings with Generations = 0; SortersPerPool = 5; RetainedParentsPerPool = 5 } source (inputs [||])
        let measure = SorterEvalFunctions.getFunctionForMeasure settings.SelectionMeasure
        let expected = baseline.Pools[0].Members |> Array.filter (fun m -> SorterEval.getIsSorted m.Evaluation)
                       |> Array.sortBy (fun m -> float (measure m.Evaluation), SorterModel.getId m.Model)
                       |> Array.distinctBy (fun m -> SorterEval.getSequenceHash m.Evaluation) |> Array.take 2
                       |> Array.map (fun m -> UMX.untag (SorterModel.getId m.Model))
        let best = { settings with Generations = 0; SortersPerPool = 2; RetainedParentsPerPool = 2;
                                  InitialPoolSelection = initialPoolSelection.Best; DistinctSorterHashes = true }
        let first = run best source (inputs [||])
        let second = run { best with InitialPoolSeed = 999; MutationMod = 15 } source (inputs [||])
        Assert.Equal<Guid>(expected, (sampledIds first)[0])
        Assert.Equal<Guid>(expected, (sampledIds second)[0])
        Assert.Equal(first.Pools[0].Id, second.Pools[0].Id)
        Assert.DoesNotContain(first.Pools[0].Members, fun m -> not (SorterEval.getIsSorted m.Evaluation))
        let options = ParallelOptions(MaxDegreeOfParallelism = 4)
        let prepared = StandardSgd.prepareInitialPopulation options best source (inputs [||])
        let replay = StandardSgd.runObservedPrepared ignore options { best with Generations = 2 } mutator prepared (inputs [||])
        let direct = run { best with Generations = 2 } source (inputs [||])
        let bytes value = value |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        Assert.Equal<byte>(bytes direct, bytes replay)
        Assert.Throws<ArgumentException>(fun () -> StandardSgd.runObservedPrepared ignore options { best with SortersPerPool = 3 } mutator prepared (inputs [||]) |> ignore) |> ignore

    [<Fact>]
    let ``best selection refuses too few eligible distinct networks`` () =
        Assert.Throws<ArgumentException>(fun () ->
            run { settings with InitialPoolSelection = initialPoolSelection.Best; DistinctSorterHashes = true }
                (samplingSource ()) (inputs prefix) |> ignore) |> ignore

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(2)>]
    [<InlineData(3)>]
    let ``SGD retains boundary networks when a shorter child ends empty-candidate recovery`` versionNumber =
        let version = [|sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3|][versionNumber - 1]
        let network = Array.append prefix suffix
        let alternative = [|ce.create 0 2; ce.create 1 3; ce.create 0 1; ce.create 2 3; ce.create 1 2|]
        let memberFor name ces : sorterPoolMember =
            let id = guid name
            let model = msce.create (UMX.tag id) 4<channelCount> [||] (ces |> Array.map Ce.toIndex)
                        |> simpleSorterModel.Msce |> sorterModel.Simple
            let evaluation =
                SorterSetEval.makeSorterEvals [|sorter.create (UMX.tag id) 4<channelCount> ces|]
                    (inputs [||]) version (UMX.tag false) |> Array.exactlyOne
            { Id = id; Model = model; Evaluation = evaluation
              MutationIndex = 0; MutationMod = 0; Birthday = 0; ParentId = None }
        let boundaryA = memberFor "boundary-a" (Array.append [|ce.create 0 0|] network)
        let boundaryB = memberFor "boundary-b" (Array.append [|ce.create 1 1|] alternative)
        let shorter = memberFor "shorter" network
        let tooLong = memberFor "too-long" (Array.append [|ce.create 0 0; ce.create 1 1|] network)
        let unsorted = memberFor "unsorted" [|ce.create 0 1|]
        let pool: sorterPool = { Id = guid "boundary-pool"; RawCeLength = 20; Members = [|boundaryA; boundaryB|] }
        let limits = { settings with SortersPerPool = 2; RetainedParentsPerPool = 2; DistinctSorterHashes = true }
        let adjusted = StandardSgd.adjustCeLength 1.0 pool
        Assert.Equal(5, adjusted.RawCeLength)
        Assert.NotEqual(SorterEval.getSequenceHash boundaryA.Evaluation, SorterEval.getSequenceHash boundaryB.Evaluation)
        Assert.All(adjusted.Members, fun m -> Assert.Equal(5, int (SorterEval.getLastCeIndex m.Evaluation)))
        Assert.Equal(4, int (SorterEval.getLastCeIndex shorter.Evaluation))
        Assert.Equal(6, int (SorterEval.getLastCeIndex tooLong.Evaluation))
        Assert.False(SorterEval.getIsSorted unsorted.Evaluation)
        // With no strictly shorter candidate, the old empty-candidate fallback
        // concealed the boundary error. A shorter child must not discard both parents.
        Assert.Equal(2, (StandardSgd.prune limits adjusted).Members.Length)
        let combined = { adjusted with Members = [|boundaryA; boundaryB; shorter; tooLong; unsorted|] }
        let selected = StandardSgd.prune limits combined
        Assert.Equal(2, selected.Members.Length)
        Assert.Contains(selected.Members, fun m -> int (SorterEval.getLastCeIndex m.Evaluation) = 5)
        Assert.All(selected.Members, fun m -> Assert.True(SorterEval.getIsSorted m.Evaluation))
        Assert.DoesNotContain(selected.Members, fun m -> m.Id = tooLong.Id || m.Id = unsorted.Id)
        Assert.Equal(2, selected.Members |> Array.distinctBy (fun m -> SorterEval.getSequenceHash m.Evaluation) |> Array.length)

    [<Fact>]
    let ``standard SGD preserves source models and prefix and advances retained mutation cursors`` () =
        let source = models prefix suffix
        let before = source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize
        let result = run settings source (inputs prefix)
        Assert.Equal(3, result.Generation)
        Assert.Equal<byte>(before, source |> SorterModelSetDto.fromDomain |> MessagePackSetup.serialize)
        for m in result.Pools[0].Members do
            Assert.Equal(3, m.MutationIndex)
            Assert.True(SorterEval.getIsSorted m.Evaluation)
            // Fixed prefix CEs count but are not evaluated against the filtered inputs.
            Assert.Equal(5, int (SorterEval.getCeLength m.Evaluation))
            Assert.Equal(3, int (SorterEval.getStageLength m.Evaluation))
            Assert.All(SorterEval.getCeUseArray m.Evaluation |> Array.take prefix.Length, fun usage -> Assert.Equal(-1, usage.UseCount))
            Assert.Equal<ce>(prefix, SorterModel.getPrefix m.Model)

    [<Fact>]
    let ``replaying the same inputs produces the same complete population`` () =
        let source, tests = models prefix suffix, inputs prefix
        let first = run settings source tests |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let second = run settings source tests |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        Assert.Equal<byte>(first, second)

    [<Fact>]
    let ``observing every generation preserves final bytes and snapshots retain their own state`` () =
        let source, tests = models prefix suffix, inputs prefix
        let expected = run settings source tests |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let snapshots = ResizeArray<sorterPoolSet * byte array>()
        let observe snapshot = snapshots.Add(snapshot, snapshot |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        let result = StandardSgd.runObserved observe (ParallelOptions(MaxDegreeOfParallelism = 2)) settings mutator source tests
        Assert.Equal<byte>(expected, result |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.Equal<int>([|0; 1; 2; 3|], snapshots |> Seq.map (fst >> _.Generation))
        Assert.Equal<byte>(expected, snd snapshots[snapshots.Count - 1])
        for snapshot, bytes in snapshots do
            Assert.Equal<byte>(bytes, snapshot |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.All((fst snapshots[0]).Pools[0].Members, fun m -> Assert.Equal(0, m.MutationIndex))

    [<Fact>]
    let ``worker budgets and prepared SIMD inputs preserve complete SGD populations`` () =
        let small = match inputs prefix with sortableTests.Bools value -> value | _ -> failwith "Expected Bools."
        let arrays = Array.init 70 (fun _ -> small.SortableBinaryArrays) |> Array.concat
        let bools = sortableBinaryTests.create small.Id small.ChannelCount arrays |> sortableTests.Bools
        let bits = SortableBitv512Tests.fromBoolArrays small.Id small.ChannelCount arrays |> sortableTests.Bitv512
        let source = models prefix suffix
        let serialize workers tests =
            StandardSgd.run (ParallelOptions(MaxDegreeOfParallelism = workers)) settings mutator source tests
            |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize
        let expected = serialize 1 bools
        for workers in [2; 4; 8; 16] do
            Assert.Equal<byte>(expected, serialize workers bools)
            Assert.Equal<byte>(expected, serialize workers bits)

    [<Fact>]
    let ``SGD final state round trips all models evaluations and mutation cursors`` () =
        let value = run settings (models prefix suffix) (inputs prefix)
        let dto = SorterPoolSetDto.fromDomain value
        Assert.Equal(2, dto.Version)
        let bytes = MessagePackSetup.serialize dto
        let restored = bytes |> MessagePackSetup.deserialize<sorterPoolSetDto> |> SorterPoolSetDto.toDomain
        Assert.Equal<byte>(bytes, restored |> SorterPoolSetDto.fromDomain |> MessagePackSetup.serialize)
        Assert.Throws<ArgumentException>(fun () -> { dto with Version = 1 } |> SorterPoolSetDto.toDomain |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> { restored with Generation = -1 } |> SorterPoolSet.validate |> ignore) |> ignore

    [<Fact>]
    let ``strict pruning recovers rather than losing an all-unsorted pool`` () =
        let bad = models [||] [|ce.create 0 1|]
        let result = run settings bad (inputs [||])
        Assert.NotEmpty(result.Pools[0].Members)
        Assert.All(result.Pools[0].Members, fun m -> Assert.False(SorterEval.getIsSorted m.Evaluation))

    [<Fact>]
    let ``duplicate suppression keeps a live representative and multiple pools remain independent`` () =
        let result = run { settings with PoolCount = 2; SortersPerPool = 2; RetainedParentsPerPool = 2; DistinctSorterHashes = true }
                         (models prefix suffix) (inputs prefix)
        Assert.Equal(2, result.Pools.Length)
        Assert.All(result.Pools, fun pool -> Assert.Single(pool.Members) |> ignore)
        Assert.NotEqual(result.Pools[0].Id, result.Pools[1].Id)

    [<Fact>]
    let ``generation zero evaluates the seed and does not mutate it`` () =
        let source = models prefix suffix
        let result = run { settings with Generations = 0 } source (inputs prefix)
        Assert.Equal(0, result.Generation)
        Assert.All(result.Pools[0].Members, fun m -> Assert.Equal(0, m.MutationIndex); Assert.True(m.ParentId.IsNone))
        Assert.Equal<Guid>(source.SorterModels |> Array.map (SorterModel.getId >> UMX.untag) |> Array.sort, result.Pools[0].Members |> Array.map (fun m -> UMX.untag (SorterModel.getId m.Model)) |> Array.sort)

    [<Fact>]
    let ``insufficient seeds and invalid settings fail before evolution`` () =
        let source = models prefix suffix
        Assert.Throws<ArgumentException>(fun () -> run { settings with SortersPerPool = 5 } source (inputs prefix) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run { settings with AdjustCeLengthFrequency = 0 } source (inputs prefix) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run { settings with SortedFraction = Double.NaN } source (inputs prefix) |> ignore) |> ignore
        Assert.Throws<ArgumentException>(fun () -> run { settings with InitialPoolSeed = -1 } source (inputs prefix) |> ignore) |> ignore

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``SGD skips each model prefix without coupling filtered tests to model metadata`` best =
        let filter = [|ce.create 1 2; ce.create 0 1|]
        let tests = inputs filter
        let modelPrefixes = [|filter; [|ce.create 0 1|]; [||]|]
        let source =
            modelPrefixes |> Array.mapi (fun index prefix ->
                msce.create (UMX.tag (guid ("independent-prefix-" + string index))) 4<channelCount> prefix [|Ce.toIndex (ce.create 0 0)|]
                |> simpleSorterModel.Msce |> sorterModel.Simple)
            |> fun models -> sorterModelSet.create (UMX.tag (guid "independent-prefixes")) models 1<ceLength>
        let configuration = { settings with Generations = 1; SortersPerPool = 3; RetainedParentsPerPool = 3
                                            InitialPoolSelection = if best then initialPoolSelection.Best else initialPoolSelection.Random
                                            SelectionMeasure = SorterEvalMeasure.stageBiased_UnsortedCount }
        let snapshots = ResizeArray<sorterPoolSet>()
        StandardSgd.runObserved snapshots.Add (ParallelOptions(MaxDegreeOfParallelism = 2)) configuration mutator source tests |> ignore
        Assert.Equal(2, snapshots.Count)
        Assert.Equal(3, snapshots[0].Pools[0].Members.Length)
        for snapshot in snapshots do
            for memberValue in snapshot.Pools[0].Members do
                let sorter = SorterModel.makeSorter memberValue.Model None
                let prefixLength = (SorterModel.getPrefix memberValue.Model).Length
                let suffix = Sortopia.Sorting.Networks.sorter.create sorter.SorterId sorter.ChannelCount (sorter.Ces |> Array.skip prefixLength)
                let expected = SorterSetEval.makeSorterEvals [|suffix|] tests sorterEvalType.V2 (UMX.tag false) |> Array.exactlyOne
                Assert.Equal(SorterEval.getUnsortedCount expected, SorterEval.getUnsortedCount memberValue.Evaluation)
                let retained = SorterEval.withModelPrefix memberValue.Model expected
                Assert.Equal<ceUse>(SorterEval.getCeUseArray retained, SorterEval.getCeUseArray memberValue.Evaluation)
                Assert.All(SorterEval.getCeUseArray memberValue.Evaluation |> Array.take prefixLength,
                    fun usage -> Assert.Equal(-1, usage.UseCount))
                Assert.All(SorterEval.getCeUseArray memberValue.Evaluation |> Array.skip prefixLength,
                    fun useCount -> Assert.True(useCount.UseCount > 0))

    [<Theory>]
    [<InlineData(false)>]
    [<InlineData(true)>]
    let ``model evaluation marks the skipped prefix and preserves suffix usage indices and pruning`` simd =
        let binary = match inputs prefix with sortableTests.Bools value -> value | _ -> failwith "Expected Bools."
        let tests =
            if simd then SortableBitv512Tests.fromBoolArrays binary.Id binary.ChannelCount binary.SortableBinaryArrays |> sortableTests.Bitv512
            else sortableTests.Bools binary
        let source = models prefix (Array.append suffix [|ce.create 0 1|])
        let model = source.SorterModels[0]
        let options = ParallelOptions(MaxDegreeOfParallelism = 2)
        for version in [sorterEvalType.V1; sorterEvalType.V2; sorterEvalType.V3] do
            let evaluation = SorterSetEval.makeSorterModelEvalsWithOptions options [|model|] (Some 4<ceLength>) tests version (UMX.tag true) |> Array.exactlyOne
            Assert.Equal(0, int (SorterEval.getUnsortedCount evaluation))
            Assert.Equal(5, int (SorterEval.getCeLength evaluation))
            Assert.Equal(3, int (SorterEval.getStageLength evaluation))
            Assert.Equal(4, int (SorterEval.getLastCeIndex evaluation))
            if version <> sorterEvalType.V1 then
                let usage = SorterEval.getCeUseArray evaluation
                Assert.Equal<int>([|0;1;2;3;4|], usage |> Array.map (fun value -> int value.CeIndex))
                Assert.All(usage |> Array.take 2, fun value -> Assert.Equal(-1, value.UseCount))
                Assert.All(usage |> Array.skip 2, fun value -> Assert.True(value.UseCount > 0))
                Assert.Throws<ArgumentException>(fun () -> SorterEval.withModelPrefix model evaluation |> ignore) |> ignore
        // A suffix limit never truncates the fixed prefix, even with no active suffix.
        let prefixOnly = SorterSetEval.makeSorterModelEvalsWithOptions options [|model|] (Some 0<ceLength>) tests sorterEvalType.V2 (UMX.tag false) |> Array.exactlyOne
        Assert.Equal(2, int (SorterEval.getCeLength prefixOnly))
        Assert.Equal(1, int (SorterEval.getStageLength prefixOnly))

    [<Fact>]
    let ``cancelled evolution never publishes a final population`` () =
        let options = ParallelOptions(CancellationToken = CancellationToken(true))
        Assert.Throws<OperationCanceledException>(fun () -> StandardSgd.run options settings mutator (models prefix suffix) (inputs prefix) |> ignore) |> ignore
