using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
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
        var definition = FunctionDefinition.results("1", required, List(Tuple.Create(output, List("ChannelCount"))), FuncConvert.FromFunc<runHost, Unit>(_ => null!));
        var defaults = List(ParameterBinding.create(ParamKeys.messageKey, "fixed"));
        var spans = List(ParameterSpan.create(ParamKeys.channelCountKey, List(8, 16)));
        FunctionParameters.validateInputs(required, definition.DataTypes[output], defaults, spans);
        var sets = ParameterSets.create(defaults, spans);
        Assert.Equal(new[] { 8, 16 }, sets.Select(set => set.TryFind(ParamKeys.channelCountKey).Value));
        Assert.All(sets, set => Assert.Equal("fixed", set.TryFind(ParamKeys.messageKey).Value));
        Assert.Contains("explicit", Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output], defaults, List<parameterSpan>())).Message);
        Assert.Throws<ArgumentException>(() => FunctionDefinition.results("1", required, List(Tuple.Create(output, List("Repl"))), definition.Execute));
        Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output], List<parameterBinding>(), spans));
        Assert.Throws<ArgumentException>(() => FunctionParameters.validateInputs(required, definition.DataTypes[output], List(defaults.Head, ParameterBinding.create(ParamKeys.replKey, 0)), spans));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_loading_rejects_a_query_key_without_an_explicit_span(bool setup)
    {
        var value = RunExecutionTests.MakeRun(setup);
        var config = value is run.RootRun root ? projectConfiguration.NewRootProjectConfig(root.Item.ProjectConfiguration) : projectConfiguration.NewBranchProjectConfig(((run.BranchRun)value).Item.ProjectConfiguration);
        var parent = project.create(value.ParentProjectId, "Project", "Project", config, []);
        var dto = RunDto.fromDomain(value);
        var key = setup ? dto.setupParameterSpans[0].keyName : dto.resultsParameterSpans[0].keyName;
        if (setup) dto.setupParameterSpans = dto.setupParameterSpans.Where(span => span.keyName != key).ToArray();
        else dto.resultsParameterSpans = dto.resultsParameterSpans.Where(span => span.keyName != key).ToArray();
        var error = Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, dto)).Message;
        Assert.Contains(key, error); Assert.Contains("explicit", error);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Defaults_stay_defaults_and_missing_span_rows_never_copy_their_values(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch); module.Editor!.LoadProject(RunExecutionTests.Settings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        var editor = run.Editor!;
        var rows = setup ? editor.SetupParameters : editor.ResultsParameters;
        var addMissing = setup ? editor.AddMissingSetupParametersCommand : editor.AddMissingResultsParametersCommand;
        var original = (setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults).Select(value => (value.Key, value.Value)).ToArray();
        Assert.Equal(2, original.Length);
        addMissing.Execute(null);
        Assert.Empty(rows);
        Assert.Single((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        Assert.Empty((setup ? editor.SetupQueryOutputs : editor.ResultsQueryOutputs).Single().Keys);
        (setup ? editor.AddSetupParameterCommand : editor.AddResultsParameterCommand).Execute(null);
        var invalid = rows.Single(); invalid.Key = original[0].Key; invalid.Values[0].Text = original[0].Value;
        Assert.DoesNotContain(original[0].Key, invalid.KeyChoices);
        Assert.Contains("already has a default", invalid.Error);
        Assert.Empty((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        Assert.Equal(original, (setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults).Select(value => (value.Key, value.Value)));
        invalid.RemoveCommand.Execute(null);
        Assert.Empty(rows);
        // Explicitly removing inheritance does not supply span values.
        foreach (var value in (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults).ToArray()) value.ExcludeCommand!.Execute(null);
        addMissing.Execute(null);
        Assert.Equal(2, rows.Count); Assert.All(rows, span => Assert.Empty(span.Values));
        Assert.Empty((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        Assert.Equal(2, (setup ? module.Editor.SetupParameterDefaults : module.Editor.ResultsParameterDefaults).Count);
    }
    [Fact]
    public async Task Save_execute_and_round_trip_preserve_selected_queries_and_disjoint_inputs()
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-explicit-spans-");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(folder);
            var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            page.AddBranchModuleCommand.Execute(null); page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(true));
            page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null); var editor = page.Selected!.Editor!;
            Assert.Empty(editor.ResultsParameters); Assert.Single(editor.ResultsParameterSetsPreview.Rows);
            WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, false); WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, true);
            foreach (var key in editor.ResultsQueryOutputs.Concat(editor.SetupQueryOutputs).SelectMany(output => output.Keys).ToArray()) key.Selected = true;
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error); WorkspaceRunExecutionTests.SelectRun(page);
            var report = await WorkspaceRunExecutionTests.GenerateAndExecuteAsync(page); Assert.Empty(page.Error); Assert.Equal(runExecutionStatus.Completed, report.Status);
            var saved = WorkspaceFileStore.LoadDefinition(folder).Modules[0].Projects[0].Runs[0];
            Assert.Equal(2, saved.ResultsParameterSpans.Length); Assert.Equal(2, saved.SetupParameterSpans.Value.Length);
            Assert.Empty(saved.ResultsParameterDefaults); Assert.Empty(saved.SetupParameterDefaults.Value);
            var reopened = new WorkspacePageViewModel(() => { }, _ => null, settings);
            var restored = reopened.Roots[0].Children[0].Children[0].Children[0].Editor!;
            Assert.False(restored.CanEditQuerySelection); Assert.All(restored.ResultsQueryOutputs.SelectMany(output => output.Keys), key => Assert.False(key.CanChange));
            restored.ResultsParameters[0].RemoveCommand.Execute(null);
            Assert.True(reopened.SaveWorkspaceCommand.CanExecute(null)); Assert.Empty(restored.ResultsParameterSetsPreview.Rows);
            page.Cancel(); reopened.Cancel();
        }
        finally { temporary.Delete(true); }
    }
}
