using System.Globalization;
using System.Security.Cryptography;
using Microsoft.FSharp.Core;
using Sortopia.Evaluation;
using Sortopia.Functions;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Sgd_sweep_reports_combine_all_saved_networks_with_exact_V1_statistics_and_parameter_values()
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "GenerationCount", values = ["0"] },
            new() { keyName = "OrthoRate", values = ["1", "2"] },
            new() { keyName = "SelfSymRate", values = ["0.025", "0.030"] }]);
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parametersFolder = Path.Combine(folder, "runParameters");
        Completed(RunParameterExecution.executeFolder(parametersFolder, null!, null!, Token));
        var files = Directory.GetFiles(parametersFolder, "*.msgpack");
        Assert.Equal(4, files.Length);
        Assert.All(files, file => Assert.True(SorterEvalReportExport.isSupported(RunParameterFiles.loadFile(file))));
        var original = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var destination = Path.Combine(temporary.FullName, "Sweep report");
        var export = SorterEvalReportExport.exportFolder(parametersFolder, FSharpOption<Guid>.Some(consumer.Runs[0].Id), destination, Token);
        Assert.Equal(4, export.SummaryRows); Assert.Equal(8, export.DetailRows);
        var details = TypedReportTests.ReadCsv(export.DetailsPath);
        Assert.Equal(8, details.Length);
        Assert.Equal(4, details.Select(row => row["WorkItemId"]).Distinct().Count());
        Assert.Equal(4, details.Select(row => (row["Results.OrthoRate"], row["Results.SelfSymRate"])).Distinct().Count());
        Assert.Equal(4, details.GroupBy(row => row["WorkItemId"]).Count(group => group.Count() == 2));
        foreach (var file in files)
        {
            var item = RunParameterFiles.loadFile(file);
            var store = new SortopiaFileDb(folder, item.ProjectId);
            var reference = Assert.Single(RunParameterExecution.inspectFile(file, Token).Outputs.Available).Reference;
            Assert.True(reference.DataType.IsSorterPoolSet);
            var saved = Assert.IsType<functionData.SorterPoolSet>(((ISortopiaDb)store).Load(reference, Token).ResultValue.Value).Item;
            var evaluations = saved.Pools.SelectMany(pool => pool.Members).Select(member => member.Evaluation).DistinctBy(SorterEval.getSorterId).ToArray();
            var summary = Assert.Single(TypedReportTests.ReadCsv(export.SummaryPath), row => row["WorkItemId"] == item.Id.ToString());
            Assert.Equal(evaluations.Length.ToString(CultureInfo.InvariantCulture), summary["EvaluatedSorterCount"]);
            Assert.Equal(evaluations.Count(SorterEval.getIsSorted).ToString(CultureInfo.InvariantCulture), summary["SuccessfulSorterCount"]);
            foreach (var evaluation in evaluations)
            {
                var v1 = Assert.IsType<sorterEval.V2>(evaluation).Item.ToV1();
                var row = Assert.Single(details, row => row["WorkItemId"] == item.Id.ToString() && row["SorterId"] == v1.SorterId.ToString());
                Assert.Equal("V2", row["EvaluationType"]);
                Assert.Equal(v1.SortableTestsSubsetId, row["SortableTestsSubsetId"]);
                Assert.Equal(v1.ChannelCount.ToString(CultureInfo.InvariantCulture), row["ChannelCount"]);
                Assert.Equal(v1.UnsortedCount.ToString(CultureInfo.InvariantCulture), row["UnsortedCount"]);
                Assert.Equal(v1.CeLength.ToString(CultureInfo.InvariantCulture), row["CeLength"]);
                Assert.Equal(v1.StageLength.ToString(CultureInfo.InvariantCulture), row["StageLength"]);
                Assert.Equal(v1.SequenceHash.ToString(CultureInfo.InvariantCulture), row["SequenceHash"]);
                Assert.Equal(v1.LastCeIndex.ToString(CultureInfo.InvariantCulture), row["LastCeIndex"]);
                Assert.Equal(v1.ReflectionSymmetric ? "TRUE" : "FALSE", row["IsReflectionSymmetric"]);
                Assert.Equal(v1.UnsortedCount == 0 ? "TRUE" : "FALSE", row["IsSorted"]);
                Assert.Equal(v1.StageCrossingsCount.ToString(CultureInfo.InvariantCulture), row["StageCrossingsCount"]);
                Assert.Equal(v1.ReflectiveCount.ToString(CultureInfo.InvariantCulture), row["ReflectiveCount"]);
                Assert.Equal(ResultsFunctionNames.standardMsuf4SimpleSgd, row["ResultsFunction"]);
                Assert.Equal(consumer.Runs[0].Id.ToString(), row["RunId"]);
                Assert.Equal(reference.Id.ToString(), row["ResultsDataId"]);
                Assert.Equal(store.GetPath(reference), row["ResultsFile"]);
                Assert.Equal(saved.TestsId.ToString(), row["SortableTestsId"]);
                Assert.Empty(row["SorterSetEvalId"]); Assert.Empty(row["SorterSetId"]);
                Assert.Contains("channelCount=8", row["Setup.SortableTests.Query.PrefixLibId"]);
                Assert.Equal(item.Parameters.TryFindText("OrthoRate").Value, row["Results.OrthoRate"]);
                Assert.Equal(item.Parameters.TryFindText("SelfSymRate").Value, row["Results.SelfSymRate"]);
            }
        }
        foreach (var file in original) Assert.Equal(file.Value, SHA256.HashData(File.ReadAllBytes(file.Key)));
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "SorterModelSet")));
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "SorterSetEval")));
        var cliDestination = Path.Combine(temporary.FullName, "CLI sweep report");
        Assert.Equal(0, Sortopia.Cli.Program.main(["export-sorter-eval", "--parameters-folder", parametersFolder, "--run", consumer.Runs[0].Id.ToString(), "--output", cliDestination]));
        Assert.Equal(File.ReadAllBytes(export.DetailsPath), File.ReadAllBytes(Path.Combine(cliDestination, "Details.csv")));
        Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles([files[0], files[0]], Path.Combine(temporary.FullName, "Duplicate"), Token));
        Assert.False(Directory.Exists(Path.Combine(temporary.FullName, "Duplicate")));
    }
}
