using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class DisjointParameterInputsTests
{
    [Fact]
    public void Shared_validation_and_generation_reject_default_span_overlap()
    {
        var defaults = ListModule.OfArray(new[] { ParameterBinding.create(ParamKeys.channelCountKey, 4) });
        var spans = ListModule.OfArray(new[] { ParameterSpan.create(ParamKeys.channelCountKey, ListModule.OfArray(new[] { 8 })) });
        Assert.Contains("both a default and a span", Assert.Throws<ArgumentException>(() => ParameterSets.create(defaults, spans)).Message);
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => ParameterSets.preview(10, defaults, spans)).Message);
        Assert.Contains("both a default and a span", ParameterSets.inputKeysError(ListModule.OfArray(new[] { "ChannelCount" }), ListModule.Empty<string>(), ListModule.OfArray(new[] { "ChannelCount" }), ListModule.OfArray(new[] { "ChannelCount" })));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Defaults_and_spans_partition_each_phase_and_query_choices_use_explicit_spans(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        var editor = run.Editor!;
        var defaults = setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults;
        Assert.Single((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        Assert.Empty((setup ? editor.SetupQueryOutputs : editor.ResultsQueryOutputs).Single().Keys);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", setup, "8");
        Assert.DoesNotContain(setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults, value => value.Key == "ChannelCount");
        var span = Assert.Single(setup ? editor.SetupParameters : editor.ResultsParameters);
        Assert.Equal("ChannelCount", span.Key); Assert.Equal("8", Assert.Single(span.Values).Text);
        Assert.DoesNotContain("SortableDataFormat", span.KeyChoices);
        var output = (setup ? editor.SetupQueryOutputs : editor.ResultsQueryOutputs).Single();
        var query = Assert.Single(output.Keys); Assert.Equal("ChannelCount", query.Key); Assert.True(query.CanChange);
        query.Selected = true;
        var settings = project.Editor!.ToProjectSettings();
        Assert.Equal(new[] { "ChannelCount" }, setup ? settings.setupDataTypes.Single().queryParameterKeys : settings.resultsDataTypes.Single().queryParameterKeys);
        Assert.Equal(new[] { "SortableDataFormat" }, (setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(value => value.keyName));
        var parent = Sortopia.Workspaces.project.create(project.Id, "Project", "Project", SettingsDto.toDomain(hierarchyKind.Branch, settings), []);
        var domain = RunDto.toDomain(parent, editor.ToRunInputs(run.Id, "Run", project.Id));
        var sets = RunParameterSets.create(domain);
        Assert.Single(setup ? sets.Setup.Value : sets.Results);
        var local = project.Editor.ToLocalOverrides();
        var restored = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Restored", family: hierarchyKind.Branch); module.Children.Add(restored);
        restored.Editor!.LoadOverrides(OverridesDto.fromDomain(OverridesDto.toDomain(hierarchyKind.Branch, local)));
        Assert.Equal(new[] { "SortableDataFormat" }, (setup ? restored.Editor.ToProjectSettings().setupParameterDefaults : restored.Editor.ToProjectSettings().resultsParameterDefaults).Select(value => value.keyName));
        span.RemoveCommand.Execute(null);
        Assert.Contains("explicit", setup ? editor.SetupParametersError : editor.ResultsParametersError);
        Assert.Empty((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
    }
    [Fact]
    public void Outputs_have_independent_query_subsets_and_empty_subsets_are_valid()
    {
        var definition = SetupFunctions.evaluationStandardMssi;
        var outputs = MapModule.OfSeq(definition.SortopiaDataTypes.Select(type => Tuple.Create(type, ListModule.OfArray(type.IsSorterModelSet ? new[] { "Repl" } : Array.Empty<string>()))));
        FunctionConfiguration.validateOutputs(definition, outputs);
        var invalid = MapModule.OfSeq(definition.SortopiaDataTypes.Select(type => Tuple.Create(type, ListModule.OfArray(new[] { "NotRequired" }))));
        Assert.Contains("unique required", Assert.Throws<ArgumentException>(() => FunctionConfiguration.validateOutputs(definition, invalid)).Message);
    }
    [Fact]
    public void Query_editor_renders_phase_output_choices_and_reports_overlap()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module"); module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
                var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
                var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
                var editor = run.Editor!;
                editor.AddResultsParameterCommand.Execute(null);
                var invalid = editor.ResultsParameters.Single(); invalid.Key = "ChannelCount"; invalid.Values[0].Text = "8";
                Assert.DoesNotContain("ChannelCount", invalid.KeyChoices); Assert.Contains("already has a default", invalid.Error);
                Assert.Empty(editor.ResultsParameterSetsPreview.Rows); invalid.RemoveCommand.Execute(null);
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", false, "8");
                editor.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
                var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = editor, ActiveItemKey = "resultsTypes" };
                var border = new System.Windows.Controls.Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 31)), Padding = new System.Windows.Thickness(20), Child = view };
                border.Measure(new System.Windows.Size(520, 720)); border.Arrange(new System.Windows.Rect(0, 0, 520, 720)); border.UpdateLayout();
                Assert.Equal(System.Windows.Visibility.Visible, ((System.Windows.Controls.ItemsControl)view.FindName("ResultsQueryOutputsList")).Visibility);
                if (Environment.GetEnvironmentVariable("SORTOPIA_QUERY_SCREENSHOT") is { Length: > 0 } path) {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(520, 720, 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(border);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(path); encoder.Save(file);
                }
            } catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Fact]
    public void Project_query_selection_must_be_available_in_every_child_run()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module"); module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var first = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "First"); project.Children.Add(first);
        WorkspaceParameterSetsPreviewTests.AddSpan(first, "ChannelCount", false, "8");
        var second = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Second"); project.Children.Add(second);
        Assert.Empty(project.Editor!.ResultsQueryOutputs.Single().Keys);
        WorkspaceParameterSetsPreviewTests.AddSpan(second, "ChannelCount", false, "4");
        Assert.Equal("ChannelCount", Assert.Single(project.Editor.ResultsQueryOutputs.Single().Keys).Key);
    }
}
