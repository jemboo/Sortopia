using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

// WPF's shared package-resource cache must not be loaded by parallel UI threads.
[CollectionDefinition("Workspace editor rendering", DisableParallelization = true)]
public sealed class WorkspaceEditorRenderingCollection;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceRequiredInputsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Coverage_distinguishes_defaults_missing_empty_valid_and_conflicting_spans_in_each_phase(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        var editor = run.Editor!;
        WorkspaceRequiredInputs Coverage() => setup ? editor.SetupRequiredInputs : editor.ResultsRequiredInputs;
        Assert.Equal(2, Coverage().DefaultCount); Assert.Equal(0, Coverage().NeedsSpanCount);
        Assert.Empty(setup ? editor.SetupParameters : editor.ResultsParameters);
        (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults).Single(row => row.Key == "ChannelCount").ExcludeCommand!.Execute(null);
        Assert.Equal(1, Coverage().DefaultCount); Assert.Equal(1, Coverage().NeedsSpanCount);
        var fixedValue = Coverage().Rows.Single(row => row.Key == "SortableDataFormat");
        Assert.Equal("Supplied by default", fixedValue.Status); Assert.Equal("Bools", fixedValue.Detail);
        Assert.Same(module.Editor, fixedValue.Source!.InheritedSource);
        Assert.Equal("Needs span", Coverage().Rows.Single(row => row.Key == "ChannelCount").Status);
        (setup ? editor.AddMissingSetupParametersCommand : editor.AddMissingResultsParametersCommand).Execute(null);
        var span = (setup ? editor.SetupParameters : editor.ResultsParameters).Single();
        Assert.Empty(span.Values); Assert.Equal(1, Coverage().NeedsSpanCount);
        span.AddValueCommand.Execute(null); span.Values[0].Text = "8";
        Assert.Equal(1, Coverage().DefaultCount); Assert.Equal(1, Coverage().SpanCount); Assert.Equal(0, Coverage().NeedsSpanCount);
        Assert.All(Coverage().Rows, row => Assert.False(row.HasIssue));
        Assert.Equal(2, (setup ? editor.ResultsRequiredInputs : editor.SetupRequiredInputs).DefaultCount);
        span.Values[0].Text = "invalid";
        Assert.Equal(0, Coverage().SpanCount); Assert.Equal(1, Coverage().NeedsSpanCount);
        span.Values[0].Text = "8";
        (setup ? project.Editor.ExcludedSetupDefaults : project.Editor.ExcludedResultsDefaults).Single().RestoreCommand.Execute(null);
        Assert.Equal("Default/span conflict", Coverage().Rows.Single(row => row.Key == "ChannelCount").Status);
        Assert.Equal(1, Coverage().OtherIssueCount);
        span.RemoveCommand.Execute(null);
        Assert.Equal(2, Coverage().DefaultCount);
    }

    private static WorkspaceTreeNode PrefixRun()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Prefix");
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        module.Editor.AddResultsParameterDefaultCommand.Execute(null);
        var format = module.Editor.ResultsParameterDefaults.Single(); format.Key = "SortableDataFormat"; format.Value.Text = "Bools";
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Prefix tests"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "cc"); project.Children.Add(run);
        var editor = run.Editor!; editor.AddMissingResultsParametersCommand.Execute(null);
        var span = editor.ResultsParameters.Single();
        foreach (var channels in new[] { 16, 18 }) {
            span.AddValueCommand.Execute(null);
            span.Values.Last().Text = $"PrefixLibId(channelCount={channels}, stageLength=1, Variant=Prefix1)";
        }
        return run;
    }

    [Fact]
    public void Setup_outputs_select_independent_subsets_without_changing_results_or_defaults()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Evaluation", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(FunctionOutputContractTests.Combined(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", true, "4", "8");
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "Repl", true, "0", "1");
        var editor = run.Editor!;
        var beforeDefaults = editor.ToProjectSettings().setupParameterDefaults;
        foreach (var output in editor.SetupQueryOutputs.ToArray()) {
            Assert.Equal(new[] { "ChannelCount", "Repl" }, output.Keys.Select(key => key.Key));
            output.Keys.Single(key => key.Key == (output.Kind == "SortableTests" ? "ChannelCount" : "Repl")).Selected = true;
        }
        var settings = editor.ToProjectSettings();
        Assert.Equal(new[] { "ChannelCount" }, settings.setupDataTypes.Single(output => output.dataType.kind == "SortableTests").queryParameterKeys);
        Assert.Equal(new[] { "Repl" }, settings.setupDataTypes.Single(output => output.dataType.kind == "SorterModelSet").queryParameterKeys);
        Assert.Equal(beforeDefaults.Select(value => (value.keyName, value.value)), settings.setupParameterDefaults.Select(value => (value.keyName, value.value)));
        Assert.All(settings.resultsDataTypes, output => Assert.Empty(output.queryParameterKeys));
        Assert.Empty(editor.ResultsParameters);
    }

    [Fact]
    public void Prefix_only_query_projection_distinguishes_outputs_without_including_the_default_format()
    {
        var run = PrefixRun(); var editor = run.Editor!;
        Assert.Equal(1, editor.ResultsRequiredInputs.DefaultCount); Assert.Equal(1, editor.ResultsRequiredInputs.SpanCount);
        var output = editor.ResultsQueryOutputs.Single();
        Assert.True(output.HasEmptyProjection); Assert.Contains("all parameter sets share one output", output.Summary);
        Assert.Contains("No query keys selected for SortableTests", editor.ResultsProjectionNotice);
        var key = Assert.Single(output.Keys); Assert.Equal("PrefixLibId", key.Key);
        key.Selected = true;
        output = editor.ResultsQueryOutputs.Single();
        Assert.Empty(editor.ResultsProjectionNotice);
        Assert.False(output.HasEmptyProjection); Assert.Equal("Query parameters: PrefixLibId", output.SelectedKeysSummary);
        var settings = editor.ToProjectSettings();
        Assert.Equal(new[] { "PrefixLibId" }, settings.resultsDataTypes.Single().queryParameterKeys);
        Assert.Equal("SortableDataFormat", Assert.Single(settings.resultsParameterDefaults).keyName);
        var parent = project.create(run.Parent!.Id, "Prefix tests", "PrefixTests", SettingsDto.toDomain(hierarchyKind.Root, settings), []);
        var value = RunDto.toDomain(parent, editor.ToRunInputs(run.Id, "cc", run.Parent.Id));
        var sets = RunParameterSets.create(value).Results;
        Assert.Equal(2, sets.Length);
        Guid Id(parameterSet parameters, string[] keys) => DataKey.create(value.ParentProjectId, value.ResultsDatabaseName, false,
            value.ResultsFunctionName, "1", value.ResultsSortopiaDataTypes.Head, ListModule.OfArray(keys), parameters, []).Id;
        Assert.Equal(Id(sets[0], []), Id(sets[1], []));
        Assert.NotEqual(Id(sets[0], ["PrefixLibId"]), Id(sets[1], ["PrefixLibId"]));
    }

    [Fact]
    public void Run_span_editor_renders_coverage_and_per_output_query_choices()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var run = PrefixRun(); run.Editor!.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
                var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = run.Editor, ActiveItemKey = "resultsSpans" };
                var border = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 31)), Padding = new System.Windows.Thickness(20), Child = view };
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                border.Measure(new System.Windows.Size(600, double.PositiveInfinity));
                border.Arrange(new System.Windows.Rect(0, 0, 600, border.DesiredSize.Height)); border.UpdateLayout();
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.Controls.ContentControl)view.FindName("ResultsRequiredInputsSummary")).Visibility);
                var queries = (System.Windows.Controls.ItemsControl)view.FindName("ResultsSpanQueryOutputsList");
                Assert.Single(queries.Items);
                if (Environment.GetEnvironmentVariable("SORTOPIA_COVERAGE_SCREENSHOT") is { Length: > 0 } path) {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(600, (int)Math.Ceiling(border.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(border);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(path); encoder.Save(file);
                }
                Assert.True(queries.ActualHeight > 0, "The per-output query choices must be laid out in the span editor.");
            } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
