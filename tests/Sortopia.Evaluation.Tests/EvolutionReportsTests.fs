namespace Sortopia.Evaluation.Tests

open System
open System.IO
open System.Text.Json.Nodes
open System.Threading
open FSharp.UMX
open MessagePack
open Xunit
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Evaluation
open Sortopia.Evaluation.Sgd
open Sortopia.Reporting

module EvolutionReportsTests =
    let private evaluation (unsorted: int) (ces: int) (stages: int) =
        sorterEvalV1.create (UMX.tag (Guid.NewGuid())) (UMX.tag "all") 4<channelCount>
            (UMX.tag unsorted) 0<sequenceHash> (UMX.tag (ces - 1)) (UMX.tag stages) (UMX.tag ces)
            (UMX.tag true) 0<stageCrossings> 0<reflectiveCount> |> sorterEval.V1

    let private snapshot groups =
        { Generation = 0; TestsId = UMX.tag (Guid.NewGuid())
          Pools = groups |> Array.map (fun evaluations ->
              { Id = Guid.NewGuid(); RawCeLength = 100
                Members = evaluations |> Array.map (fun evaluation ->
                    { Id = Guid.NewGuid(); MutationIndex = 0; MutationMod = 0; Birthday = 0; ParentId = None
                      Model = msce.create (UMX.tag (Guid.NewGuid())) 4<channelCount> [||] [|Ce.toIndex (ce.create 0 1)|]
                              |> simpleSorterModel.Msce |> sorterModel.Simple
                      Evaluation = evaluation }) }) }

    let private summarize groups =
        let pools = snapshot groups
        { TestsId = UMX.untag pools.TestsId; Rows = EvolutionReports.summarize SorterEvalMeasure.stageBiasedFilterUnsorted pools }

    let private bin ces stages count : sorterLengthHistogramBin = { CeLength = ces; StageLength = stages; Count = count }
    let private coordinates bins = bins |> Option.get |> Array.map (fun b -> b.CeLength, b.StageLength, b.Count)

    [<Fact>]
    let ``histograms count exact length pairs by success separately for each pool and every member`` () =
        let repeated = evaluation 0 8 3
        let summary = summarize [|
            [|evaluation 1 8 3; repeated; evaluation 0 4 2; evaluation 0 8 4; repeated; evaluation 2 8 3; evaluation 1 4 3|]
            [|evaluation 3 5 2|] |] |> SorterPoolSummarySet.validate
        let first, second = summary.Rows[0], summary.Rows[1]
        Assert.Equal<(int * int * int)>([|4, 2, 1; 8, 3, 2; 8, 4, 1|], coordinates first.SuccessfulLengthHistogram)
        Assert.Equal<(int * int * int)>([|4, 3, 1; 8, 3, 2|], coordinates first.UnsuccessfulLengthHistogram)
        Assert.Equal(7, first.MemberCount)
        Assert.Equal(4, first.SuccessfulCount)
        Assert.Empty(second.SuccessfulLengthHistogram.Value)
        Assert.Equal<(int * int * int)>([|5, 2, 1|], coordinates second.UnsuccessfulLengthHistogram)

    [<Theory>]
    [<InlineData(0)>]
    [<InlineData(7)>]
    let ``empty groups are recorded and wide coordinates do not allocate empty bins`` unsorted =
        let summary = summarize [|[|evaluation unsorted 1000000 100000; evaluation unsorted 0 0|]|]
                      |> SorterPoolSummarySet.validate
        let row = summary.Rows[0]
        let occupied, empty =
            if unsorted = 0 then row.SuccessfulLengthHistogram, row.UnsuccessfulLengthHistogram
            else row.UnsuccessfulLengthHistogram, row.SuccessfulLengthHistogram
        Assert.Equal<(int * int * int)>([|0, 0, 1; 1000000, 100000, 1|], coordinates occupied)
        Assert.Empty(empty.Value)
        let csvRow = EvolutionReports.row row
        let emptyName = if unsorted = 0 then "UnsuccessfulLengthHistogram" else "SuccessfulLengthHistogram"
        Assert.Equal(reportValue.Text "[]", csvRow[emptyName])

    [<Fact>]
    let ``all evaluation variants use evaluated lengths including fixed prefix CEs`` () =
        let usage = [|ceUse.create 0<ceIndex> -1 (ce.create 0 1)
                      ceUse.create 2<ceIndex> 1 (ce.create 1 2)
                      ceUse.create 8<ceIndex> 1 (ce.create 2 3)|]
        let v2 = sorterEvalV2.create (UMX.tag (Guid.NewGuid())) (UMX.tag "all") 4<channelCount> 0<sortableCount>
                     0<sequenceHash> 2<stageLength> usage (UMX.tag true) 0<stageCrossings> 0<reflectiveCount>
        let tests = sortableBinaryTests.create (UMX.tag (Guid.NewGuid())) 4<channelCount>
                        [|sortableBoolArray.create([|false; false; false; false|], 4<channelCount>)|] |> sortableTests.Bools
        let v3 = sorterEvalV3.create v2.SorterId v2.SortableTestsSubsetId v2.ChannelCount v2.UnsortedCount
                     v2.SequenceHash v2.StageLength usage tests v2.IsReflectionSymmetric v2.StageCrossingsCount v2.ReflectiveCount
        let summary = summarize [|[|sorterEval.V1 (v2.ToV1()); sorterEval.V2 v2; sorterEval.V3 v3|]|]
        Assert.Equal<(int * int * int)>([|3, 2, 3|], coordinates summary.Rows[0].SuccessfulLengthHistogram)

    [<Fact>]
    let ``summary validation rejects malformed histogram bins and inconsistent totals`` () =
        let valid = summarize [|[|evaluation 0 8 3; evaluation 0 8 3; evaluation 1 4 2|]|]
        let invalidBins = [|
            [|bin -1 3 2|]; [|bin 8 -1 2|]; [|bin 8 3 0|]; [|bin 8 3 -2|]
            [|bin 8 3 1; bin 8 3 1|]; [|bin 8 4 1; bin 8 3 1|]
            [|bin 8 3 1|]; [||]; [|Unchecked.defaultof<sorterLengthHistogramBin>|]; null
            [|bin 8 3 Int32.MaxValue; bin 9 3 Int32.MaxValue|] |]
        for bins in invalidBins do
            let row = { valid.Rows[0] with SuccessfulLengthHistogram = Some bins }
            Assert.Throws<ArgumentException>(fun () -> SorterPoolSummarySet.validate { valid with Rows = [|row|] } |> ignore) |> ignore
        let missing = { valid.Rows[0] with UnsuccessfulLengthHistogram = None }
        Assert.Throws<ArgumentException>(fun () -> SorterPoolSummarySet.validate { valid with Rows = [|missing|] } |> ignore) |> ignore
        let wrongFailures = { valid.Rows[0] with UnsuccessfulLengthHistogram = Some [||] }
        Assert.Throws<ArgumentException>(fun () -> SorterPoolSummarySet.validate { valid with Rows = [|wrongFailures|] } |> ignore) |> ignore

    [<Fact>]
    let ``histograms survive serialization and older summaries explicitly lack them`` () =
        let summary = summarize [|[|evaluation 0 8 3; evaluation 1 4 2|]|]
        let bytes = MessagePackSetup.serialize summary
        let restored = MessagePackSetup.deserialize<sorterPoolSummarySet> bytes |> SorterPoolSummarySet.validate
        Assert.Equal(summary, restored)
        // Remove the new fields from the named-field wire format used by existing summary files.
        let json = JsonNode.Parse(MessagePackSerializer.ConvertToJson(bytes, MessagePackSetup.options))
        for row in json["Rows"].AsArray() do
            Assert.True(row.AsObject().Remove("SuccessfulLengthHistogram"))
            Assert.True(row.AsObject().Remove("UnsuccessfulLengthHistogram"))
        let legacy = MessagePackSerializer.ConvertFromJson(json.ToJsonString(), MessagePackSetup.options)
                     |> MessagePackSetup.deserialize<sorterPoolSummarySet> |> SorterPoolSummarySet.validate
        Assert.Equal(summary.Rows[0].MeanCeLength, legacy.Rows[0].MeanCeLength)
        Assert.True(legacy.Rows[0].SuccessfulLengthHistogram.IsNone)
        Assert.True(legacy.Rows[0].UnsuccessfulLengthHistogram.IsNone)
        let oldReport = EvolutionReports.row legacy.Rows[0]
        Assert.Equal(reportValue.Missing, oldReport["SuccessfulLengthHistogram"])
        Assert.Equal(reportValue.Missing, oldReport["UnsuccessfulLengthHistogram"])

    [<Fact>]
    let ``report histogram cells contain deterministic JSON triples in CE stage count order`` () =
        let summary = summarize [|[|evaluation 0 8 4; evaluation 0 4 2; evaluation 0 8 4; evaluation 1 8 4|]|]
        let row = EvolutionReports.row summary.Rows[0]
        Assert.Equal(reportValue.Text "[[4,2,1],[8,4,2]]", row["SuccessfulLengthHistogram"])
        Assert.Equal(reportValue.Text "[[8,4,1]]", row["UnsuccessfulLengthHistogram"])
        use output = new MemoryStream()
        CsvReport.write output { Columns = EvolutionReports.columns; Rows = [row] } CancellationToken.None
        Assert.True(output.Length > 0L)
