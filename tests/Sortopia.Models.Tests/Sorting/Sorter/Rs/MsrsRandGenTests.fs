namespace Sortopia.Models.Tests.Sorter.Rs

open System
open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Core.Rando
open Sortopia.Sorting
open Sortopia.Models.Sorting.Simple.Rs

type MsrsRandGenTests() =
    // Helper function to create a mock random number generator
    let createMockRando (indices: int list) (floats: float list) =
        fun _ _ -> new MockRando(floats, indices) :> IRando

    // Helper function to create an MsrsRandGen instance
    let createModelRsGen rngType (width:int) stageLength orthoRate paraRate selfSymRate =
        let rates = opsGenRates.create (orthoRate, paraRate, selfSymRate)
        let ratesArray = opsGenRatesArray.create (Array.create stageLength rates)
        msrsRandGen.create rngType (UMX.tag<channelCount> width) rates (UMX.tag<stageLength> stageLength)

    [<Fact>]
    let ``MakeSorterModelFromIndex creates correct number of stages`` () =
        let model = createModelRsGen rngFactory.LcgFactory 4 3 (1.0/3.0) (1.0/3.0) (1.0/3.0)
        let randoGen = createMockRando [0; 1; 0; 1; 0; 1] [0.2; 0.4; 0.6]
        let modelRs = model.MakeSorterModelFromIndex (UMX.tag 0)
        modelRs.Perm_Rss.Length |> should equal 3

    [<Fact>]
    let ``MakeSorterModelFromIndex creates permutations with correct width`` () =
        let model = createModelRsGen rngFactory.LcgFactory 4 2 (1.0/3.0) (1.0/3.0) (1.0/3.0)
        let randoGen = createMockRando [0; 1; 0; 1] [0.2; 0.4]
        let modelRs = model.MakeSorterModelFromIndex (UMX.tag 0)
        modelRs.Perm_Rss |> Array.iter (fun perm -> perm.Order |> should equal (UMX.tag<channelCount> 4))

    [<Fact>]
    let ``MakeSorterModelFromIndex throws on odd width`` () =
        let model = createModelRsGen rngFactory.LcgFactory 3 2 (1.0/3.0) (1.0/3.0) (1.0/3.0)
        let randoGen = createMockRando [0; 1] [0.2; 0.4]
        (fun () -> model.MakeSorterModelFromIndex (UMX.tag 0) |> ignore)
        |> should throw typeof<System.Exception>

    [<Fact>]
    let ``MakeSorterModelFromIndex generates self-inverse permutations`` () =
        let model = createModelRsGen rngFactory.LcgFactory 4 2 0.0 0.0 1.0
        let randoGen = createMockRando [0; 1; 0; 0; 1] [0.2; 0.4]
        let modelRs = model.MakeSorterModelFromIndex (UMX.tag 0)
        modelRs.Perm_Rss
        |> Array.iter (fun perm ->
            PermSi.isReflectionSymmetric perm.PermSi |> should equal true)

    [<Fact>]
    let ``MakeSorterModelFromIndex generates unique IDs`` () =
        let model = createModelRsGen rngFactory.LcgFactory 4 2 (1.0/3.0) (1.0/3.0) (1.0/3.0)
        let randoGen = createMockRando [0; 1; 0; 1] [0.2; 0.4]
        let modelRs1 = model.MakeSorterModelFromIndex (UMX.tag 0)
        let modelRs2 = model.MakeSorterModelFromIndex (UMX.tag 1)
        modelRs1.Id |> should not' (equal modelRs2.Id)
