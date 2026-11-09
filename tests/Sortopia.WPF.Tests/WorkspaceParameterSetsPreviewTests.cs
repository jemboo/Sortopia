using System.IO;
using System.Numerics;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.WPF.Models;
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
        page.Selected!.Editor!.ResultsFunctionName = "Evaluate";
        if (branch) page.Selected.Editor.SetupFunctionName = "Setup";
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
        var results = run.Editor!.ResultsParameterSetsPreview;
        Assert.Equal(new BigInteger(4), results.TotalCount);
        Assert.Equal(new[] { "1:4", "1:8", "2:4", "2:8" }, results.Rows.Select(row => $"{row.ParameterSet.TryFind(ParamKeys.stageLengthKey).Value}:{row.ParameterSet.TryFind(ParamKeys.channelCountKey).Value}"));
        Assert.All(results.Columns, column => { Assert.Equal("Integer", column.TypeLabel); Assert.Equal("run span", column.Source); });
        Assert.Equal("4", results.Rows[0].Values[0]);
        var setup = run.Editor.SetupParameterSetsPreview;
        if (branch)
        {
            Assert.Equal(3, setup.Rows.Length);
            Assert.Equal("default", setup.Columns.Single(column => column.Key == "ChannelCount").Source);
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
    public void Preview_updates_with_ancestor_defaults_and_local_spans_and_never_keeps_stale_invalid_rows()
    {
        var page = Open();
        var (ancestor, _) = WorkspaceParameterInheritanceTests.Hierarchy(page, true);
        var editor = AddRun(page, true).Editor!;
        var original = editor.ResultsParameterSetsPreview;
        Assert.Same(original, editor.ResultsParameterSetsPreview);
        ancestor.Editor!.ResultsParameterDefaults.Single().Value.Text = "24";
        Assert.NotSame(original, editor.ResultsParameterSetsPreview);
        Assert.Equal(24, Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        Assert.Equal(32, Assert.Single(editor.SetupParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var span = AddSpan(editor, "ChannelCount", false, "4", "8");
        Assert.Equal(2, editor.ResultsParameterSetsPreview.Rows.Length);
        span.Values[0].Text = "bad";
        Assert.NotEmpty(editor.ResultsParameterSetsPreview.Error);
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
        Assert.True(editor.SetupParameterSetsPreview.HasRows);
        span.Values[0].Text = "12";
        Assert.Equal(12, editor.ResultsParameterSetsPreview.Rows[0].ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var duplicate = AddSpan(editor, "ChannelCount", false, "16");
        Assert.Contains("unique", editor.ResultsParameterSetsPreview.Error);
        duplicate.RemoveCommand.Execute(null);
        span.Values.Clear();
        Assert.NotEmpty(editor.ResultsParameterSetsPreview.Error);
        span.RemoveCommand.Execute(null);
        Assert.Equal(24, Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
    }

    [Fact]
    public void Preview_requires_query_values_and_preserves_empty_and_typed_defaults_only_sets()
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        var module = page.Selected!;
        var editor = AddRun(page, false).Editor!;
        Assert.Empty(Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet.ParamMap);
        Assert.Contains("empty parameter set", editor.ResultsParameterSetsPreview.Summary);
        module.Editor!.AddResultsQueryKeyCommand.Execute(null);
        module.Editor.ResultsQueryKeys.Single().Text = "Message";
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
        Assert.Contains("Message", editor.ResultsParameterSetsPreview.Error);
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "Message", "");
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "SetupCompleted", "true");
        var preview = editor.ResultsParameterSetsPreview;
        var set = Assert.Single(preview.Rows).ParameterSet;
        Assert.Equal("", set.TryFind(ParamKeys.messageKey).Value);
        Assert.True(set.TryFind(ParamKeys.setupCompletedKey).Value);
        Assert.All(preview.Columns, column => Assert.Equal("default", column.Source));
        Assert.False(module.Editor.ResultsParameterSetsPreview.HasRows);
    }

    [Fact]
    public void Large_products_only_materialize_the_preview_limit_and_report_array_overflow()
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        var editor = AddRun(page, false).Editor!;
        for (var index = 0; index < 25; index++) AddSpan(editor, $"Key{index}", false, "a", "b");
        var preview = editor.ResultsParameterSetsPreview;
        Assert.Equal(BigInteger.Pow(2, 25), preview.TotalCount);
        Assert.Equal(WorkspaceParameterSetsPreview.MaximumRows, preview.Rows.Length);
        Assert.Contains("first 50 of 33,554,432", preview.Summary);
        for (var index = 25; index < 31; index++) AddSpan(editor, $"Key{index}", false, "a", "b");
        Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
        Assert.Contains("too many combinations", editor.ResultsParameterSetsPreview.Error);
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
