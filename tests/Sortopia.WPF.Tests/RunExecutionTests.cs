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
        settings.resultsFunctionName = ResultsFunctionNames.sortableTests;
        settings.setupFunctionName = branch ? SetupFunctionNames.standardTests : "";
        settings.resultsQueryParameterKeys = [];
        settings.resultsSortopiaDataTypes = [new() { kind = "SortableTests", value = "standard" }];
        settings.resultsParameterDefaults = [Binding("SortableDataFormat", "Bools"), Binding("MergeLibId",
            MergeLibId.toString(MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2)), parameterValueType.MergeLibId)];
        settings.setupParameterDefaults = branch ? [Binding("ChannelCount", "4", parameterValueType.Integer), Binding("SortableDataFormat", "Bools")] : [];
        return settings;
    }
    private static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Text) =>
        new() { keyName = key, value = value, valueType = (int)type };
    internal static run MakeRun(bool branch, projectSettingsDto? settings = null, parameterSpanDto[]? results = null, parameterSpanDto[]? setup = null)
    {
        var id = Guid.NewGuid();
        var parent = project.create(id, "Project", "Project", SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings ?? Settings(branch)), []);
        return RunDto.toDomain(parent, new runDto
        {
            schemaVersion = 11, id = Guid.NewGuid(), parentProjectId = id, runKind = branch ? "branch" : "root", runName = "Execute",
            resultsParameterSpans = results ?? [], setupParameterSpans = setup ?? []
        });
    }
    private sealed class Progress(Action<runExecutionProgress> action) : IProgress<runExecutionProgress>
    { public void Report(runExecutionProgress value) => action(value); }
    private static runExecutionReport Execute(run value, IProgress<runExecutionProgress>? progress = null, CancellationToken token = default,
        params functionData[] inputs) => RunExecution.execute(value, ListModule.OfArray(inputs), progress!, token);

    [Fact]
    public void Root_executes_every_set_beyond_the_preview_limit_and_keeps_typed_parameters()
    {
        var value = MakeRun(false, results: [new() { keyName = "Repl", values = Enumerable.Range(0, 51).Select(i => i.ToString()).ToArray() }]);
        var report = Execute(value, token: TestContext.Current.CancellationToken);
        Assert.Equal(runExecutionStatus.Completed, report.Status);
        Assert.Empty(report.Setup);
        Assert.Equal(51, report.Results.Length);
        Assert.Equal(Enumerable.Range(0, 51), report.Results.Select(call => call.Parameters.TryFind(ParamKeys.replKey).Value));
        Assert.All(report.Results, call => Assert.IsType<functionData.SortableTests>(Assert.Single(call.Outputs)));
    }

    [Fact]
    public void Branch_finishes_all_setup_before_the_full_product_and_passes_the_matching_batch()
    {
        var settings = Settings(true);
        settings.resultsFunctionName = ResultsFunctionNames.sorterEval;
        settings.resultsParameterDefaults = [Binding("ChannelCount", "4", parameterValueType.Integer), Binding("SorterEvalType", "V1"), Binding("CollectSortableTests", "false", parameterValueType.Boolean)];
        var value = MakeRun(true, settings, [new() { keyName = "Repl", values = ["0", "1", "2"] }],
            [new() { keyName = "Repl", values = ["0", "1"] }]);
        var modelParameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.stageLengthKey, 3),
            ParameterBinding.create(ParamKeys.simpleSorterModelTypeKey, "Mssi"), ParameterBinding.create(ParamKeys.rngTypeKey, "SplitMix64"),
            ParameterBinding.create(ParamKeys.excludeSelfCeKey, true), ParameterBinding.create(ParamKeys.sorterCountKey, 2), ParameterBinding.create(ParamKeys.replKey, 0)]);
        var models = FunctionCatalogs.setupFunctionCatalog[SetupFunctionNames.uniformSorterModels].Invoke(SetupDataContext.transient).Invoke(modelParameters).Invoke(ListModule.Empty<functionData>()).ResultValue;
        var updates = new List<runExecutionProgress>();
        var report = Execute(value, new Progress(updates.Add), TestContext.Current.CancellationToken, models.ToArray());
        Assert.Equal(runExecutionStatus.Completed, report.Status);
        Assert.Equal(2, report.Setup.Length);
        Assert.Equal(6, report.Results.Length);
        Assert.Equal(new[] { 1, 1, 1, 2, 2, 2 }, report.Results.Select(call => call.SetupSetNumber.Value));
        Assert.All(updates, update => Assert.Equal(8, update.Total));
        Assert.Equal(2, updates.First(update => update.Message.StartsWith("Results")).Completed);
        foreach (var result in report.Results)
        {
            var tests = Assert.IsType<functionData.SortableTests>(Assert.Single(report.Setup[result.SetupSetNumber.Value - 1].Outputs));
            var evaluation = Assert.IsType<functionData.SorterSetEval>(Assert.Single(result.Outputs));
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
        var settings = Settings(true);
        if (setup) settings.setupFunctionName = "unregistered"; else settings.resultsFunctionName = "unregistered";
        var report = Execute(MakeRun(true, settings), token: TestContext.Current.CancellationToken);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Empty(report.Setup); Assert.Empty(report.Results);
        Assert.Contains("unregistered", report.Error);
    }

    [Fact]
    public void Cancellation_between_setup_calls_prevents_results_and_preserves_outputs()
    {
        using var cancellation = new CancellationTokenSource();
        var report = Execute(MakeRun(true, setup: [new() { keyName = "Repl", values = ["0", "1"] }]),
            new Progress(update => { if (update.Completed == 1) cancellation.Cancel(); }), cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, report.Status);
        Assert.Single(report.Setup); Assert.Empty(report.Results); Assert.Empty(report.Error);
        Assert.Empty(Execute(MakeRun(false), token: cancellation.Token).Results);
    }
}
