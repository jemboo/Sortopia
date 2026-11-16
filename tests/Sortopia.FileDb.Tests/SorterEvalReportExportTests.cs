using System.Globalization;
using System.Security.Cryptography;
using Sortopia.Evaluation;
using Sortopia.Functions;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Csv_reports_export_saved_evaluations_and_inherited_queries_from_a_copied_project()
    {
        var (world, _, _, consumer, _, _) = EvaluationCopyWorkspace(8, prefix: true);
        Completed(Prepare(world, consumer).Execution);
        var copy = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parametersFolder = Path.Combine(copy, "runParameters");
        Completed(RunParameterExecution.executeFolder(parametersFolder, null!, null!, Token));
        var files = Directory.GetFiles(parametersFolder, "*.msgpack");
        var original = Directory.GetFiles(copy, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
        var export = SorterEvalReportExport.exportFiles(files, Path.Combine(temporary.FullName, "Report"), Token);
        Assert.Equal(2, export.SummaryRows); Assert.Equal(6, export.DetailRows);
        var summary = TypedReportTests.ReadCsv(export.SummaryPath); var details = TypedReportTests.ReadCsv(export.DetailsPath);
        Assert.Equal(2, summary.Length); Assert.Equal(6, details.Length);
        foreach (var row in summary)
        {
            var item = RunParameterFiles.loadFile(row["ParameterFile"]);
            var store = new SortopiaFileDb(copy, item.ProjectId);
            var reference = Assert.Single(RunParameterExecution.inspectFile(row["ParameterFile"], Token).Outputs.Available).Reference;
            var evaluations = Assert.IsType<functionData.SorterSetEval>(((ISortopiaDb)store).Load(reference, Token).ResultValue.Value).Item;
            Assert.Equal(reference.Id.ToString(), row["ResultsDataId"]); Assert.Equal(item.Id.ToString(), row["WorkItemId"]);
            Assert.Equal(item.RunId.ToString(), row["RunId"]); Assert.Equal(evaluations.SorterTestId.ToString(), row["SortableTestsId"]);
            Assert.Equal("8", row["Setup.SorterModelSet.Query.ChannelCount"]);
            Assert.Contains("channelCount=8", row["Setup.SortableTests.Query.PrefixLibId"]);
            Assert.Equal("V1", row["Results.SorterEvalType"]); Assert.Equal("FALSE", row["Results.CollectSortableTests"]);
            Assert.Equal(copy, Path.GetDirectoryName(Path.GetDirectoryName(row["ParameterFile"])));
            foreach (var input in item.SetupInputs)
            {
                var prefix = "Setup." + Sortopia.Runs.SortopiaDataType.kindName(input.DataType);
                Assert.Equal(input.Id.ToString(), row[prefix + ".DataId"]);
                foreach (var binding in store.ReadQueryParameters(input, Token))
                    Assert.Equal(binding.FormattedValue, row[prefix + ".Query." + binding.KeyName]);
            }
            var ownDetails = details.Where(detail => detail["ResultsDataId"] == row["ResultsDataId"]).ToArray();
            Assert.Equal(evaluations.SorterEvals.Length, ownDetails.Length);
            Assert.Equal(ownDetails.Count(detail => detail["IsSorted"] == "TRUE").ToString(), row["SuccessfulSorterCount"]);
            Assert.Equal((double)ownDetails.Count(detail => detail["IsSorted"] == "TRUE") / ownDetails.Length,
                double.Parse(row["ObservedSuccessFraction"], CultureInfo.InvariantCulture));
            Assert.All(ownDetails, detail => Assert.Equal(row["Setup.SortableTests.Query.PrefixLibId"], detail["Setup.SortableTests.Query.PrefixLibId"]));
        }
        var cliFolder = Path.Combine(temporary.FullName, "CLI report");
        Assert.Equal(0, Sortopia.Cli.Program.main(["export-sorter-eval", "--parameters-folder", parametersFolder, "--run", consumer.Runs[0].Id.ToString(), "--output", cliFolder]));
        Assert.Equal(File.ReadAllBytes(export.SummaryPath), File.ReadAllBytes(Path.Combine(cliFolder, "Summary.csv")));
        Assert.Equal(File.ReadAllBytes(export.DetailsPath), File.ReadAllBytes(Path.Combine(cliFolder, "Details.csv")));
        Assert.Equal(2, Sortopia.Cli.Program.main(["export-sorter-eval", "--parameters", files[0], "--run", consumer.Runs[0].Id.ToString(), "--output", "unused"]));
        Assert.Throws<ArgumentException>(() => SorterEvalReportExport.exportFiles(files, export.Folder, Token));
        Assert.Contains("same results file", Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles([files[0], files[0]], Path.Combine(temporary.FullName, "Duplicates"), Token)).Message);
        Assert.Equal(original.Keys.Order(), Directory.GetFiles(copy, "*", SearchOption.AllDirectories).Order());
        Assert.All(original, pair => Assert.Equal(pair.Value, SHA256.HashData(File.ReadAllBytes(pair.Key))));
    }

    [Fact]
    public void Csv_report_failures_leave_no_partial_export_or_overwritten_report()
    {
        var (world, project) = Create(true); Completed(Prepare(world, project).Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var files = Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack").Order().ToArray();
        var output = Path.Combine(temporary.FullName, "Report");
        Assert.Contains("not been generated", Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles(files, output, Token)).Message);
        Assert.False(Directory.Exists(output));
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        var item = RunParameterFiles.loadFile(files[^1]); var store = new SortopiaFileDb(folder, item.ProjectId);
        var reference = Assert.Single(RunParameterExecution.inspectFile(files[^1], Token).Outputs.Available).Reference;
        var resultPath = store.GetPath(reference); var bytes = File.ReadAllBytes(resultPath);
        File.WriteAllBytes(resultPath, [1, 2, 3]);
        Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles(files, output, Token));
        Assert.False(Directory.Exists(output)); Assert.Empty(Directory.GetDirectories(temporary.FullName, ".sortopia-report-*.tmp"));
        File.WriteAllBytes(resultPath, bytes);
        File.Delete(store.GetPath(item.SetupInputs[0]));
        Assert.Contains("Cannot report", Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles(files, output, Token)).Message);
        Assert.False(Directory.Exists(output));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => SorterEvalReportExport.exportFiles(files, output, cancelled.Token));
        Assert.Throws<ArgumentException>(() => SorterEvalReportExport.exportFiles([], output, Token));
    }

    [Fact]
    public void Csv_reports_reject_root_runs()
    {
        var (world, project) = Create(false); Completed(Prepare(world, project).Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        Assert.False(SorterEvalReportExport.isSupported(RunParameterFiles.loadFile(file)));
        Assert.Contains("branch", Assert.Throws<InvalidOperationException>(() => SorterEvalReportExport.exportFiles([file], Path.Combine(temporary.FullName, "Report"), Token)).Message);
    }
}
