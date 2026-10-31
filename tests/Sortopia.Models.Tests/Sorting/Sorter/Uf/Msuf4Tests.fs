namespace Sortopia.Models.Tests.Sorter.Uf

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Uf4

module private Samples =
    let makeTestTwoOrbitUf4 seed kind order =
        let steps = Array.init (MathUtils.exactLog2 (order / 4)) (fun index ->
            let order = 4 * MathUtils.integerPower 2 index
            twoOrbitUfStep.create (Array.create (order / 2) kind) order)
        twoOrbitUf4.create seed steps

type Msuf4Tests() =

    // Helper function to convert string to SeedType
    let toTwoOrbitPairType (twoOrbitPairTypeStr: string) : twoOrbitPairType =
        match twoOrbitPairTypeStr with
        | "Ortho" -> twoOrbitPairType.Ortho
        | "Para" -> twoOrbitPairType.Para
        | "SelfRefl" -> twoOrbitPairType.SelfRefl
        | _ -> failwith $"Invalid TwoOrbitPairType string: {twoOrbitPairTypeStr}"

    // Mock floatPicker for deterministic testing
    let mockFloatPicker () = 0.5

    // Helper function to create a TwoOrbitUf4GenRates for a given order
    let createTestGenRates (order: int) :uf4GenRates =
        Uf4GenRates.makeUniform order

    // Helper function that uses mockFloatPicker to create a random TwoOrbitUnfolder4 for a given order,
    // SeedType, and optional TwoOrbitType override
    let createRandomTestTwoOrbitUf4
            (order: int)
            (topType: twoOrbitPairType)
            (twoOrbitPairTypeOverride: twoOrbitPairType option)
            : twoOrbitUf4  =
        if order = 4 then twoOrbitUf4.create topType [||]
        else
            let baseGenRates : uf4GenRates = createTestGenRates order
            let orthoRate = if topType = twoOrbitPairType.Ortho then 1.0 else 0.0
            let paraRate = if topType = twoOrbitPairType.Para then 1.0 else 0.0
            let selfSyymRate = if topType = twoOrbitPairType.SelfRefl then 1.0 else 0.0

            let ratesArray =
                    match twoOrbitPairTypeOverride with
                    | Some tot ->
                        let orthoRate = if topType = twoOrbitPairType.Ortho then 1.0 else 0.0
                        let paraRate = if topType = twoOrbitPairType.Para then 1.0 else 0.0
                        let selfSyymRate = if topType = twoOrbitPairType.SelfRefl then 1.0 else 0.0
                        Array.init baseGenRates.OpsGenRatesArray.RatesArray.Length (
                            fun _ -> opsGenRates.create(orthoRate, paraRate, selfSyymRate))
                    | None -> baseGenRates.OpsGenRatesArray.RatesArray

            let genRates : uf4GenRates = uf4GenRates.create
                                            order
                                            (opsGenRates.create(orthoRate, paraRate, selfSyymRate))
                                            (opsGenRatesArray.create ratesArray)

            RandomUnfolderOps4.makeRandomTwoOrbitUf4 mockFloatPicker genRates


    [<Fact>]
    let ``create succeeds with valid input`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 4<channelCount>
        let tou = createRandomTestTwoOrbitUf4 4 twoOrbitPairType.Ortho None
        let msuf4 = msuf4.create id width [| tou |]
        Assert.Equal(id, msuf4.Id)
        Assert.Equal(width, msuf4.ChannelCount)
        Assert.Equal(1, %msuf4.StageLength)
        Assert.Equal<twoOrbitUf4 array>([| tou |], msuf4.TwoOrbitUnfolder4s)

    [<Fact>]
    let ``create fails with empty TwoOrbitUnfolder4 array`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 4<channelCount>
        let ex = Assert.Throws<Exception>(fun () -> msuf4.create id width [||] |> ignore)
        Assert.Equal("Must have at least 1 TwoOrbitUnfolder4, got 0", ex.Message)

    [<Fact>]
    let ``create fails with invalid width`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 0<channelCount>
        let tou = createRandomTestTwoOrbitUf4 4 twoOrbitPairType.Ortho None
        let ex = Assert.Throws<Exception>(fun () -> msuf4.create id width [| tou |] |> ignore)
        Assert.Equal("ChannelCount must be at least 1, got 0", ex.Message)

    [<Fact>]
    let ``create fails with mismatched TwoOrbitUnfolder4 order`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 8<channelCount>
        let tou = createRandomTestTwoOrbitUf4 4 twoOrbitPairType.Ortho None
        let ex = Assert.Throws<Exception>(fun () -> msuf4.create id width [| tou |] |> ignore)
        Assert.Equal("All TwoOrbitUnfolder4 must have order 8", ex.Message)

    [<Theory>]
    [<InlineData("Ortho", 0, 1, 2, 3)>]
    [<InlineData("Para", 0, 2, 1, 3)>]
    [<InlineData("SelfRefl", 0, 3, 1, 2)>]
    let ``makeSorter produces correct Sorter for order 4`` (seedTypeStr: string) (low1: int) (hi1: int) (low2: int) (hi2: int) =
        let seedType = toTwoOrbitPairType seedTypeStr
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 4<channelCount>
        let tou = createRandomTestTwoOrbitUf4 4 seedType None
        let msuf4 = msuf4.create id width [| tou |]
        let sorter = msuf4.MakeSorter None
        Assert.Equal(%id |> UMX.tag<sorterId>, sorter.SorterId)
        Assert.Equal(width, sorter.ChannelCount)
        let expectedCes = [| ce.create low1 hi1; ce.create low2 hi2 |]
        Assert.Equal<ce>(expectedCes, sorter.Ces)

    [<Fact>]
    let ``makeSorter handles order 8 correctly with Ortho seed and Para unfolding`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 8<channelCount>
        let seedType = twoOrbitPairType.Ortho
        let tou = Samples.makeTestTwoOrbitUf4 seedType twoOrbitPairType.Para 8
        let msuf4 = msuf4.create id width [| tou |]
        let sorter = msuf4.MakeSorter None
        let expectedCes = [| ce.create 0 6; ce.create 1 7; ce.create 2 4; ce.create 3 5 |]
        Assert.Equal<ce>(expectedCes, sorter.Ces)
        Assert.Equal(%id |> UMX.tag<sorterId>, sorter.SorterId)
        Assert.Equal(width, sorter.ChannelCount)

    [<Fact>]
    let ``makeSorter handles order 8 correctly with Ortho seed and Ortho unfolding`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 8<channelCount>
        let seedType = twoOrbitPairType.Ortho
        let tou = Samples.makeTestTwoOrbitUf4 seedType twoOrbitPairType.Ortho 8
        let msuf4 = msuf4.create id width [| tou |]
        let sorter = msuf4.MakeSorter None
        let expectedCes = [| ce.create 0 1; ce.create 2 3; ce.create 4 5; ce.create 6 7 |]
        Assert.Equal<ce>(expectedCes, sorter.Ces)
        Assert.Equal(%id |> UMX.tag<sorterId>, sorter.SorterId)
        Assert.Equal(width, sorter.ChannelCount)

    [<Fact>]
    let ``makeSorter handles order 8 correctly with Ortho seed and SelfRefl unfolding`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let width = 8<channelCount>
        let seedType = twoOrbitPairType.Ortho
        let tou = Samples.makeTestTwoOrbitUf4 seedType twoOrbitPairType.SelfRefl 8
        let msuf4 = msuf4.create id width [| tou |]
        let sorter = msuf4.MakeSorter None
        let expectedCes = [| ce.create 0 7; ce.create 1 6; ce.create 2 5; ce.create 3 4 |]
        Assert.Equal<ce>(expectedCes, sorter.Ces)
        Assert.Equal(%id |> UMX.tag<sorterId>, sorter.SorterId)
        Assert.Equal(width, sorter.ChannelCount)

    [<Fact>]
    let ``makeSorter handles order 16 correctly with Ortho seed and Para unfolding`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let order = 16
        let width = 16<channelCount>
        let seedType = twoOrbitPairType.Ortho
        let tou = Samples.makeTestTwoOrbitUf4 seedType twoOrbitPairType.Para order
        let msuf4 = msuf4.create id width [| tou |]
        let sorter = msuf4.MakeSorter None
        let expectedCes = [| ce.create 0 9; ce.create 1 8; ce.create 2 11; ce.create 3 10
                             ce.create 4 13; ce.create 5 12; ce.create 6 15; ce.create 7 14 |]
        Assert.Equal<ce>(expectedCes, sorter.Ces)
        Assert.Equal(%id |> UMX.tag<sorterId>, sorter.SorterId)
        Assert.Equal(width, sorter.ChannelCount)

    [<Fact>]
    let ``equality based on id`` () =
        let id = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let order = 16
        let width = 16<channelCount>
        let tou1 = Samples.makeTestTwoOrbitUf4 twoOrbitPairType.Ortho twoOrbitPairType.Para order
        let tou2 = Samples.makeTestTwoOrbitUf4 twoOrbitPairType.Para twoOrbitPairType.Para order
        let msuf4_1 = msuf4.create id width [| tou1 |]
        let msuf4_2 = msuf4.create id width [| tou1; tou2 |]
        Assert.Equal(msuf4_1, msuf4_2)
        Assert.Equal(msuf4_1.GetHashCode(), msuf4_2.GetHashCode())

    [<Fact>]
    let ``inequality with different ids`` () =
        let id1 = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let id2 = Guid.NewGuid() |> UMX.tag<sorterModelId>
        let order = 16
        let width = 16<channelCount>
        let tou = Samples.makeTestTwoOrbitUf4 twoOrbitPairType.Ortho twoOrbitPairType.Para order
        let msuf4_1 = msuf4.create id1 width [| tou |]
        let msuf4_2 = msuf4.create id2 width [| tou |]
        Assert.NotEqual(msuf4_1, msuf4_2)
        Assert.NotEqual(msuf4_1.GetHashCode(), msuf4_2.GetHashCode())