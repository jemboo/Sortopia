using System.IO;
using System.Numerics;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
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
    // Test-fixture setup only: configure which inputs are spans before exercising the UI.
    internal static void ConfigureExplicitInputs(WorkspaceTreeNode run, bool setup)
    {
        if (setup && !run.IsBranch || run.Parent?.Editor is not { IsEditable: true }) return;
        var editor = run.Editor!;
        var values = (setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults).ToArray();
        foreach (var value in values)
            if (!(setup ? editor.SetupParameters : editor.ResultsParameters).Any(span => span.Key == value.Key))
                AddSpan(run, value.Key, setup, value.Value);
    }
    internal static WorkspaceParameterSpan AddSpan(WorkspaceTreeNode run, string key, bool setup, params string[] values)
    {
        var editor = run.Editor!;
        if (run.Parent?.Editor is { IsEditable: true } project)
        {
            var overrides = project.ToLocalOverrides();
            if (setup) {
                overrides.excludedSetupDefaults = (overrides.excludedSetupDefaults ?? []).Append(key).Distinct().ToArray();
                overrides.setupParameterDefaults = overrides.setupParameterDefaults?.Where(value => value.keyName != key).ToArray();
            } else {
                overrides.excludedResultsDefaults = (overrides.excludedResultsDefaults ?? []).Append(key).Distinct().ToArray();
                overrides.resultsParameterDefaults = overrides.resultsParameterDefaults?.Where(value => value.keyName != key).ToArray();
            }
            project.LoadOverrides(overrides);
        }
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
        if (page.Selected!.Editor!.ResultsFunctionName.Length == 0) page.Selected.Editor.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSimpleSorterModels;
        if (branch && page.Selected.Editor.SetupFunctionName.Length == 0) page.Selected.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformMssiSimpleSorterModels;
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
        AddSpan(run, "Repl", false, "1", "2");
        AddSpan(run, "ChannelCount", false, "+004", "8");
        if (branch) AddSpan(run, "Repl", true, "3", "4", "5");
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        var results = run.Editor!.ResultsParameterSetsPreview;
        Assert.Equal(new BigInteger(4), results.TotalCount);
        Assert.Equal(new[] { "1:4", "1:8", "2:4", "2:8" }, results.Rows.Select(row => $"{row.ParameterSet.TryFind(ParamKeys.replKey).Value}:{row.ParameterSet.TryFind(ParamKeys.channelCountKey).Value}"));
        Assert.All(results.Columns.Where(column => column.Key is "Repl" or "ChannelCount"), column => { Assert.Equal("Integer", column.TypeLabel); Assert.Equal("run span", column.Source); });
        Assert.Equal("4", results.Rows[0].Values[Array.FindIndex(results.Columns, column => column.Key == "ChannelCount")]);
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
        var saved = WorkspaceTestFiles.LoadRuntimeSnapshot(Root).Modules[0].Modules[0].Modules[0].Modules[0].Projects[0].Runs[0];
        var generated = RunParameterSets.create(saved);
        Assert.Equal(generated.Results, results.Rows.Select(row => row.ParameterSet));
        if (branch) Assert.Equal(generated.Setup.Value, setup.Rows.Select(row => row.ParameterSet));
    }

    [Fact]
    public void Preview_refreshes_span_edits_and_preserves_explicit_values_after_default_changes()
    {
        var page = Open(); var (ancestor, _) = WorkspaceParameterInheritanceTests.Hierarchy(page, true);
        var run = AddRun(page, true); var editor = run.Editor!; WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        var original = editor.ResultsParameterSetsPreview; Assert.Same(original, editor.ResultsParameterSetsPreview);
        ancestor.Editor!.ResultsParameterDefaults.Single().Value.Text = "24";
        Assert.NotSame(original, editor.ResultsParameterSetsPreview);
        Assert.Equal(16, Assert.Single(editor.ResultsParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var span = editor.ResultsParameters.Single(row => row.Key == "ChannelCount"); span.Values[0].Text = "bad";
        Assert.NotEmpty(editor.ResultsParameterSetsPreview.Error); Assert.Empty(editor.ResultsParameterSetsPreview.Rows); Assert.True(editor.SetupParameterSetsPreview.HasRows);
        span.Values[0].Text = "12"; Assert.Equal(12, editor.ResultsParameterSetsPreview.Rows[0].ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
        var duplicate = AddSpan(run, "ChannelCount", false, "16"); Assert.Contains("unique", editor.ResultsParameterSetsPreview.Error);
        duplicate.RemoveCommand.Execute(null); span.RemoveCommand.Execute(null); Assert.Contains("Missing values", editor.ResultsParameterSetsPreview.Error);
    }

    [Fact]
    public void Default_only_preview_is_valid_until_query_spans_are_selected()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var module = page.Selected!;
        module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings());
        var run = AddRun(page, false); var editor = run.Editor!;
        Assert.Single(editor.ResultsParameterSetsPreview.Rows);
        Assert.Empty(editor.ResultsParameterSetsPreview.Error);
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Suffix_preview_uses_each_phases_function_and_each_width_and_success_fraction(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch);
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        module.Editor.SetupFunctionName = SetupFunctionNames.uniformMsceSimpleSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        AddSpan(run, "ChannelCount", setup, "4", "8");
        AddSpan(run, "SorterSuccessFraction", setup, "50%", "90%", "99%");
        WorkspaceParameterSetsPreview Preview() => setup ? run.Editor!.SetupParameterSetsPreview : run.Editor!.ResultsParameterSetsPreview;
        var preview = Preview();
        Assert.True(preview.HasPreviews); Assert.Equal(6, preview.Rows.Length);
        foreach (var row in preview.Rows)
        {
            var width = row.ParameterSet.TryFind(ParamKeys.channelCountKey).Value;
            var percent = SorterSuccessFraction.percent(row.ParameterSet.TryFind(ParamKeys.sorterSuccessFractionKey).Value);
            var expected = (setup, width, percent) switch {
                (true, 4, 50) => 7, (true, 4, 90) => 12, (true, 4, 99) => 18,
                (true, 8, 50) => 17, (true, 8, 90) => 26, (true, 8, 99) => 40,
                (false, 4, 50) => 5, (false, 4, 90) => 9, (false, 4, 99) => 15,
                (false, 8, 50) => 22, (false, 8, 90) => 33, (false, 8, 99) => 49,
                _ => throw new InvalidOperationException() };
            Assert.Equal(expected, FunctionParameterSupportTests.SuffixStages(row));
            Assert.Null(row.ParameterSet.TryFind(ParamKeys.stageLengthKey));
            Assert.Equal(row.ParameterSet.ParamMap.Count, row.Values.Length);
        }
        var reps = AddSpan(run, "Repl", setup, Enumerable.Range(0, 100).Select(value => value.ToString()).ToArray());
        Assert.Equal(600, (int)Preview().TotalCount);
        Assert.Equal(WorkspaceParameterSetsPreview.MaximumRows, Preview().Rows.Length);
        Assert.All(Preview().Rows, row => Assert.NotNull(FunctionParameterSupportTests.SuffixStages(row)));
        reps.Values.Last().Text = "invalid";
        Assert.Empty(Preview().Rows); Assert.False(Preview().HasPreviews); Assert.NotEmpty(Preview().Error);
    }

    [Fact]
    public void Prefix_suffix_preview_refreshes_inherited_values_and_rejects_invalid_drafts()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models");
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixMssiSimpleSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        var prefix = module.Editor.ResultsParameterDefaults.Single(row => row.Key == "PrefixLibId").Value;
        var fraction = module.Editor.ResultsParameterDefaults.Single(row => row.Key == "SorterSuccessFraction").Value;
        Assert.Equal(48, FunctionParameterSupportTests.SuffixStages(Assert.Single(run.Editor!.ResultsParameterSetsPreview.Rows)));
        prefix.Text = PrefixLibId.toString(prefixLibId.create(8, 3, prefixLibVariant.PrefixA));
        Assert.Equal(46, FunctionParameterSupportTests.SuffixStages(Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows)));
        fraction.Text = "50%";
        Assert.Equal(19, FunctionParameterSupportTests.SuffixStages(Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows)));
        prefix.Text = "invalid";
        Assert.Empty(run.Editor.ResultsParameterSetsPreview.Rows); Assert.False(run.Editor.ResultsParameterSetsPreview.HasPreviews);
        module.Editor.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.False(run.Editor.ResultsParameterSetsPreview.HasPreviews);
        Assert.Null(FunctionParameterSupportTests.SuffixStages(Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows)));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
