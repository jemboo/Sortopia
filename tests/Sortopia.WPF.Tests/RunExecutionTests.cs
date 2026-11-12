using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class RunExecutionTests
{
    internal static projectSettingsDto Settings(bool branch)
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        settings.setupFunctionName = branch ? SetupFunctionNames.standardTests : "";
        settings.resultsQueryParameterKeys = [];
        settings.resultsSortopiaDataTypes = [new() { kind = "SortableTests", value = "" }];
        settings.resultsParameterDefaults = [Binding("SortableDataFormat", "Bools"), Binding("MergeLibId",
            MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)), parameterValueType.MergeLibId)];
        settings.setupParameterDefaults = branch ? [Binding("ChannelCount", "4", parameterValueType.Integer), Binding("SortableDataFormat", "Bools")] : [];
        return settings;
    }
    internal static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Text) =>
        new() { keyName = key, value = value, valueType = (int)type };
    internal static parameterBindingDto[] ModelDefaults(int channels = 4) => [
        Binding("ChannelCount", channels.ToString(), parameterValueType.Integer), Binding("StageLength", "3", parameterValueType.Integer),
        Binding("SimpleSorterModelType", "Mssi"), Binding("RngType", "SplitMix64"),
        Binding("ExcludeSelfCe", "true", parameterValueType.Boolean), Binding("SorterCount", "3", parameterValueType.Integer),
        Binding("Repl", "0", parameterValueType.Integer)];
    // Successful execution fixtures explicitly supply fixed query inputs as one-value spans.
    // Production run construction never fills these in implicitly.
    internal static parameterSpanDto[] QuerySpans(IEnumerable<string> keys, IEnumerable<parameterBinding> defaults, parameterSpanDto[]? supplied = null)
    {
        var spans = (supplied ?? []).ToList();
        foreach (var key in keys.Distinct().Where(key => !spans.Any(span => span.keyName == key)))
            if (defaults.FirstOrDefault(binding => binding.KeyName == key) is { } value)
                spans.Add(new() { keyName = key, values = [value.FormattedValue] });
        return spans.ToArray();
    }
    internal static run MakeRun(bool branch, projectSettingsDto? settings = null, parameterSpanDto[]? results = null, parameterSpanDto[]? setup = null)
    {
        var id = Guid.NewGuid();
        settings = SettingsDto.fromDomain(SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings ?? Settings(branch)));
        var definition = FunctionCatalogs.requireResults(settings.resultsFunctionName);
        settings.resultsDataTypes = definition.DataTypes.Select(entry => new resultsDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.ToArray() }).ToArray();
        results = QuerySpans(settings.resultsDataTypes.SelectMany(output => output.queryParameterKeys), settings.resultsParameterDefaults.Select(ParameterBindingDto.toDomain), results);
        settings.resultsParameterDefaults = settings.resultsParameterDefaults.Where(binding => !results.Any(span => span.keyName == binding.keyName)).ToArray();
        if (branch) {
            var setupDefinition = FunctionCatalogs.requireSetup(settings.setupFunctionName);
            settings.setupDataTypes = setupDefinition.DataTypes.Select(entry => new setupDataTypeConfigDto { dataType = SortopiaDataTypeDto.fromDomain(entry.Key), queryParameterKeys = entry.Value.ToArray(), sourceProjectId = settings.setupDataTypes.FirstOrDefault(output => output.dataType.kind == SortopiaDataType.kindName(entry.Key))?.sourceProjectId ?? Guid.Empty }).ToArray();
            setup = QuerySpans(settings.setupDataTypes.SelectMany(output => output.queryParameterKeys), settings.setupParameterDefaults.Select(ParameterBindingDto.toDomain), setup);
            settings.setupParameterDefaults = settings.setupParameterDefaults.Where(binding => !setup.Any(span => span.keyName == binding.keyName)).ToArray();
        }
        var parent = project.create(id, "Project", "Project", SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
        return RunDto.toDomain(parent, new runDto
        {
            schemaVersion = 14, id = Guid.NewGuid(), parentProjectId = id, runKind = branch ? "branch" : "root", runName = "Execute",
            resultsParameterSpans = QuerySpans(parent.ResultsQueryParameterKeys, parent.ResultsParameterDefaults, results),
            setupParameterSpans = parent is project.BranchProject b ? QuerySpans(b.Item.SetupDataTypes.SelectMany(output => output.QueryParameterKeys), b.Item.SetupParameterDefaults, setup) : []
        });
    }
    internal static project MakeProject(string name, bool branch, projectSettingsDto settings, parameterSpanDto[]? results = null, parameterSpanDto[]? setup = null, bool seal = true)
    {
        var value = MakeRun(branch, settings, results, setup);
        var config = value is run.RootRun root ? projectConfiguration.NewRootProjectConfig(root.Item.ProjectConfiguration) : projectConfiguration.NewBranchProjectConfig(((run.BranchRun)value).Item.ProjectConfiguration);
        var parent = project.create(value.ParentProjectId, name, name, config, []);
        return seal ? project.withRuns(parent, [RunDto.toDomain(parent, RunDto.fromDomain(value))]) : parent;
    }
    private sealed class Progress(Action<runExecutionProgress> action) : IProgress<runExecutionProgress>
    { public void Report(runExecutionProgress value) => action(value); }
    private static runExecutionReport Execute(run value, IProgress<runExecutionProgress>? progress = null, CancellationToken token = default) =>
        RunExecution.execute(new CatalogTestHost(value, token), progress!);

    [Fact]
    public void Root_executes_every_set_beyond_the_preview_limit_and_keeps_typed_parameters()
    {
        var value = MakeRun(false, results: [new() { keyName = "SortableDataFormat", values = Enumerable.Repeat("Bools", 51).ToArray() }]);
        var report = Execute(value, token: TestContext.Current.CancellationToken);
        Assert.Equal(runExecutionStatus.Completed, report.Status);
        Assert.Empty(report.Setup);
        Assert.Equal(51, report.Results.Length);
        Assert.All(report.Results, call => Assert.NotNull(call.Parameters.TryFind(ParamKeys.mergeLibIdKey)));
        Assert.All(report.Results, call => Assert.True(Assert.Single(call.StoredOutputs).Reference.DataType.IsSortableTests));
    }

    [Fact]
    public void Branch_finishes_all_setup_before_the_full_product_and_passes_the_matching_batch()
    {
        var settings = Settings(true);
        settings.setupFunctionName = SetupFunctionNames.evaluationStandard;
        settings.setupParameterDefaults = [.. ModelDefaults(), Binding("SortableDataFormat", "Bools")];
        settings.resultsFunctionName = ResultsFunctionNames.sorterEval;
        settings.resultsParameterDefaults = [Binding("ChannelCount", "4", parameterValueType.Integer), Binding("SorterEvalType", "V1"), Binding("CollectSortableTests", "false", parameterValueType.Boolean)];
        var value = MakeRun(true, settings, [new() { keyName = "ChannelCount", values = ["4", "4", "4"] }],
            [new() { keyName = "SortableDataFormat", values = ["Bools", "Ints"] }]);
        var updates = new List<runExecutionProgress>();
        var host = new CatalogTestHost(value, TestContext.Current.CancellationToken);
        var report = RunExecution.execute(host, new Progress(updates.Add));
        Assert.Equal(runExecutionStatus.Completed, report.Status);
        Assert.Equal(2, report.Setup.Length);
        Assert.Equal(2, report.Setup.SelectMany(call => call.StoredOutputs).Where(output => output.Reference.DataType.IsSortableTests)
            .Select(output => output.Reference).Distinct().Count());
        Assert.Equal(6, report.Results.Length);
        Assert.Equal(new[] { 1, 1, 1, 2, 2, 2 }, report.Results.Select(call => call.SetupSetNumber.Value));
        Assert.All(updates, update => Assert.Equal(8, update.Total));
        Assert.Equal(2, updates.First(update => update.Message.StartsWith("Results")).Completed);
        foreach (var result in report.Results)
        {
            var tests = Assert.IsType<functionData.SortableTests>(host.Load(report.Setup[result.SetupSetNumber.Value - 1].StoredOutputs.Single(output => output.Reference.DataType.IsSortableTests).Reference));
            var evaluation = Assert.IsType<functionData.SorterSetEval>(host.Load(Assert.Single(result.StoredOutputs).Reference));
            Assert.Equal(SortableTests.getId(tests.Item1), evaluation.Item.SorterTestId);
        }
    }

    [Fact]
    public void A_setup_failure_retains_completed_setup_and_prevents_all_results()
    {
        var report = Execute(MakeRun(true, setup: [new() { keyName = "SortableDataFormat", values = ["Bools", "invalid"] }]), token: TestContext.Current.CancellationToken);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Single(report.Setup);
        Assert.Empty(report.Results);
        Assert.Contains("Setup set 2", report.Error);
        Assert.Contains(SetupFunctionNames.standardTests, report.Error);
    }

    [Fact]
    public void A_results_failure_retains_completed_calls_and_identifies_its_set()
    {
        var report = Execute(MakeRun(false, results: [new() { keyName = "SortableDataFormat", values = ["Bools", "invalid", "Bools"] }]), token: TestContext.Current.CancellationToken);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Single(report.Results);
        Assert.Contains("Results set 2", report.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unknown_function_names_are_rejected_before_any_calls(bool setup)
    {
        var settings = Settings(true); if (setup) settings.setupFunctionName = "unregistered"; else settings.resultsFunctionName = "unregistered";
        Assert.Contains("Unknown", Assert.Throws<ArgumentException>(() => MakeRun(true, settings)).Message);
    }

    [Fact]
    public void Cancellation_between_setup_calls_prevents_results_and_preserves_outputs()
    {
        using var cancellation = new CancellationTokenSource();
        var report = Execute(MakeRun(true, setup: [new() { keyName = "SortableDataFormat", values = ["Bools", "Bools"] }]),
            new Progress(update => { if (update.Completed == 1) cancellation.Cancel(); }), cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, report.Status);
        Assert.Single(report.Setup); Assert.Empty(report.Results); Assert.Empty(report.Error);
        Assert.Empty(Execute(MakeRun(false), token: cancellation.Token).Results);
    }
}
