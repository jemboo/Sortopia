using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ExplicitQuerySpanTests
{
    private static FSharpList<T> List<T>(params T[] values) => ListModule.OfArray(values);

    [Fact]
    public void Required_parameters_are_independent_of_output_query_keys()
    {
        var required = List(ParamKey.describe(ParamKeys.channelCountKey), ParamKey.describe(ParamKeys.messageKey));
        var output = sortopiaDataType.NewSortableTests("");
        var definition = FunctionDefinition.results(required, List(Tuple.Create(output, List("ChannelCount"))),
            FuncConvert.FromFunc<runHost, Unit>(_ => null!));
        Assert.Equal(new[] { "ChannelCount", "Message" }, definition.RequiredParameters.Select(key => key.Name));
        Assert.Equal(new[] { "ChannelCount" }, definition.DataTypes[output]);
        var defaults = List(ParameterBinding.create(ParamKeys.channelCountKey, 16), ParameterBinding.create(ParamKeys.messageKey, "fixed"));
        var spans = List(ParameterSpan.create(ParamKeys.channelCountKey, List(8, 16)));
        FunctionParameters.validateInputs(required, definition.DataTypes[output], defaults, spans);
        var sets = ParameterSets.create(defaults, spans);
        Assert.Equal(new[] { 8, 16 }, sets.Select(set => set.TryFind(ParamKeys.channelCountKey).Value));
        Assert.All(sets, set => { Assert.Equal(2, set.ParamMap.Count); Assert.Equal("fixed", set.TryFind(ParamKeys.messageKey).Value); });
        Assert.Contains("explicit", Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output], defaults, List<parameterSpan>())).Message);
        Assert.Throws<ArgumentException>(() => FunctionDefinition.results(required, List(Tuple.Create(output, List("Repl"))), definition.Execute));
        Assert.Throws<ArgumentException>(() => FunctionDefinition.results(List(required.Head, required.Head), List(Tuple.Create(output, List("ChannelCount"))), definition.Execute));
        Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output], List(defaults.Head), spans));
        Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output],
            ListModule.OfSeq(defaults.Append(ParameterBinding.create(ParamKeys.replKey, 0))), spans));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_loading_rejects_a_query_key_supplied_only_by_a_default(bool setup)
    {
        var value = RunExecutionTests.MakeRun(setup);
        var config = value is run.RootRun root ? projectConfiguration.NewRootProjectConfig(root.Item.ProjectConfiguration)
            : projectConfiguration.NewBranchProjectConfig(((run.BranchRun)value).Item.ProjectConfiguration);
        var parent = project.create(value.ParentProjectId, "Project", "Project", config, []);
        var dto = RunDto.fromDomain(value);
        var key = setup ? dto.setupParameterSpans[0].keyName : dto.resultsParameterSpans[0].keyName;
        if (setup) dto.setupParameterSpans = dto.setupParameterSpans.Where(span => span.keyName != key).ToArray();
        else dto.resultsParameterSpans = dto.resultsParameterSpans.Where(span => span.keyName != key).ToArray();
        var error = Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, dto)).Message;
        Assert.Contains(key, error); Assert.Contains("explicit", error); Assert.Contains("Defaults do not satisfy", error);
    }

    private static (WorkspaceTreeNode Module, WorkspaceTreeNode Run) Editors()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(RunExecutionTests.Settings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        return (module, run);
    }

    [Fact]
    public void Copy_actions_create_explicit_typed_spans_without_overwriting_existing_spans_or_defaults()
    {
        var (module, run) = Editors(); var editor = run.Editor!;
        Assert.Empty(editor.ResultsParameters); Assert.Empty(editor.SetupParameters);
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows); Assert.Empty(editor.SetupParameterSetsPreview.Rows);
        Assert.Contains("explicit", editor.ResultsParametersError); Assert.Contains("ChannelCount", editor.SetupParametersError);
        var inherited = editor.InheritedSetupDefaults.Single(value => value.Key == "ChannelCount");
        Assert.Contains("requires an explicit", inherited.Usage);
        Assert.True(inherited.UseAsSpanCommand!.CanExecute(null));
        inherited.UseAsSpanCommand.Execute(null);
        var copied = Assert.Single(editor.SetupParameters);
        Assert.Equal("ChannelCount", copied.Key); Assert.Equal("4", Assert.Single(copied.Values).Text);
        Assert.Equal(parameterValueType.Integer, copied.ValueType);
        Assert.False(inherited.UseAsSpanCommand.CanExecute(null));
        Assert.Equal("4", module.Editor!.SetupParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text);

        editor.UseSetupDefaultsAsSpansCommand.Execute(null);
        editor.UseResultsDefaultsAsSpansCommand.Execute(null);
        Assert.Equal(2, editor.SetupParameters.Count); Assert.Equal(2, editor.ResultsParameters.Count);
        Assert.Empty(editor.SetupParametersError); Assert.Empty(editor.ResultsParametersError);
        Assert.All(editor.SetupParameterSetsPreview.Columns, column => Assert.Equal("run span", column.Source));
        var library = editor.ResultsParameters.Single(value => value.Key == "MergeLibId");
        Assert.True(library.Values[0].IsLibraryId); Assert.Empty(library.Error);
        copied.Values[0].Text = "8";
        editor.UseSetupDefaultsAsSpansCommand.Execute(null);
        Assert.Equal("8", copied.Values[0].Text); Assert.Equal(2, editor.SetupParameters.Count);
        module.Editor.SetupParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "16";
        Assert.Equal("8", copied.Values[0].Text); // A copied span is a local value, not a live default link.
        copied.RemoveCommand.Execute(null);
        Assert.Contains("ChannelCount", editor.SetupParameterSetsPreview.Error); Assert.Empty(editor.SetupParameterSetsPreview.Rows);
        Assert.Single(editor.ResultsParameterSetsPreview.Rows);
        editor.InheritedSetupDefaults.Single(value => value.Key == "ChannelCount").UseAsSpanCommand!.Execute(null);
        Assert.Equal("16", editor.SetupParameters.Single(value => value.Key == "ChannelCount").Values[0].Text);
    }

    [Fact]
    public void Adding_missing_rows_does_not_silently_copy_defaults_and_invalid_defaults_cannot_be_copied()
    {
        var (module, run) = Editors(); var editor = run.Editor!;
        module.Editor!.SetupParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "invalid";
        Assert.False(editor.InheritedSetupDefaults.Single(value => value.Key == "ChannelCount").UseAsSpanCommand!.CanExecute(null));
        editor.AddMissingResultsParametersCommand.Execute(null);
        Assert.Equal(2, editor.ResultsParameters.Count);
        Assert.All(editor.ResultsParameters, span => Assert.Empty(span.Values));
        editor.UseResultsDefaultsAsSpansCommand.Execute(null);
        Assert.All(editor.ResultsParameters, span => Assert.Empty(span.Values));
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
    }

    [Fact]
    public async Task Save_execute_and_round_trip_require_and_preserve_explicit_spans()
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-explicit-spans-");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(folder);
            var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            page.AddBranchModuleCommand.Execute(null);
            page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(true));
            page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
            var editor = page.Selected!.Editor!;
            Assert.False(page.SaveWorkspaceCommand.CanExecute(null)); Assert.False(page.ExecuteRunCommand.CanExecute(null));
            Assert.Contains("explicit", page.SaveHint);
            Assert.Empty(editor.ResultsParameters); // Inspecting and attempting Save must not fill in inputs.
            await page.SaveWorkspaceAsync();
            Assert.Empty(editor.ResultsParameters);
            editor.UseResultsDefaultsAsSpansCommand.Execute(null);
            Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
            editor.UseSetupDefaultsAsSpansCommand.Execute(null);
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            WorkspaceRunExecutionTests.SelectRun(page);
            Assert.True(page.ExecuteRunCommand.CanExecute(null), page.ExecuteHint);
            await page.ExecuteRunAsync(); Assert.Empty(page.Error);
            Assert.Equal(runExecutionStatus.Completed, page.SelectedExecution!.Report!.Status);
            var saved = WorkspaceFileStore.LoadDefinition(folder).Modules[0].Projects[0].Runs[0];
            Assert.Equal(2, saved.ResultsParameterSpans.Length); Assert.Equal(2, saved.SetupParameterSpans.Value.Length);
            Assert.All(saved.ResultsParameterSpans.Concat(saved.SetupParameterSpans.Value), span => Assert.Single(span.Values));
            var reopened = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var restored = reopened.Roots[0].Children[0].Children[0].Children[0].Editor!;
            Assert.Empty(restored.ResultsParametersError); Assert.Empty(restored.SetupParametersError);
            restored.ResultsParameters[0].RemoveCommand.Execute(null);
            Assert.False(reopened.SaveWorkspaceCommand.CanExecute(null)); Assert.Empty(restored.ResultsParameterSetsPreview.Rows);
        }
        finally { temporary.Delete(true); }
    }
}
