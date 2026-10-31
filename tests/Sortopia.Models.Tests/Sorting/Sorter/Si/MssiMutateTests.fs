namespace Sortopia.Models.Tests.Sorter.Si

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Si

type MssiRandMutateTests() =
    [<Theory>]
    [<InlineData(0.0, 0.0)>]
    [<InlineData(1.0, 0.0)>]
    [<InlineData(0.0, 1.0)>]
    [<InlineData(0.5, 0.5)>]
    member _.``Mutation preserves self inversion and replays deterministically`` ortho para =
        let parent = mssi.create (UMX.tag (Guid.Parse "00000000-0000-0000-0000-000000000001")) 4<channelCount>
                        [|permSi.create [|1; 0; 3; 2|]; permSi.create [|3; 2; 1; 0|]|]
        let mutator = mssiRandMutate.create rngFactory.LcgFactory (opActionRates.create (ortho, para)) mutatorVariant.V1
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal(parent.ChannelCount, result.ChannelCount)
        Assert.Equal(parent.Perm_Sis.Length, result.Perm_Sis.Length)
        Assert.All(result.Perm_Sis, fun perm -> Assert.True(Permutation.isSelfInverse perm.Permutation))
        Assert.Equal(result, mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0))
        Assert.NotEqual(parent.Id, result.Id)
        Assert.NotEqual(result.Id, (mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 1) (UMX.tag 0)).Id)
        if ortho + para = 0.0 then Assert.Equal<permSi[]>(parent.Perm_Sis, result.Perm_Sis)
        else
            // Mutation may select a no-op for a particular seed; verify changes over a fixed sample.
            let changed = [|0 .. 31|] |> Array.exists (fun index ->
                (mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag index) (UMX.tag 0)).Perm_Sis <> parent.Perm_Sis)
            Assert.True(changed)

    [<Theory>]
    [<InlineData(1)>]
    [<InlineData(3)>]
    member _.``Uniform mutator accepts parents with different stage counts`` count =
        let mutator = mssiRandMutate.create rngFactory.LcgFactory (opActionRates.create (0.0, 0.0)) mutatorVariant.V1
        let parent = mssi.create (UMX.tag Guid.Empty) 4<channelCount> (Array.create count (permSi.create [|1; 0; 3; 2|]))
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal<permSi[]>(parent.Perm_Sis, result.Perm_Sis)
