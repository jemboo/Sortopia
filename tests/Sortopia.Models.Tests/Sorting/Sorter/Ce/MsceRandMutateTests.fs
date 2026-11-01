namespace Sortopia.Models.Tests.Sorter.Ce

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Ce

type MsceRandMutateTests() =
    [<Theory>]
    [<InlineData(1.0, 0.0, 0.0)>]
    [<InlineData(0.0, 1.0, 0.0)>]
    [<InlineData(0.0, 0.0, 1.0)>]
    [<InlineData(0.0, 0.0, 0.0)>]
    member _.``Uniform mutation modes preserve length and produce valid reproducible comparators`` mutation insertion deletion =
        let parent = msce.create (UMX.tag (Guid.Parse "00000000-0000-0000-0000-000000000001")) 10<channelCount> [|42; 42; 42; 42|]
        let mutator = msceRandMutate.create rngFactory.LcgFactory (indelRates.create (mutation, insertion, deletion)) (UMX.tag false) mutatorVariant.V1
        let result = mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0)
        Assert.Equal(parent.ChannelCount, result.ChannelCount)
        Assert.Equal(parent.CeLength, result.CeLength)
        Assert.Equal(result, mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 0) (UMX.tag 0))
        Assert.NotEqual(parent.Id, result.Id)
        Assert.NotEqual(result.Id, (mutator.MakeSorterModelFromIndexAndMod parent (UMX.tag 1) (UMX.tag 0)).Id)
        Assert.All(result.CeCodes, fun code ->
            let ce = Ce.fromIndex code
            Assert.InRange((int ce.LowChannel), 0, 9)
            Assert.InRange((int ce.HighChannel), (int ce.LowChannel), 9))
        if mutation + insertion + deletion = 0.0 then Assert.Equal<int>(parent.CeCodes :> seq<int>, result.CeCodes :> seq<int>)
        else Assert.NotEqual<int[]>(parent.CeCodes, result.CeCodes)
        if insertion = 1.0 then
            Assert.Equal(42, result.CeCodes.[1])
            Assert.Equal(42, result.CeCodes.[3])
