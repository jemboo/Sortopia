using Microsoft.FSharp.Core;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Evaluation;
using Sortopia.Evaluation.Sgd;
using Sortopia.Evaluation.Serialization;
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
    [InlineData("Constant(2)", 5, new[] { 0, 2, 4 })]
    [InlineData("Constant(2)", 0, new[] { 0 })]
    [InlineData("Exponential(1, 1)", 10, new[] { 0, 1, 3, 7 })]
    [InlineData("Cyclic(Constant(2), 2)", 8, new[] { 0, 2, 3, 5, 6, 8 })]
    [InlineData("Cyclic(Constant(100), 2)", 8, new[] { 0 })]
    [InlineData("Prefixed(3, Constant(100), Constant(1))", 8, new[] { 0 })]
    [InlineData("Prefixed(3, Constant(1), Constant(4))", 10, new[] { 0, 1, 2, 3, 7 })]
    [InlineData("Constant(2147483647)", int.MaxValue, new[] { 0, int.MaxValue })]
    public void Reporting_schedules_use_only_scheduled_points_and_require_an_aligned_final_generation(string schedule, int final, int[] expected)
    {
        Assert.Equal(expected, SgdReporting.samples(schedule, final));
        foreach (var reporting in new[] { new sgdReportingConfig(schedule, "Constant(1)"), new sgdReportingConfig("Constant(1)", schedule) })
        {
            if (expected.Contains(final)) SgdReporting.validateGenerationCount(reporting, final);
            else
            {
                var error = Assert.Throws<ArgumentException>(() => SgdReporting.validateGenerationCount(reporting, final));
                Assert.Contains($"GenerationCount {final}", error.Message);
                Assert.Contains(reporting.SnapshotSchedule == schedule ? "full snapshot schedule" : "summary schedule", error.Message);
            }
        }
    }

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Unaligned_generation_counts_are_rejected_when_resolving_runs_before_any_data_is_written(bool sweep, bool summaries)
    {
        string[] counts = sweep ? ["0", "2", "3", "4"] : ["3"];
        var (world, consumer) = SgdWorkspace(resultsSpans: [new() { keyName = "GenerationCount", values = counts }]);
        var inputs = RunDto.fromDomain(consumer.Runs[0]);
        inputs.reporting = FSharpOption<sgdReportingConfig>.Some(summaries ? new("Constant(1)", "Constant(2)") : new("Constant(2)", "Constant(1)"));
        var error = Assert.Throws<ArgumentException>(() => RunDto.toDomain(consumer, inputs));
        Assert.Contains("GenerationCount 3", error.Message);
        Assert.Contains(summaries ? "summary schedule" : "full snapshot schedule", error.Message);
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        Assert.Empty(DataFiles(folder, "setup"));
        Assert.Empty(DataFiles(folder, "results"));
        Assert.False(Directory.Exists(Path.Combine(folder, "runParameters")));
    }

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
        var reporting = new sgdReportingConfig("Constant(3)", "Constant(1)");
        var configured = new runParameters(original.Id, original.ProjectId, original.RunId, original.Results, original.Parameters,
            original.SetupInputs, original.SetNumber, original.SetupSetNumber, original.SetupSuffixStages, original.ResultsSuffixStages,
            FSharpOption<sgdReportingConfig>.Some(reporting), null);
        var id = RunParameterFiles.saveWorkItem(folder, configured, Token);
        Assert.NotEqual(original.Id, id);
        var path = Path.Combine(parameters, id + ".msgpack");
        var item = RunParameterFiles.loadFile(path);
        Assert.Equal(reporting, item.Reporting.Value);
        Assert.Equal(outputState.Partial, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var completed = RunParameterExecution.executeFile(path, null!, Token); Completed(completed);
        var savedOutputs = Assert.Single(completed.Results).StoredOutputs;
        Assert.True(Assert.Single(savedOutputs, output => Equals(output.Reference.DataType, sortopiaDataType.NewSorterPoolSet(""))).Reused);
        Assert.All(savedOutputs.Where(output => !Equals(output.Reference.DataType, sortopiaDataType.NewSorterPoolSet(""))), output => Assert.False(output.Reused));
        foreach (var file in dataBefore) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));

        var loaded = RunOutputFiles.loadSamples(folder, item, Token);
        Assert.Equal(new[] { 0, 3 }, loaded.Item1.Snapshots);
        Assert.Equal(new[] { 0, 1, 2, 3 }, loaded.Item2.Select(row => row.Generation));
        Assert.Equal(2, loaded.Item2[0].MemberCount);
        Assert.All(loaded.Item2, row => { Assert.Equal(0, row.PoolIndex); Assert.InRange(row.MemberCount, 1, 2); });
        foreach (var generation in loaded.Item1.Snapshots)
        {
            var snapshot = RunOutputFiles.loadSnapshot(folder, item, generation, Token);
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
        Assert.Equal(new[] { "0", "3" }, snapshots.Select(row => row["Generation"]).Distinct());
        Assert.All(snapshots, row => { Assert.Equal("8", row["ChannelCount"]); Assert.NotEmpty(row["ModelId"]); Assert.True(File.Exists(row["SnapshotFile"])); });
        // Normal typed results are shared by equivalent invocations from different runs.
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var keys = RunOutputFiles.keys(item);
        var equivalent = new runParameters(Guid.NewGuid(), item.ProjectId, Guid.NewGuid(), item.Results, item.Parameters,
            item.SetupInputs, 42, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, item.Reporting, item.Sgd);
        Assert.Equal(keys.Select(key => key.Id), RunOutputFiles.keys(equivalent).Select(key => key.Id));
        Assert.Equal(outputState.Complete, RunOutputFiles.inspect(folder, equivalent, Token).State);
        RunParameterFiles.saveWorkItem(folder, equivalent, Token);
        var neverKeep = FuncConvert.FromFunc<storedDataReference, FSharpList<storedDataReference>, bool>((_, _) => false);
        var removedPaths = RunDataCleanup.plan(folder, item.ProjectId, SetModule.Singleton(item.RunId), neverKeep, Token);
        Assert.All(keys, key => Assert.DoesNotContain(store.GetPath(key.Reference), removedPaths));
        var denser = new runParameters(item.Id, item.ProjectId, item.RunId, item.Results, item.Parameters, item.SetupInputs,
            item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, FSharpOption<sgdReportingConfig>.Some(new("Constant(1)", "Constant(1)")), null);
        Assert.All(RunOutputFiles.samples(item).Snapshots, snapshot =>
            Assert.Contains(RunOutputFiles.samples(denser).Snapshots, other => other.Item1 == snapshot.Item1 && other.Item2.Id == snapshot.Item2.Id));
        var different = new runParameters(item.Id, item.ProjectId, item.RunId, item.Results,
            item.Parameters.WithBinding(ParameterBinding.create(ParamKeys.orthoRateKey, 99.0)), item.SetupInputs,
            item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, item.Reporting, item.Sgd);
        // Intermediate samples include all parameters; the final result retains
        // the project's saved query projection (GenerationCount in this fixture).
        var finalKey = RunOutputFiles.samples(item).Snapshots.Last().Item2;
        Assert.Empty(RunOutputFiles.keys(different).Where(key => key.Id != finalKey.Id)
            .Select(key => key.Id).Intersect(keys.Where(key => key.Id != finalKey.Id).Select(key => key.Id)));
        var sliceBytes = keys.Select(key => store.GetPath(key.Reference)).ToDictionary(file => file, File.ReadAllBytes);
        Assert.All(keys.Where(key => key.Id != finalKey.Id), key => Assert.Contains("GenerationCurrent", store.ReadQueryParameters(key.Reference, Token).Select(binding => binding.KeyName)));
        Assert.Contains(keys, key => key.Reference.DataType.IsSorterPoolSummarySet);
        Assert.False(Directory.Exists(Path.Combine(folder, "data", "results", "evolution")));
        var missingSnapshot = RunOutputFiles.snapshotPath(folder, item, 0);
        File.Delete(missingSnapshot);
        var repaired = RunParameterExecution.executeFile(path, null!, Token); Completed(repaired);
        Assert.Equal(keys.Length, Assert.Single(repaired.Results).StoredOutputs.Length);
        Assert.Single(Assert.Single(repaired.Results).StoredOutputs, output => !output.Reused);
        foreach (var file in sliceBytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var damaged = sliceBytes.First(); File.WriteAllBytes(damaged.Key, [0]);
        Assert.Equal(outputState.Invalid, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        var failedExport = Path.Combine(temporary.FullName, "Corrupt CSV");
        Assert.ThrowsAny<Exception>(() => SorterEvalReportExport.exportFiles([path], failedExport, Token));
        Assert.False(Directory.Exists(failedExport));
    }

    [Theory]
    [InlineData(0, "Constant(2)", new[] { 0 })]
    [InlineData(3, "Constant(3)", new[] { 0, 3 })]
    [InlineData(4, "Constant(2)", new[] { 0, 2, 4 })]
    [InlineData(3, "Exponential(1, 1)", new[] { 0, 1, 3 })]
    public void Final_result_is_the_last_snapshot_and_is_counted_saved_exported_and_cleaned_up_once(
        int generations, string schedule, int[] snapshotGenerations)
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "GenerationCount", values = [generations.ToString()] }
        ], reporting: new(schedule, "Constant(1)"));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parameters = Path.Combine(folder, "runParameters");
        var item = Assert.Single(RunParameterFiles.listWorkItems(parameters, Token));
        var path = Path.Combine(parameters, item.Id + ".msgpack");
        var executed = RunParameterExecution.executeFile(path, null!, Token); Completed(executed);
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var final = Assert.Single(FileResultsHost.plan(item.ProjectId, item.Results, item.Parameters, item.SetupInputs)).Value.Key;
        var samples = RunOutputFiles.samples(item);
        var keys = RunOutputFiles.keys(item);
        var finalPath = store.GetPath(final.Reference);
        Assert.Equal(snapshotGenerations, samples.Snapshots.Select(sample => sample.Item1));
        Assert.Equal(final.Reference, samples.Snapshots.Last().Item2.Reference);
        Assert.Equal(finalPath, RunOutputFiles.snapshotPath(folder, item, generations));
        Assert.Equal(new[] { "GenerationCount" }, store.ReadQueryParameters(final.Reference, Token).Select(binding => binding.KeyName));
        Assert.Equal(keys.Length, DataFiles(folder, "results").Length);
        Assert.Equal(keys.Length, Assert.Single(executed.Results).StoredOutputs.Length);
        Assert.Equal(snapshotGenerations.Length, Directory.GetFiles(Path.GetDirectoryName(finalPath)!, "*.msgpack").Length);
        foreach (var status in new[] { RunParameterExecution.inspectFile(path, Token), RunParameterExecution.inspectFileIntegrity(path, Token) })
        {
            Assert.Equal(outputState.Complete, status.Outputs.State);
            Assert.Equal(keys.Length, status.Outputs.ExpectedCount);
            Assert.Equal(keys.Length, status.Outputs.Available.Select(output => output.Reference).Distinct().Count());
        }
        var exported = SorterEvalReportExport.exportFiles([path], Path.Combine(temporary.FullName, "Shared final CSV"), Token);
        Assert.All(TypedReportTests.ReadCsv(exported.SnapshotsPath.Value).Where(row => row["Generation"] == generations.ToString()),
            row => { Assert.Equal(finalPath, row["SnapshotFile"]); Assert.Equal(finalPath, row["ResultsFile"]); });

        var bytes = keys.ToDictionary(key => store.GetPath(key.Reference), key => File.ReadAllBytes(store.GetPath(key.Reference)));
        File.Delete(finalPath);
        foreach (var status in new[] { RunParameterExecution.inspectFile(path, Token), RunParameterExecution.inspectFileIntegrity(path, Token) })
        {
            Assert.Equal(outputState.Partial, status.Outputs.State);
            Assert.Equal(keys.Length, status.Outputs.ExpectedCount);
            Assert.Equal(keys.Length - 1, status.Outputs.Available.Length);
        }
        Completed(RunParameterExecution.executeFile(path, null!, Token));
        foreach (var file in bytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));

        // A surviving invocation without reporting still owns the shared final file.
        var plain = new runParameters(Guid.NewGuid(), item.ProjectId, Guid.NewGuid(), item.Results, item.Parameters,
            item.SetupInputs, item.SetNumber, item.SetupSetNumber, item.SetupSuffixStages, item.ResultsSuffixStages, null!, null);
        RunParameterFiles.saveWorkItem(folder, plain, Token);
        var neverKeep = FuncConvert.FromFunc<storedDataReference, FSharpList<storedDataReference>, bool>((_, _) => false);
        var removed = RunDataCleanup.plan(folder, item.ProjectId, SetModule.Singleton(item.RunId), neverKeep, Token);
        Assert.DoesNotContain(finalPath, removed);
        Assert.All(keys.Where(key => key.Id != final.Id), key => Assert.Contains(store.GetPath(key.Reference), removed));
        var removeAll = RunDataCleanup.plan(folder, item.ProjectId, SetModule.OfSeq(new[] { item.RunId, plain.RunId }), neverKeep, Token);
        Assert.All(keys, key => Assert.Contains(store.GetPath(key.Reference), removeAll));

        // A shared file with the wrong final generation must fail full validation,
        // even if its schema and checksum are valid.
        var initial = RunOutputFiles.loadSnapshot(folder, item, 0, Token);
        var wrong = new sorterPoolSet(generations + 1, initial.TestsId, initial.Pools);
        File.Delete(finalPath);
        var saved = ((ISortopiaDb)store).GetOrCreate(final,
            FuncConvert.FromFunc(() => FSharpResult<sortopiaStorageData, string>.NewOk(sortopiaStorageData.NewSorterPoolSet(wrong))), Token);
        Assert.True(saved.IsOk);
        var invalid = RunParameterExecution.inspectFile(path, Token);
        Assert.Equal(outputState.Invalid, invalid.Outputs.State);
        Assert.Contains("generation", invalid.Outputs.Error);
        Assert.Equal(runExecutionStatus.Failed, RunParameterExecution.executeFile(path, null!, Token).Status);
    }

    [Fact]
    public void Replaying_missing_samples_rejects_a_valid_but_different_saved_snapshot()
    {
        var (world, consumer) = SgdWorkspace(reporting: new("Constant(3)", "Constant(1)"));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var item = Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        var path = Path.Combine(folder, "runParameters", item.Id + ".msgpack");
        Completed(RunParameterExecution.executeFile(path, null!, Token));
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var samples = RunOutputFiles.samples(item);
        var snapshotKey = samples.Snapshots[0].Item2;
        var snapshot = RunOutputFiles.loadSnapshot(folder, item, 0, Token);
        var pool = snapshot.Pools[0];
        var changed = new sorterPoolSet(snapshot.Generation, snapshot.TestsId,
            [new sorterPool(pool.Id, pool.RawCeLength + 1, pool.Members)]);
        var snapshotPath = store.GetPath(snapshotKey.Reference);
        File.Delete(snapshotPath);
        Assert.True(((ISortopiaDb)store).GetOrCreate(snapshotKey,
            FuncConvert.FromFunc(() => FSharpResult<sortopiaStorageData, string>.NewOk(sortopiaStorageData.NewSorterPoolSet(changed))), Token).IsOk);
        var bytes = File.ReadAllBytes(snapshotPath);
        File.Delete(store.GetPath(samples.Summaries[0].Item2.Reference));
        var replay = RunParameterExecution.executeFile(path, null!, Token);
        Assert.Equal(runExecutionStatus.Failed, replay.Status);
        Assert.Contains("differs from replay", replay.Error);
        Assert.Equal(bytes, File.ReadAllBytes(snapshotPath));
    }

    [Fact]
    public void Direct_execution_honors_saved_run_reporting_and_removal_cleans_up_samples()
    {
        var (world, consumer) = SgdWorkspace(reporting: new("Constant(3)", "Constant(1)"));
        Completed(FileRunExecution.execute(world, Root, consumer.Runs[0], null!, Token));
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        var item = Assert.Single(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token));
        Assert.Equal(outputState.Complete, RunOutputFiles.inspect(folder, item, Token).State);
        var document = WorkspaceFileStore.LoadDocument(Root);
        WorkspaceFileStore.RemoveRunParameterFiles(Root, document, consumer.Id, consumer.Runs[0].Id, Token);
        Assert.All(RunOutputFiles.keys(item), key => Assert.False(File.Exists(new SortopiaFileDb(folder, item.ProjectId).GetPath(key.Reference))));
    }

    [Theory]
    [InlineData("Constant(1)", new[] { 0, 1, 2, 3, 4, 5, 6 })]
    [InlineData("Constant(2)", new[] { 0, 2, 4, 6 })]
    [InlineData("Constant(6)", new[] { 0, 6 })]
    public void Summary_sets_are_the_only_history_and_follow_the_selected_interval(string interval, int[] generations)
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "GenerationCount", values = ["6"] },
            new() { keyName = "SorterPoolCount", values = ["2"] }
        ], reporting: new("Constant(1)", interval));
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(WorkspaceData.projectFolder(world, Root, consumer.Id));
        var parameters = Path.Combine(folder, "runParameters");
        var item = Assert.Single(RunParameterFiles.listWorkItems(parameters, Token));
        var executed = RunParameterExecution.executeFolder(parameters, null!, null!, Token); Completed(executed);
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var resultReference = Assert.Single(Assert.Single(executed.Results).StoredOutputs,
            output => output.Reference.DataType.Equals(sortopiaDataType.NewSorterPoolSet(""))).Reference;
        var final = Assert.IsType<sortopiaStorageData.SorterPoolSet>(((ISortopiaDb)store).Load(resultReference, Token).ResultValue.Value).Item;
        var samples = RunOutputFiles.loadSamples(folder, item, Token);
        Assert.Equal(generations, samples.Item1.SummaryGenerations);
        Assert.Equal(generations.SelectMany(g => new[] { (g, 0), (g, 1) }), samples.Item2.Select(row => (row.Generation, row.PoolIndex)));
        var plan = RunOutputFiles.samples(item);
        var storedRows = plan.Summaries.SelectMany(batch =>
            Assert.IsType<sortopiaStorageData.SorterPoolSummarySet>(((ISortopiaDb)store).Load(batch.Item2.Reference, Token).ResultValue.Value).Item.Rows).ToArray();
        Assert.Equal(MessagePackSetup.serialize(samples.Item2), MessagePackSetup.serialize(storedRows));
        foreach (var generation in samples.Item1.Snapshots.Intersect(generations))
        {
            var snapshot = RunOutputFiles.loadSnapshot(folder, item, generation, Token);
            var expected = EvolutionReports.summarize(SorterEvalFunctions.fromCompactString(item.Parameters.TryFindText("SorterEvalMeasure").Value), snapshot);
            var actual = samples.Item2.Where(row => row.Generation == generation).ToArray();
            Assert.Equal(MessagePackSetup.serialize(expected), MessagePackSetup.serialize(actual));
        }
        var last = RunOutputFiles.loadSnapshot(folder, item, final.Generation, Token);
        Assert.Equal(MessagePackSetup.serialize(SorterPoolSetDto.fromDomain(final)), MessagePackSetup.serialize(SorterPoolSetDto.fromDomain(last)));
        var export = SorterEvalReportExport.exportFolder(parameters, null!, Path.Combine(temporary.FullName, "Scheduled history"), Token);
        Assert.Equal(generations.Length * 2, export.EvolutionSummaryRows);
        Assert.Equal(generations.SelectMany(g => new[] { g.ToString(), g.ToString() }),
            TypedReportTests.ReadCsv(export.EvolutionSummaryPath.Value).Select(row => row["Generation"]));
    }
}
