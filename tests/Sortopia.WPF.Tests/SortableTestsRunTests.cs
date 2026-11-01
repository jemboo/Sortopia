using System.IO;
using System.Text.Json;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.WPF.ViewModels;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SortableTestsRunTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("Sortopia-runs-test-").FullName;
    private RunLibrary Library => new(directory);

    [Fact]
    public async Task BothGeneratorsCanRunAndTheirOutputsCanBePublishedAtDifferentSlotsAfterRestart()
    {
        var vm = new SortableTestsPageViewModel(() => { }, Library) { RunName = "First run" };
        vm.Add.Execute(null);
        vm.Generator = "Prefix";
        vm.Prefix = vm.Prefixes.Single(p => p.Channels == 8);
        vm.Add.Execute(null);
        await vm.ExecuteAsync();

        var run = Assert.Single(vm.Runs);
        Assert.Equal(1, vm.Tab);
        Assert.Equal(2, run.Outputs.Length);
        Assert.All(run.Outputs, output => Assert.True(output.TestCount > 0));
        var all = Assert.Single(vm.Manifests);
        var reopened = new SortableTestsPageViewModel(() => { }, Library);
        reopened.Open();
        Assert.Equal(run.Id, reopened.SelectedRun!.Id);
        Assert.Equal(all.Id, Assert.Single(reopened.Manifests).Id);
        reopened.Slots[0].Included = false;
        reopened.Slots[1].Slot = "7";
        reopened.ManifestName = "Prefix only";
        reopened.Publish.Execute(null);

        var saved = Library.Manifests(run.Id);
        Assert.Equal(2, saved.Length);
        var prefixOnly = saved.Single(m => m.Name == "Prefix only");
        Assert.Equal(run.Outputs[1].ArtifactId, Assert.Single(prefixOnly.Entries).ArtifactId);
        foreach (var (manifest, index, output) in new[] { (all, 0, 0), (all, 1, 1), (prefixOnly, 7, 1) })
        {
            var (tests, prefix) = Library.LoadTestsWithPrefix(new(run.Id, manifest.Id, index));
            Assert.Equal(OutputDataType.SortableTestsWithPrefix, run.Outputs[output].DataType);
            if (run.Outputs[output].Recipe is PrefixTests) Assert.NotEmpty(prefix);
            else Assert.Empty(prefix);
            Assert.Equal(8, SortableTests.getChannelCount(tests));
            Assert.Equal(run.Outputs[output].TestCount, SortableTests.getSortableCount(tests));
            Assert.Equal(run.Outputs[output].ArtifactId, SortableTests.getId(tests));
        }
        Assert.Equal(2, Directory.GetFiles(directory, "*.mpk", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task EvaluatorUsesSelectedTypedSlotsAndPersistsModelResultsAndPrefix()
    {
        var library = Library;
        var token = TestContext.Current.CancellationToken;
        var testRun = library.Execute("tests", [new PrefixTests(8, 3, "PrefixA")], false, token);
        var manifest = library.Publish(testRun.Id, "curated", [new(OutputDataType.SortableTestsWithPrefix, 7, testRun.Outputs[0].ArtifactId)]);
        var vm = new SortableTestsPageViewModel(() => { }, library) { ModelCount = "3", ModelStages = "4", ModelChannels = "8" };
        vm.Open();
        await vm.GenerateModelsAsync();
        var modelRun = Assert.Single(vm.Runs, r => r.Outputs[0].DataType == OutputDataType.SorterModelSet);
        vm.Slots[0].Slot = "5";
        vm.ManifestName = "selected models";
        vm.Publish.Execute(null);
        vm.ModelsInput = vm.ModelChoices.Single(c => c.Slot.Index == 5);
        await vm.EvaluateModelsAsync();

        var completed = library.CompletedRuns().Single(r => r.Outputs[0].DataType == OutputDataType.SorterSetEval);
        var output = Assert.Single(completed.Outputs);
        var evaluation = Assert.IsType<EvalRecipe>(output.Recipe);
        Assert.Equal(new ManifestSlot(testRun.Id, manifest.Id, 7), evaluation.Tests);
        Assert.Equal(modelRun.Id, evaluation.Models.RunId);
        Assert.Equal(5, evaluation.Models.Index);
        var result = library.LoadEvaluation(new(completed.Id, Assert.Single(library.Manifests(completed.Id)).Id, 0));
        Assert.Equal(testRun.Outputs[0].ArtifactId, result.SorterTestId);
        var models = library.LoadModels(evaluation.Models);
        Assert.Equal(models.Id, result.SorterSetId);
        Assert.Equal(models.SorterModels.Select(Sortopia.Models.Sorting.SorterModel.getId).Order(),
            result.SorterEvals.Select(Sortopia.Evaluation.SorterEval.getSorterId).Order());
        var prefix = library.LoadTestsWithPrefix(evaluation.Tests).Prefix;
        Assert.NotEmpty(prefix);
        Assert.All(result.SorterEvals, item => Assert.Equal(prefix,
            Sortopia.Evaluation.SorterEval.getCeUseArray(item).Where(use => use.UseCount == -1).Select(use => use.Ce)));
        Assert.Equal(3, output.Evaluation!.ModelCount);
        Assert.Equal(result.SorterEvals.Min(Sortopia.Evaluation.SorterEval.getUnsortedCount), output.Evaluation.FewestFailures);
        Assert.Equal(testRun.Outputs[0].TestCount, output.TestCount);

        var wrongWidth = library.Execute("other width", [new MergeTests(4, 2)], true, token);
        var wrongSlot = new ManifestSlot(wrongWidth.Id, library.Manifests(wrongWidth.Id).Single().Id, 0);
        Assert.Throws<ArgumentException>(() => library.Execute("incompatible", [evaluation with { Tests = wrongSlot }], true, token));
        Assert.Throws<KeyNotFoundException>(() => library.Execute("wrong type", [evaluation with { Models = wrongSlot }], true, token));
        Assert.DoesNotContain(library.CompletedRuns(), r => r.Name == "incompatible" || r.Name == "wrong type");
    }

    [Fact]
    public async Task DirectRunCanReopenBetweenPhasesAndExecutesTheSavedParameterValues()
    {
        var editor = new SortableTestsPageViewModel(() => { }, Library) { Channels = "4", Ways = "2" };
        editor.Add.Execute(null);
        editor.SaveDefinition.Execute(null);
        var definition = Assert.Single(editor.Definitions);
        var runFolder = Path.Combine(directory, definition.Id.ToString("N"));
        Assert.True(File.Exists(Path.Combine(runFolder, "run.json")));
        Assert.False(Directory.Exists(Path.Combine(runFolder, "parameters")));

        var reopened = new SortableTestsPageViewModel(() => { }, Library);
        reopened.Open();
        Assert.Equal(definition.Id, reopened.SelectedDefinition!.Id);
        Assert.False(reopened.ExecuteParameters.CanExecute(null));
        Assert.Throws<InvalidOperationException>(() => Library.ExecuteSortableTests(definition.Id,
            cancellation: TestContext.Current.CancellationToken));
        await reopened.GenerateParametersAsync();
        Assert.True(reopened.ExecuteParameters.CanExecute(null));
        Assert.Empty(Library.CompletedRuns());
        Assert.False(Directory.Exists(Path.Combine(runFolder, "results")));

        var file = Assert.Single(Directory.GetFiles(Path.Combine(runFolder, "parameters"), "*.json"));
        var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!;
        fields["channels"] = "6";
        File.WriteAllText(file, JsonSerializer.Serialize(fields));
        var executor = new SortableTestsPageViewModel(() => { }, Library);
        executor.Open();
        await executor.ExecuteParametersAsync();
        var run = Assert.Single(executor.Runs);
        var output = Assert.Single(run.Outputs);
        Assert.Equal(Assert.Single(definition.Datasets).ParameterId, output.ParameterId);
        var directStore = Storage.ProjectFileStore.FromRunsFolder(directory);
        var result = Assert.Single(directStore.ListResults(run.Id));
        Assert.Equal(output.ParameterId, result.Parameters.ParameterId);
        Assert.Equal(output.ArtifactId, result.DataId);
        var data = Assert.IsType<Storage.ResultData.SortableTestsWithPrefix>(directStore.LoadResult(result));
        Assert.Equal(6, SortableTests.getChannelCount(data.Item1));
        Assert.Equal(4, SortableTests.getSortableCount(data.Item1));
        Assert.Empty(data.Item2);
    }

    private sealed class CallbackProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string message) => report(message);
    }

    [Fact]
    public void InterruptedDirectRunResumesWithTheSameParametersResultsAndManifest()
    {
        var token = TestContext.Current.CancellationToken;
        var definition = Library.SaveSortableTestsDefinition("resume", [new MergeTests(4, 2), new PrefixTests(8, 3, "PrefixA")]);
        Library.GenerateSortableTestsParameters(definition.Id, token);
        var store = Storage.ProjectFileStore.FromRunsFolder(directory);
        var rows = store.ListParameters(definition.Id);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var progress = new CallbackProgress(message => { if (message.StartsWith("Dataset 2 ")) stop.Cancel(); });
        Assert.Throws<OperationCanceledException>(() => Library.ExecuteSortableTests(definition.Id, true, stop.Token, progress));
        var first = Assert.Single(store.ListResults(definition.Id));
        Assert.Empty(Library.CompletedRuns());
        Assert.Empty(Library.Manifests(definition.Id));
        Library.GenerateSortableTestsParameters(definition.Id, token);
        Assert.Equal(rows, store.ListParameters(definition.Id));
        var completed = Library.ExecuteSortableTests(definition.Id, true, token);
        var retry = Library.ExecuteSortableTests(definition.Id, true, token);
        Assert.Equal(completed.CompletedAt, retry.CompletedAt);
        var results = store.ListResults(definition.Id);
        Assert.Equal(2, results.Length);
        Assert.Contains(first, results);
        Assert.Single(Library.Manifests(definition.Id));
    }

    [Fact]
    public void RejectedSlotsDoNotPublishAndPublishedVersionsRemainAvailable()
    {
        var library = Library;
        var run = library.Execute("merge", [new MergeTests(4, 2)], false, TestContext.Current.CancellationToken);
        Assert.Empty(library.Manifests(run.Id));
        var entry = new ManifestEntry(OutputDataType.SortableTestsWithPrefix, 2, run.Outputs[0].ArtifactId);
        foreach (var invalid in new ManifestEntry[][] {
            [], [entry, entry], [entry with { Index = -1 }], [entry with { ArtifactId = Guid.NewGuid() }] })
            Assert.Throws<ArgumentException>(() => library.Publish(run.Id, "test", invalid));
        Assert.Empty(library.Manifests(run.Id));
        var first = library.Publish(run.Id, "test", [entry]);
        var next = library.Publish(run.Id, "test", [entry with { Index = 5 }]);
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal(SortableTests.getId(library.LoadTests(run.Id, first.Id, 2)),
            SortableTests.getId(library.LoadTests(run.Id, next.Id, 5)));
    }

    [Fact]
    public void ParameterFilesCanExistBeforeACompletedRunIsPublished()
    {
        Directory.CreateDirectory(Path.Combine(directory, Guid.NewGuid().ToString("N"), "parameters"));
        var completed = Library.Execute("merge", [new MergeTests(4, 2)], false, TestContext.Current.CancellationToken);
        Assert.Equal(completed.Id, Assert.Single(Library.CompletedRuns()).Id);
    }

    [Fact]
    public void AFailedOrCancelledRunNeverAppearsAsCompleted()
    {
        var library = Library;
        Assert.Throws<ArgumentException>(() =>
            library.Execute("invalid batch", [new MergeTests(4, 2), new MergeTests(5, 2)], true, TestContext.Current.CancellationToken));
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => library.Execute("cancelled", [new MergeTests(4, 2)], true, stop.Token));
        Assert.Empty(library.CompletedRuns());
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
