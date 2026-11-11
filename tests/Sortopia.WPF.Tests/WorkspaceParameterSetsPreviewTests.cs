using System.IO;
using System.Numerics;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceParameterSetsPreviewTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-parameter-preview-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }
    internal static WorkspaceParameterSpan AddSpan(WorkspaceConfigurationViewModel editor, string key, bool setup, params string[] values)
    {
        (setup ? editor.AddSetupParameterCommand : editor.AddResultsParameterCommand).Execute(null);
        var span = (setup ? editor.SetupParameters : editor.ResultsParameters).Last();
        span.Key = key;
        span.Values.Clear();
        foreach (var value in values)
        {
            span.AddValueCommand.Execute(null);
            span.Values.Last().Text = value;
        }
        return span;
    }
    private static WorkspaceTreeNode AddRun(WorkspacePageViewModel page, bool branch)
    {
        if (page.Selected!.Editor!.ResultsFunctionName.Length == 0) page.Selected.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformSorterModels;
        if (branch && page.Selected.Editor.SetupFunctionName.Length == 0) page.Selected.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformSorterModels;
        page.Selected.Editor.AddRequiredResultsDefaultsCommand.Execute(null); if (branch) page.Selected.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        page.AddProjectCommand.Execute(null);
        page.AddRunCommand.Execute(null);
        return page.Selected!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preview_matches_runtime_order_and_typed_values_for_results_and_setup(bool branch)
    {
        var page = Open();
        WorkspaceParameterInheritanceTests.Hierarchy(page, branch);
        var run = AddRun(page, branch);
        AddSpan(run.Editor!, "StageLength", false, "1", "2");
        AddSpan(run.Editor!, "ChannelCount", false, "+004", "8");
        if (branch) AddSpan(run.Editor!, "StageLength", true, "3", "4", "5");
        run.Editor!.UseResultsDefaultsAsSpansCommand.Execute(null); if (branch) run.Editor.UseSetupDefaultsAsSpansCommand.Execute(null);
        var results = run.Editor.ResultsParameterSetsPreview;
        Assert.Equal(new BigInteger(4), results.TotalCount);
        Assert.Equal(new[] { "1:4", "1:8", "2:4", "2:8" }, results.Rows.Select(row => $"{row.ParameterSet.TryFind(ParamKeys.stageLengthKey).Value}:{row.ParameterSet.TryFind(ParamKeys.channelCountKey).Value}"));
        Assert.All(results.Columns.Where(column => column.Key is "StageLength" or "ChannelCount"), column => { Assert.Equal("Integer", column.TypeLabel); Assert.Equal("run span", column.Source); });
        Assert.Equal("4", results.Rows[0].Values[0]);
        var setup = run.Editor.SetupParameterSetsPreview;
        if (branch)
        {
            Assert.Equal(3, setup.Rows.Length);
            Assert.Equal("run span", setup.Columns.Single(column => column.Key == "ChannelCount").Source);
            Assert.All(setup.Rows, row => Assert.Equal(32, row.ParameterSet.TryFind(ParamKeys.channelCountKey).Value));
        }
        else Assert.False(setup.HasRows);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].Modules[0].Modules[0].Projects[0].Runs[0];
        var generated = RunParameterSets.create(saved);
        Assert.Equal(generated.Results, results.Rows.Select(row => row.ParameterSet));
        if (branch) Assert.Equal(generated.Setup.Value, setup.Rows.Select(row => row.ParameterSet));
    }

    [Fact]
    public void Preview_refreshes_span_edits_and_preserves_explicit_values_after_default_changes()
    {
        var page = Open(); var (ancestor, _) = WorkspaceParameterInheritanceTests.Hierarchy(page, true);
        var editor = AddRun(page, true).Editor!; editor.UseResultsDefaultsAsSpansCommand.Execute(null); editor.UseSetupDefaultsAsSpansCommand.Execute(null);
        var original = editor.ResultsParameterSetsPreview; Assert.Same(original, editor.ResultsParameterSetsPreview);
        ancestor.Editor!.ResultsParameterDefaults.Single().Value.Text = "24";
        Assert.NotSame(original, editor.ResultsParameterSetsPreview);
        Assert.Equal(16, Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var span = editor.ResultsParameters.Single(row => row.Key == "ChannelCount"); span.Values[0].Text = "bad";
        Assert.NotEmpty(editor.ResultsParameterSetsPreview.Error); Assert.Empty(editor.ResultsParameterSetsPreview.Rows); Assert.True(editor.SetupParameterSetsPreview.HasRows);
        span.Values[0].Text = "12"; Assert.Equal(12, editor.ResultsParameterSetsPreview.Rows[0].ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var duplicate = AddSpan(editor, "ChannelCount", false, "16"); Assert.Contains("unique", editor.ResultsParameterSetsPreview.Error);
        duplicate.RemoveCommand.Execute(null); span.RemoveCommand.Execute(null); Assert.Contains("explicit", editor.ResultsParameterSetsPreview.Error);
    }

    [Fact]
    public void Preview_requires_explicit_query_spans_even_when_defaults_exist()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var module = page.Selected!;
        module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
        var editor = AddRun(page, false).Editor!;
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
        Assert.Contains("ChannelCount", editor.ResultsParameterSetsPreview.Error);
        editor.UseResultsDefaultsAsSpansCommand.Execute(null);
        var set = Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet;
        Assert.Equal(8, set.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Equal("Bools", set.TryFind(ParamKeys.sortableDataFormatKey).Value);
        Assert.All(editor.ResultsParameterSetsPreview.Columns, column => Assert.Equal("run span", column.Source));
    }

    [Fact]
    public void Large_products_only_materialize_the_preview_limit_and_report_array_overflow()
    {
        var spans = Enumerable.Range(0, 25).Select(i => new WorkspaceParameterSpan($"Key{i}", ["a", "b"], _ => { }, _ => null)).ToList();
        var preview = WorkspaceParameterSetsPreview.Create([], spans, "");
        Assert.Equal(BigInteger.Pow(2, 25), preview.TotalCount); Assert.Equal(WorkspaceParameterSetsPreview.MaximumRows, preview.Rows.Length);
        Assert.Contains("first 50 of 33,554,432", preview.Summary);
        spans.AddRange(Enumerable.Range(25, 6).Select(i => new WorkspaceParameterSpan($"Key{i}", ["a", "b"], _ => { }, _ => null)));
        preview = WorkspaceParameterSetsPreview.Create([], spans, ""); Assert.Empty(preview.Rows); Assert.Contains("too many combinations", preview.Error);
    }

    [Fact]
    public void Bounded_generation_validates_values_beyond_the_visible_rows_and_rejects_invalid_limits()
    {
        var bindings = ListModule.Empty<parameterBinding>();
        var valid = ListModule.OfArray(new[] { ParameterSpan.create(ParamKeys.channelCountKey, ListModule.OfSeq(Enumerable.Range(1, 80))) });
        Assert.Equal(ParameterSets.create(bindings, valid).Take(3), ParameterSets.preview(3, bindings, valid));
        Assert.Throws<ArgumentException>(() => ParameterSets.preview(0, bindings, valid));
        var invalid = ListModule.OfArray(new[] { ParameterSpan.create(ParamKey.text("ChannelCount"), ListModule.OfSeq(Enumerable.Range(1, 80).Select(value => value.ToString()).Append("bad"))) });
        Assert.Throws<ArgumentException>(() => ParameterSets.preview(3, bindings, invalid));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
