using Sortopia.Evaluation;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class EvaluationChannelCountTests
{
    private static parameterSet Parameters(string evaluator, bool collect) => parameterSet.fromBindings([
        ParameterBinding.create(ParamKeys.sorterEvalTypeKey, evaluator),
        ParameterBinding.create(ParamKeys.collectSortableTestsKey, collect)]);

    private static sortopiaStorageData Tests(int channels) => Assert.Single(CatalogTestHost.Invoke(false,
        ResultsFunctionNames.standardSortableTests, parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.channelCountKey, channels),
            ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")])));

    private static sortopiaStorageData.SorterModelSet Models(int channels) => Assert.IsType<sortopiaStorageData.SorterModelSet>(
        Assert.Single(CatalogTestHost.Invoke(false, ResultsFunctionNames.uniformMssiSimpleSorterModels,
            parameterSet.fromBindings(RunExecutionTests.UniformModelDefaults(channels).Select(ParameterBindingDto.toDomain)))));

    [Theory]
    [InlineData(4, "V1", false)]
    [InlineData(8, "V2", true)]
    [InlineData(4, "V3", true)]
    public void Evaluation_derives_the_channel_count_from_the_tests(int channels, string evaluator, bool collect)
    {
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.sorterEval);
        Assert.Equal("6", definition.Version);
        Assert.Equal(new[] { "SorterEvalType", "CollectSortableTests" }, definition.RequiredParameters.Select(key => key.Name));
        Assert.DoesNotContain("ChannelCount", definition.DataTypes.Values.SelectMany(keys => keys));
        var models = Models(channels);
        var result = Assert.IsType<sortopiaStorageData.SorterSetEval>(Assert.Single(CatalogTestHost.Invoke(false,
            ResultsFunctionNames.sorterEval, Parameters(evaluator, collect), models, Tests(channels))));
        Assert.Equal(models.Item.Count, result.Item.SorterEvals.Length);
        Assert.All(result.Item.SorterEvals, evaluation => Assert.Equal(channels, SorterEval.getChannelCount(evaluation)));
    }

    [Theory]
    [InlineData("V1")]
    [InlineData("V2")]
    [InlineData("V3")]
    public void Evaluation_checks_every_model_even_when_the_first_matches_the_tests(string evaluator)
    {
        var four = Models(4).Item; var eight = Models(8).Item;
        var mixed = sorterModelSet.create(Guid.NewGuid(), [.. four.SorterModels, .. eight.SorterModels], Math.Max(four.RawCeLength, eight.RawCeLength));
        var channels = SorterModel.getChannelCount(mixed.SorterModels[0]);
        Assert.Contains(mixed.SorterModels.Skip(1), model => SorterModel.getChannelCount(model) != channels);
        var error = Assert.Throws<ArgumentException>(() => CatalogTestHost.Invoke(false,
            ResultsFunctionNames.sorterEval, Parameters(evaluator, false), sortopiaStorageData.NewSorterModelSet(mixed), Tests(channels)));
        Assert.Contains("models and tests must have the same channel count", error.Message);
    }

    [Fact]
    public void Draft_run_drops_the_obsolete_empty_channel_count_span_from_its_active_inputs()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        var settings = FunctionOutputContractTests.Combined(true);
        settings.resultsParameterDefaults = [];
        module.Editor!.LoadProject(settings);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Full", family: hierarchyKind.Branch);
        project.Children.Add(run);
        run.Editor!.LoadRun(new runDto {
            schemaVersion = WorkspaceDtoSchema.version, runKind = "branch", resultsParameterSpans = [
                new() { keyName = "ChannelCount", values = [] },
                new() { keyName = "SorterEvalType", values = ["V2"] },
                new() { keyName = "CollectSortableTests", values = ["False"] }], setupParameterSpans = []
        });
        Assert.DoesNotContain(run.Editor.ActiveResultsParameters, span => span.Key == "ChannelCount");
        Assert.Empty(run.Editor.ResultsParametersError);
        Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows);
        Assert.Equal(new[] { "SorterEvalType", "CollectSortableTests" },
            run.Editor.ToRunInputs(run.Id, run.Name, project.Id).resultsParameterSpans.Select(span => span.keyName));
    }
}
