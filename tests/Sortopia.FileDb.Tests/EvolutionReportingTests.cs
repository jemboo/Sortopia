using Microsoft.FSharp.Core;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Evaluation;
using Sortopia.Evaluation.Sgd;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData("Constant(100)", 100, new[] { 0, 100 })]
    [InlineData("Constant(2)", 5, new[] { 0, 2, 4, 5 })]
    [InlineData("Constant(2)", 0, new[] { 0 })]
    [InlineData("Exponential(1, 1)", 10, new[] { 0, 1, 3, 7, 10 })]
    [InlineData("Cyclic(Constant(2), 2)", 8, new[] { 0, 2, 3, 5, 6, 8 })]
    [InlineData("Cyclic(Constant(100), 2)", 8, new[] { 0, 8 })]
    [InlineData("Prefixed(3, Constant(100), Constant(1))", 8, new[] { 0, 8 })]
    [InlineData("Prefixed(3, Constant(1), Constant(4))", 10, new[] { 0, 1, 2, 3, 7, 10 })]
    [InlineData("Constant(2147483647)", int.MaxValue, new[] { 0, int.MaxValue })]
    public void Reporting_schedules_are_bounded_and_include_both_endpoints(string schedule, int final, int[] expected)
        => Assert.Equal(expected, SgdReporting.samples(schedule, final));

    [Theory]
    [InlineData("Constant(0)")]
    [InlineData("Constant(-1)")]
    [InlineData("Exponential(NaN, 1)")]
    [InlineData("Exponential(-0.1, 1)")]
    [InlineData("Exponential(0.1, 0)")]
    [InlineData("Cyclic(Constant(1), 0)")]
    [InlineData("Prefixed(-1, Constant(1), Constant(2))")]
    public void Invalid_reporting_schedules_are_rejected(string schedule)
        => Assert.ThrowsAny<ArgumentException>(() => SgdReporting.parse(schedule));

    [Fact]
    public void Evolution_reports_replay_cached_results_portably_export_all_samples_and_detect_corruption()
    {
        var (world, consumer) = SgdWorkspace();
        var runDefinition = RunDto.fromDomain(consumer.Runs[0]);
        Assert.Equal(runDefinition, MessagePackSetup.deserialize<runDto>(MessagePackSetup.serialize(runDefinition)));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parameters = Path.Combine(folder, "runParameters");
        var original = Assert.Single(RunParameterFiles.listWorkItems(parameters, Token));
        // Adding optional reporting must not rewrite existing invocation bytes or data identities.
        Assert.Equal(MessagePackSetup.serialize(RunParameterCodec.toDto(original)), RunParameterCodec.encode(original));
        Completed(RunParameterExecution.executeFolder(parameters, null!, null!, Token));
        var dataBefore = Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        var reporting = new sgdReportingConfig("Constant(2)", "Constant(1)");
        var configured = new runParameters(original.Id, original.ProjectId, original.RunId, original.Results, original.Parameters,
            original.SetupInputs, original.SetNumber, original.SetupSetNumber, original.SetupSuffixStages, original.ResultsSuffixStages,
            FSharpOption<sgdReportingConfig>.Some(reporting));
        var id = RunParameterFiles.saveWorkItem(folder, configured, Token);
        Assert.NotEqual(original.Id, id);
        var path = Path.Combine(parameters, id + ".msgpack");
        var item = RunParameterFiles.loadFile(path);
        Assert.Equal(reporting, item.Reporting.Value);
        Assert.Equal(outputState.Partial, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var completed = RunParameterExecution.executeFile(path, null!, Token); Completed(completed);
        Assert.All(Assert.Single(completed.Results).StoredOutputs, output => Assert.True(output.Reused));
        foreach (var file in dataBefore) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));

        var loaded = EvolutionReportingStore.load(folder, item, Token);
        Assert.Equal(new[] { 0, 2, 3 }, loaded.Item1.Snapshots);
        Assert.Equal(new[] { 0, 1, 2, 3 }, loaded.Item2.Select(row => row.Generation));
        Assert.Equal(2, loaded.Item2[0].MemberCount);
        Assert.All(loaded.Item2, row => { Assert.Equal(0, row.PoolIndex); Assert.InRange(row.MemberCount, 1, 2); });
        foreach (var generation in loaded.Item1.Snapshots)
        {
            var snapshot = EvolutionReportingStore.loadSnapshot(folder, item, generation, Token);
            var row = Assert.Single(loaded.Item2, row => row.Generation == generation);
            var evaluations = snapshot.Pools[0].Members.Select(member => member.Evaluation).ToArray();
            var lengths = evaluations.Select(value => (double)SorterEval.getCeLength(value)).ToArray();
            Assert.Equal(lengths.Min(), row.MinimumCeLength);
            Assert.Equal(lengths.Average(), row.MeanCeLength, 12);
            Assert.Equal(Math.Sqrt(lengths.Average(value => Math.Pow(value - lengths.Average(), 2))), row.StdDevCeLength, 12);
            Assert.Equal(evaluations.Average(value => (double)SorterEval.getStageLength(value)), row.MeanStageLength, 12);
            Assert.Equal(evaluations.Count(SorterEval.getIsSorted), row.SuccessfulCount);
        }
        var destination = Path.Combine(temporary.FullName, "Evolution CSV");
        var exported = SorterEvalReportExport.exportFiles([path], destination, Token);
        Assert.Equal(4, exported.EvolutionSummaryRows);
        Assert.Equal(loaded.Item2.Where(row => loaded.Item1.Snapshots.Contains(row.Generation)).Sum(row => row.MemberCount), exported.SnapshotRows);
        var summary = TypedReportTests.ReadCsv(exported.EvolutionSummaryPath.Value);
        Assert.Equal(new[] { "0", "1", "2", "3" }, summary.Select(row => row["Generation"]));
        Assert.All(summary, row => Assert.Contains("channelCount=8", row["Setup.SortableTests.Query.PrefixLibId"]));
        var snapshots = TypedReportTests.ReadCsv(exported.SnapshotsPath.Value);
        Assert.Equal(new[] { "0", "2", "3" }, snapshots.Select(row => row["Generation"]).Distinct());
        Assert.All(snapshots, row => { Assert.Equal("8", row["ChannelCount"]); Assert.NotEmpty(row["ModelId"]); Assert.True(File.Exists(row["SnapshotFile"])); });
        // Normal typed results are shared by equivalent invocations from different runs.
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var keys = EvolutionReportingStore.keys(item);
        var equivalent = new runParameters(Guid.NewGuid(), item.ProjectId, Guid.NewGuid(), item.Results, item.Parameters,
            item.SetupInputs, 42, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, item.Reporting);
        Assert.Equal(keys.Select(key => key.Id), EvolutionReportingStore.keys(equivalent).Select(key => key.Id));
        Assert.Equal(outputState.Complete, EvolutionReportingStore.inspect(folder, equivalent, Token).Item1);
        RunParameterFiles.saveWorkItem(folder, equivalent, Token);
        var neverKeep = FuncConvert.FromFunc<storedDataReference, FSharpList<storedDataReference>, bool>((_, _) => false);
        var removedPaths = RunDataCleanup.plan(folder, item.ProjectId, SetModule.Singleton(item.RunId), neverKeep, Token);
        Assert.All(keys, key => Assert.DoesNotContain(store.GetPath(key.Reference), removedPaths));
        var denser = new runParameters(item.Id, item.ProjectId, item.RunId, item.Results, item.Parameters, item.SetupInputs,
            item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, FSharpOption<sgdReportingConfig>.Some(new("Constant(1)", "Constant(1)")));
        Assert.All(EvolutionReportingStore.plan(item).Snapshots, snapshot =>
            Assert.Contains(EvolutionReportingStore.plan(denser).Snapshots, other => other.Item1 == snapshot.Item1 && other.Item2.Id == snapshot.Item2.Id));
        var different = new runParameters(item.Id, item.ProjectId, item.RunId, item.Results,
            item.Parameters.WithBinding(ParameterBinding.create(ParamKeys.orthoRateKey, 99.0)), item.SetupInputs,
            item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, item.Reporting);
        Assert.Empty(EvolutionReportingStore.keys(different).Select(key => key.Id).Intersect(keys.Select(key => key.Id)));
        var sliceBytes = keys.Select(key => store.GetPath(key.Reference)).ToDictionary(file => file, File.ReadAllBytes);
        Assert.All(keys, key => Assert.Contains("GenerationCurrent", store.ReadQueryParameters(key.Reference, Token).Select(binding => binding.KeyName)));
        Assert.Contains(keys, key => key.Reference.DataType.IsSorterPoolSummarySet);
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "evolution")));
        File.Delete(sliceBytes.First().Key);
        Completed(RunParameterExecution.executeFile(path, null!, Token));
        foreach (var file in sliceBytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var damaged = sliceBytes.First(); File.WriteAllBytes(damaged.Key, [0]);
        Assert.Equal(outputState.Invalid, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var failedExport = Path.Combine(temporary.FullName, "Corrupt CSV");
        Assert.ThrowsAny<Exception>(() => SorterEvalReportExport.exportFiles([path], failedExport, Token));
        Assert.False(Directory.Exists(failedExport));
    }

    [Fact]
    public void Direct_execution_honors_saved_run_reporting_and_removal_cleans_up_samples()
    {
        var (world, consumer) = SgdWorkspace(reporting: new("Constant(2)", "Constant(1)"));
        Completed(FileRunExecution.execute(world, Root, consumer.Runs[0], null!, Token));
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        var item = Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        Assert.Equal(outputState.Complete, EvolutionReportingStore.inspect(folder, item, Token).Item1);
        var document = WorkspaceFileStore.LoadDocument(Root);
        WorkspaceFileStore.RemoveRunParameterFiles(Root, document, consumer.Id, consumer.Runs[0].Id, Token);
        Assert.All(EvolutionReportingStore.keys(item), key => Assert.False(File.Exists(new SortopiaFileDb(folder, item.ProjectId).GetPath(key.Reference))));
    }
}
