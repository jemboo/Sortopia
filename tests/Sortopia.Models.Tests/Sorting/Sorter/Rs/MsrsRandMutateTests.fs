namespace Sortopia.Models.Tests.Sorter.Rs

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
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
        let parent = msrs.create (UMX.tag (Guid.Parse "00000000-0000-0000-0000-000000000001")) 4<channelCount> [|input; input|]
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
        let parent = msrs.create (UMX.tag Guid.Empty) 4<channelCount> (Array.create count (permRs.create [|1; 0; 3; 2|]))
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal<permRs[]>(parent.Perm_Rss, result.Perm_Rss)
