using Sortopia.Workspaces.Serialization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Sortopia.Evaluation;
using Sortopia.Evaluation.Sgd;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sgd_sweep_exports_only_evolution_summary_with_pool_statistics_and_saved_parameters(bool msrs)
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "OrthoRate", values = ["1", "2"] },
            new() { keyName = "SelfSymRate", values = ["0.025", "0.030"] }], sgd: new(1, 0, null), msrs: msrs);
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var parametersFolder = Path.Combine(folder, "runParameters");
        Completed(RunParameterExecution.executeFolder(parametersFolder, null!, null!, Token));
        var files = Directory.GetFiles(parametersFolder, "*.msgpack");
        Assert.Equal(4, files.Length);
        Assert.All(files, file => Assert.True(SorterEvalReportExport.isSupported(RunParameterFiles.loadFile(file))));
        var original = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var destination = Path.Combine(temporary.FullName, "Sweep report");
        var export = SorterEvalReportExport.exportFolder(parametersFolder, FSharpOption<Guid>.Some(consumer.Runs[0].id), destination, Token);
        Assert.Equal(new[] { "EvolutionSummary.csv" }, Directory.GetFiles(destination).Select(Path.GetFileName));
        Assert.Null(export.SummaryPath); Assert.Null(export.DetailsPath);
        Assert.Equal(0, export.SummaryRows); Assert.Equal(0, export.DetailRows);
        Assert.Equal(4, export.EvolutionSummaryRows);
        var rows = TypedReportTests.ReadCsv(export.EvolutionSummaryPath.Value);
        Assert.Equal(4, rows.Length);
        Assert.Equal(4, rows.Select(row => row["WorkItemId"]).Distinct().Count());
        Assert.Equal(4, rows.Select(row => (row["Results.OrthoRate"], row["Results.SelfSymRate"])).Distinct().Count());
        foreach (var file in files)
        {
            var item = RunParameterFiles.loadFile(file);
            var saved = RunOutputFiles.loadSnapshot(folder, item, 0, Token);
            var pool = Assert.Single(saved.Pools);
            var evaluations = pool.Members.Select(member => member.Evaluation).ToArray();
            var row = Assert.Single(rows, row => row["WorkItemId"] == item.Id.ToString());
            Assert.Equal("0", row["Generation"]); Assert.Equal("0", row["PoolIndex"]);
            Assert.Equal(pool.Id.ToString(), row["PoolId"]);
            Assert.Equal(evaluations.Length.ToString(CultureInfo.InvariantCulture), row["MemberCount"]);
            Assert.Equal(evaluations.Count(SorterEval.getIsSorted).ToString(CultureInfo.InvariantCulture), row["SuccessfulCount"]);
            Assert.Equal(evaluations.Average(value => (double)SorterEval.getCeLength(value)), double.Parse(row["MeanCeLength"], CultureInfo.InvariantCulture), 12);
            Assert.Equal(evaluations.Min(value => (double)SorterEval.getStageLength(value)), double.Parse(row["MinimumStageLength"], CultureInfo.InvariantCulture));
            Assert.Equal(item.Results.FunctionName, row["ResultsFunction"]);
            Assert.Equal(consumer.Runs[0].id.ToString(), row["RunId"]);
            Assert.Equal(RunOutputFiles.snapshotPath(folder, item, 0), row["ResultsFile"]);
            Assert.Contains("channelCount=8", row["Setup.SortableTests.Query.PrefixLibId"]);
            Assert.Equal(item.Parameters.TryFindText("OrthoRate").Value, row["Results.OrthoRate"]);
            Assert.Equal(item.Parameters.TryFindText("SelfSymRate").Value, row["Results.SelfSymRate"]);
            var stored = Assert.Single(RunOutputFiles.loadSamples(folder, item, Token).Item2);
            foreach (var successful in new[] { true, false })
            {
                var name = successful ? "SuccessfulLengthHistogram" : "UnsuccessfulLengthHistogram";
                var histogram = successful ? stored.SuccessfulLengthHistogram.Value : stored.UnsuccessfulLengthHistogram.Value;
                var triples = JsonSerializer.Deserialize<int[][]>(row[name])!;
                Assert.All(triples, triple => Assert.Equal(3, triple.Length));
                Assert.Equal(histogram.Select(bin => (bin.CeLength, bin.StageLength, bin.Count)),
                    triples.Select(triple => (triple[0], triple[1], triple[2])));
                Assert.Equal(evaluations.Count(value => SorterEval.getIsSorted(value) == successful), histogram.Sum(bin => bin.Count));
                foreach (var bin in histogram)
                    Assert.Equal(evaluations.Count(value => SorterEval.getIsSorted(value) == successful
                        && SorterEval.getCeLength(value) == bin.CeLength && SorterEval.getStageLength(value) == bin.StageLength), bin.Count);
            }
        }
        foreach (var file in original) Assert.Equal(file.Value, SHA256.HashData(File.ReadAllBytes(file.Key)));
        var cliDestination = Path.Combine(temporary.FullName, "CLI sweep report");
        Assert.Equal(0, Sortopia.Cli.Program.main(["export-sorter-eval", "--parameters-folder", parametersFolder, "--run", consumer.Runs[0].id.ToString(), "--output", cliDestination]));
        Assert.Equal(new[] { "EvolutionSummary.csv" }, Directory.GetFiles(cliDestination).Select(Path.GetFileName));
        Assert.Equal(File.ReadAllBytes(export.EvolutionSummaryPath.Value), File.ReadAllBytes(Path.Combine(cliDestination, "EvolutionSummary.csv")));
        Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles([files[0], files[0]], Path.Combine(temporary.FullName, "Duplicate"), Token));
        Assert.False(Directory.Exists(Path.Combine(temporary.FullName, "Duplicate")));

        // Re-export removes obsolete CSVs while keeping user files and rolling back on failure.
        foreach (var name in new[] { "Summary.csv", "Details.csv", "Snapshots.csv" })
            File.WriteAllText(Path.Combine(destination, name), "old report");
        File.WriteAllText(Path.Combine(destination, "Notes.csv"), "keep this");
        var previous = Directory.GetFiles(destination).ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(Path.Combine(destination, "Snapshots.csv"), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => SorterEvalReportExport.exportFiles([files[0]], destination, Token));
        foreach (var file in previous) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        SorterEvalReportExport.exportFiles([files[0]], destination, Token);
        Assert.Equal(new[] { "EvolutionSummary.csv", "Notes.csv" }, Directory.GetFiles(destination).Select(Path.GetFileName).Order());
        Assert.Single(TypedReportTests.ReadCsv(export.EvolutionSummaryPath.Value));
        Assert.Equal("keep this", File.ReadAllText(Path.Combine(destination, "Notes.csv")));
    }
}
