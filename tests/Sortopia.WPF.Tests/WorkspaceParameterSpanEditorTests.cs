using System.Globalization;
using System.IO;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceParameterSpanEditorTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-span-editor-");
    private WorkspacePageViewModel Open(bool branch)
    {
        var root = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        if (page.HasSavedWorkspace) return page;
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var value = WorkspaceHierarchyTests.ProjectSettings(branch);
        value.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformSorterModels; value.resultsParameterDefaults = RunExecutionTests.ModelDefaults();
        if (branch) { value.setupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformSorterModels; value.setupParameterDefaults = RunExecutionTests.ModelDefaults(); }
        page.Selected!.Editor!.LoadProject(value);
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        return page;
    }
    private static WorkspaceParameterSpan Span(string key, string text) => new(key, [text], _ => { }, _ => null);

    [Theory]
    [InlineData("RngType", "SplitMix64", "NET")]
    [InlineData("SortableDataFormat", "Bools", "BoolArray")]
    [InlineData("SimpleSorterModelType", "Mssi", "unknown")]
    [InlineData("SorterModelType", "SimpleR", "MssiR")]
    [InlineData("SorterEvalType", "V2", "V4")]
    [InlineData("MergeSuffixType", "NoSuffix", "unknown")]
    public void Named_choices_use_the_domain_contract_and_validate_unknown_values(string key, string valid, string invalid)
    {
        var row = Span(key, valid); var value = row.Values.Single();
        Assert.Contains(key, row.KeyChoices); Assert.Contains(valid, value.Choices);
        Assert.True(value.HasChoices); Assert.False(value.IsTextEditor); Assert.Empty(value.Error);
        value.Text = invalid; Assert.NotEmpty(row.Error); Assert.Contains(key, row.Error);
        Assert.Equal(invalid, value.Text);
        value.Text = valid; Assert.Empty(row.Error);
    }

    [Theory]
    [InlineData("ChannelCount", "0", "1")]
    [InlineData("SorterCount", "0", "1")]
    [InlineData("StageLength", "-1", "0")]
    [InlineData("Repl", "-1", "0")]
    [InlineData("AMax", "-1", "0")]
    public void Numeric_keys_enforce_their_own_limits_without_replacing_invalid_text(string key, string invalid, string valid)
    {
        var row = Span(key, invalid); var value = row.Values.Single();
        Assert.True(value.IsNumeric); Assert.True(value.IsTextEditor); Assert.NotEmpty(row.Error);
        Assert.Contains(key, row.Error); Assert.Equal(invalid, value.Text);
        value.Text = valid; Assert.Empty(row.Error);
        value.Text = "2147483648"; Assert.NotEmpty(row.Error);
    }

    [Fact]
    public void Changing_the_key_reselects_the_editor_and_revalidates_every_existing_value()
    {
        var row = Span("Message", "text"); row.AddValueCommand.Execute(null); row.Values[1].Text = "False";
        row.Key = "SetupCompleted";
        Assert.All(row.Values, value => Assert.True(value.IsBoolean));
        Assert.NotEmpty(row.Values[0].Error); Assert.Empty(row.Values[1].Error);
        row.Key = "RngType";
        Assert.All(row.Values, value => Assert.True(value.HasChoices)); Assert.NotEmpty(row.Values[1].Error);
        row.Key = "Id";
        Assert.All(row.Values, value => Assert.Equal(parameterValueType.Guid, value.ValueType));
        row.Values[0].Text = Guid.NewGuid().ToString(); Assert.Empty(row.Values[0].Error);
        row.Key = "Message";
        Assert.All(row.Values, value => { Assert.True(value.IsMultiline); Assert.True(value.IsTextEditor); Assert.Empty(value.Error); });
        Assert.Equal("False", row.Values[1].Text);
    }

    [Fact]
    public void Catalog_identifiers_have_choices_and_use_existing_parsers()
    {
        foreach (var (key, value) in new[] {
            ("PrefixLibId", PrefixLibId.toString(PrefixLib.ids()[0])),
            ("SorterLibId", SorterLibId.toString(SorterLib.ids()[0])),
            ("MergeLibId", MergeLibId.toString(MergeLibId.availableForChannelCount(4)[0])) })
        {
            var row = Span(key, value);
            Assert.True(row.Values[0].IsLibraryId); Assert.NotEmpty(row.Values[0].LibraryId.CatalogChoices); Assert.Empty(row.Error);
            row.Values[0].Text = "invalid identifier"; Assert.NotEmpty(row.Error);
        }
        Assert.Empty(Span("SimpleSorterModelType", "mssi").Error); // A supported parser alias remains valid.
    }

    [Fact]
    public void Key_choices_are_restricted_to_the_inherited_function_contract()
    {
        var page = Open(true); var run = page.Selected!;
        run.Parent!.Parent!.Editor!.LoadProject(FunctionOutputContractTests.Combined(true));
        var results = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", false, "8");
        var setup = WorkspaceParameterSetsPreviewTests.AddSpan(run, "StageLength", true, "3");
        Assert.DoesNotContain("SorterEvalType", results.KeyChoices); Assert.DoesNotContain("StageLength", results.KeyChoices);
        Assert.Contains("StageLength", setup.KeyChoices); Assert.DoesNotContain("SorterEvalType", setup.KeyChoices);
        Assert.Equal(parameterValueType.Integer, results.ValueType); Assert.Equal(parameterValueType.Integer, setup.ValueType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_choices_block_save_and_preview_then_round_trip_after_correction(bool setup)
    {
        var page = Open(setup); var editor = page.Selected!.Editor!;
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, false); if (setup) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, true);
        (setup ? editor.SetupParameters : editor.ResultsParameters).Single(row => row.Key == "RngType").RemoveCommand.Execute(null);
        var row = WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "RngType", setup, "invalid");
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null)); Assert.Contains("RngType", page.SaveHint);
        Assert.Empty((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        row.Values[0].Text = "SplitMix64";
        Assert.Single((setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(page.WorkspaceFolder).Modules[0].Projects[0].Runs[0];
        var sets = RunParameterSets.create(saved);
        Assert.Equal("SplitMix64", (setup ? sets.Setup.Value : sets.Results)[0].TryFind(ParamKeys.rngTypeKey).Value);
        var reopened = Open(setup); // Existing saved workspace is loaded by the settings path.
        var restored = reopened.Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.True((setup ? restored.SetupParameters : restored.ResultsParameters).Single(row => row.Key == "RngType").Values[0].HasChoices);
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
