namespace Sortopia.Models.Tests.Sorter.Rs

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Rs

type MsrsRandMutateTests() =
    [<Theory>]
    [<InlineData(0.0, 0.0, 0.0)>]
    [<InlineData(1.0, 0.0, 0.0)>]
    [<InlineData(0.0, 1.0, 0.0)>]
    [<InlineData(0.0, 0.0, 1.0)>]
    [<InlineData(0.25, 0.25, 0.25)>]
    member _.``Uniform mutation modes preserve reflection symmetry and replay deterministically`` ortho para selfSym =
        let input = permRs.create [|1; 0; 3; 2|]
        let parent = msrs.create (UMX.tag (Guid.Parse "00000000-0000-0000-0000-000000000001")) 4<channelCount> [||] [|input; input|]
        let mutator = msrsRandMutate.create rngFactory.LcgFactory (opsActionRates.create ortho para selfSym) mutatorVariant.V1
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal(parent.ChannelCount, result.ChannelCount)
        Assert.Equal(parent.StageLength, result.StageLength)
        Assert.Equal(result, mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0))
        Assert.NotEqual(parent.Id, result.Id)
        Assert.NotEqual(result.Id, (mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 1) (UMX.tag 0)).Id)
        Assert.All(result.Perm_Rss, fun perm ->
            Assert.True(PermSi.isReflectionSymmetric perm.PermSi)
            Assert.True(perm.Array |> Array.indexed |> Array.forall (fun (i, x) -> perm.Array.[x] = i))
            if ortho + para + selfSym = 0.0 then Assert.Equal(input, perm)
            if para = 1.0 then Assert.Equal<int[]>([|2; 3; 0; 1|], perm.Array)
            if selfSym = 1.0 then Assert.Equal<int[]>([|3; 2; 1; 0|], perm.Array))

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(3)>]
    member _.``Uniform mutator accepts parents with different stage counts`` count =
        let mutator = msrsRandMutate.create rngFactory.LcgFactory (opsActionRates.create 0.0 0.0 0.0) mutatorVariant.V1
        let parent = msrs.create (UMX.tag Guid.Empty) 4<channelCount> [||] (Array.create count (permRs.create [|1; 0; 3; 2|]))
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal<permRs>(parent.Perm_Rss :> seq<permRs>, result.Perm_Rss :> seq<permRs>)

    [<Fact>]
    member _.``Unchanged pair mutations share the original after consuming their random choices`` () =
        for mode, values in [opsActionMode.Ortho, [|1;0;3;2|]; opsActionMode.Para, [|2;3;0;1|]; opsActionMode.SelfRefl, [|3;2;1;0|]] do
            let original = permRs.create values
            let expectedChoices = ResizeArray<int>()
            PermRsOps.findRsPoints (fun n -> expectedChoices.Add n; (expectedChoices.Count - 1) % n) original |> ignore
            let actualChoices = ResizeArray<int>()
            let result = PermRsOps.mutatePerm_Rs (fun n -> actualChoices.Add n; (actualChoices.Count - 1) % n) mode original
            Assert.Same(original, result)
            Assert.NotEmpty actualChoices
            Assert.Equal<int>(expectedChoices, actualChoices)
            Assert.Equal<int>(values, original.Array)

    [<Theory>]
    [<InlineData(24)>]
    [<InlineData(32)>]
    [<InlineData(64)>]
    member _.``MSRS cutoff materialization preserves every comparator and prefix`` width =
        let stages = Array.init 12 (fun index ->
            Array.init width (fun channel -> if index % 2 = 0 then channel ^^^ 1 else width - channel - 1) |> permRs.create)
        let prefix = [|ce.create 0 1; ce.create 2 3|]
        let model = msrs.create (UMX.tag (Guid.Parse "00000000-0000-0000-0000-000000000031")) (UMX.tag width) prefix stages
        let complete = model.MakeSorter None
        let suffix = complete.Ces |> Array.skip prefix.Length
        for count in [0; 1; width / 2 - 1; width / 2; width / 2 + 1; suffix.Length; suffix.Length + 1] do
            let actual = model.MakeSorter (Some (UMX.tag count))
            let expected = Array.append prefix (Array.truncate count suffix)
            Assert.Equal<ce>(expected, actual.Ces)
        Assert.Throws<ArgumentException>(fun () -> model.MakeSorter (Some -1<ceLength>) |> ignore) |> ignore
