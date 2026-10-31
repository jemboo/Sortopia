namespace Sortopia.Sorting.Tests

open System
open FSharp.UMX
open FsUnit.Xunit
open Xunit
open Sortopia.Core
open Sortopia.Core.Combinatorics
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks

type CeTests() =

    [<Fact>]
    let ``channel endpoints normalize order and expose their span`` () =
        let comparator = ce.create 7 2
        let lower: int<channelIndex> = comparator.LowChannel
        let upper: int<channelIndex> = comparator.HighChannel
        let span: int<channelCount> = comparator.ChannelSpan
        Assert.Equal(2<channelIndex>, lower)
        Assert.Equal(7<channelIndex>, upper)
        Assert.Equal(5<channelCount>, span)
        Assert.Equal(0<channelCount>, (ce.create 3 3).ChannelSpan)
        Assert.Throws<ArgumentException>(fun () -> ChannelIndex.create -1 |> ignore) |> ignore


    [<Fact>]
    let ``generateCeCode with excludeSelfCe false returns indexPicker value`` () =
        let width = 4
        let indices = [|0 .. 20|] // Corresponds to Ces: (0,1), (0,2), (1,2), (0,3), (1,3), (2,3)
        let picker = indexPicker indices
        let result = Ce.generateCeCode false width picker
        result |> should equal 0 // First index from picker
        let maxIndex = Ce.maxIndexForChannelCount width
        result |> should be (lessThanOrEqualTo maxIndex)

    [<Fact>]
    let ``generateCeCode with excludeSelfCe true shifts hi index`` () =
        let width = 4
        let indices = [|0; 1; 2|] // Corresponds to Ces: (0,1), (0,2), (1,2)
        let picker = indexPicker indices
        let result = Ce.generateCeCode true width picker
        let expectedCe = ce.create 0 1 // index 0 maps to (0,0), shifted to (0,1)
        let expectedIndex = Ce.toIndex expectedCe
        result |> should equal expectedIndex
        let ce = Ce.fromIndex result
        (int ce.LowChannel) |> should not' (equal (int ce.HighChannel))

    [<Fact>]
    let ``generateCeCode with excludeSelfCe true produces valid Ce within width`` () =
        let width = 5
        let indices = [|0; 1; 2; 3; 4; 5|]
        let picker = indexPicker indices
        let results = [ for _ in 1..6 -> Ce.generateCeCode true width picker]
        results |> List.iter (fun idx ->
            let ce = Ce.fromIndex idx
            (int ce.LowChannel) |> should be (lessThanOrEqualTo (width - 1))
            (int ce.HighChannel) |> should be (lessThanOrEqualTo (width - 1))
            (int ce.LowChannel) |> should not' (equal (int ce.HighChannel)))

    [<Fact>]
    let ``generateCeCode throws for width less than 1`` () =
        let picker = indexPicker [|0; 1; 2|]
        (fun () -> Ce.generateCeCode false 0 picker |> ignore) 
        |> should throw typeof<Exception>

    [<Fact>]
    let ``generateCeCode with excludeSelfCe true handles edge case width 2`` () =
        let width = 2
        let indices = [|0|] // Maps to (0,1) when excludeSelfCe is true
        let picker = indexPicker indices
        let result = Ce.generateCeCode true width picker
        let cet = Ce.fromIndex result
        cet |> should equal (ce.create 0 1)
        (int cet.LowChannel) |> should not' (equal (int cet.HighChannel))


    [<Fact>]
    let ``toIndex and fromIndex round-trip for valid Ce including Low = Hi``() =
        let ces = [ 
                ce.create 0 0; ce.create 0 1; ce.create 0 2; ce.create 0 3; ce.create 0 4; ce.create 0 5; 
                ce.create 1 1; ce.create 1 2; ce.create 1 3; ce.create 1 4; ce.create 1 5; ce.create 1 6; 
                ce.create 2 2; ce.create 2 3; ce.create 2 4; ce.create 2 5; ce.create 2 6; ce.create 2 7; 
                ce.create 3 3; ce.create 3 4; ce.create 3 5; ce.create 3 6; ce.create 3 7; ce.create 3 8; 
                ce.create 4 4; ce.create 4 5; ce.create 4 6; ce.create 4 7; ce.create 4 8; ce.create 4 9; 
                ce.create 5 5; ce.create 5 6; ce.create 5 7; ce.create 5 8; ce.create 5 9; ce.create 5 10; 
            ]

        for ce in ces do
            let index = Ce.toIndex ce
            let ce' = Ce.fromIndex index
            Assert.Equal(ce, ce')

    [<Theory>]
    [<InlineData(-1, 0, "Indices must be non-negative")>]
    [<InlineData(0, -1, "Indices must be non-negative")>]
    [<InlineData(-1, -1, "Indices must be non-negative")>]
    let ``ce.create rejects negative indices`` (low: int, hi: int, expectedMsg: string) =
        let ex = Assert.Throws<Exception>(fun () -> ce.create low hi |> ignore)
        Assert.Equal(expectedMsg, ex.Message)

    [<Theory>]
    [<InlineData(-1, "Index must be non-negative")>]
    [<InlineData(-2, "Index must be non-negative")>]
    let ``fromIndex handles invalid index`` (index: int, expectedMsg: string) =
        let ex = Assert.Throws<Exception>(fun () -> Ce.fromIndex index |> ignore)
        Assert.Equal(expectedMsg, ex.Message)


















    [<Fact>]
    let ``countReflectiveOrReflected returns 0 for empty array`` () =
        let width = UMX.tag 4
        let count = Ce.countReflectiveOrReflected width [||]
        count |> should equal 0


    [<Fact>]
    let ``countReflectiveOrReflected counts self-reflective CEs`` () =
        let width = UMX.tag 4
        // (0, 3) and (1, 2) are self-reflective on width 4
        let input = [| ce.create 0 3; ce.create 1 2 |]
        let count = Ce.countReflectiveOrReflected width input
        count |> should equal 2


    [<Fact>]
    let ``countReflectiveOrReflected counts paired reflected CEs`` () =
        let width = UMX.tag 4
        // (0, 1) reflects to (2, 3). Both are present in the array.
        let input = [| ce.create 0 1; ce.create 2 3 |]
        let count = Ce.countReflectiveOrReflected width input
        count |> should equal 2


    [<Fact>]
    let ``countReflectiveOrReflected ignores unreflected CEs without matching reflection`` () =
        let width = UMX.tag 4
        // (0, 1) reflects to (2, 3), but (2, 3) is NOT in the array.
        // (1, 2) is self-reflective.
        let input = [| ce.create 0 1; ce.create 1 2 |]
        let count = Ce.countReflectiveOrReflected width input
        count |> should equal 1 // Only (1, 2) counts


