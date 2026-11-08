using System.IO;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceFieldTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-fields-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root); Settings.Save(Root);
        return new(() => { }, _ => null, Settings);
    }
    private WorkspaceTreeNode CreateProject(WorkspacePageViewModel page, bool branch)
    {
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.AddProjectCommand.Execute(null);
        var node = page.Selected!;
        node.Name = "Project defaults";
        var fields = node.Editor!;
        fields.Description = "Shared description";
        fields.ResultsDatabaseName = "Results";
        fields.ExecuteFunctionName = "Evaluate";
        fields.OutputTypes.Single(choice => choice.Value == outputDataType.SorterSetEval).IsSelected = true;
        fields.AddOutputQueryKeyCommand.Execute(null);
        fields.OutputQueryKeys.Single().Text = "Seed";
        if (branch)
        {
            fields.SetupDatabaseName = "Setup";
            fields.SetupFunctionName = "Generate";
            fields.SetupOutputTypes.Single(choice => choice.Value == outputDataType.SorterModelSet).IsSelected = true;
            fields.AddSetupQueryKeyCommand.Execute(null);
            fields.SetupQueryKeys.Single().Text = "Width";
        }
        Assert.Empty(page.Error);
        return node;
    }
    private static WorkspaceTreeNode CreateRun(WorkspacePageViewModel page, bool branch)
    {
        page.AddRunCommand.Execute(null);
        var node = page.Selected!;
        var fields = node.Editor!;
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        fields.AddMissingOutputParametersCommand.Execute(null);
        var output = fields.OutputParameters.Single();
        Assert.Equal("Seed", output.Key);
        Assert.Empty(output.Values);
        output.AddValueCommand.Execute(null);
        output.Values[0].Text = "7";
        if (branch)
        {
            fields.AddMissingSetupParametersCommand.Execute(null);
            var setup = fields.SetupParameters.Single();
            Assert.Equal("Width", setup.Key);
            setup.AddValueCommand.Execute(null); setup.Values[0].Text = "8";
        }
        Assert.Empty(page.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        return node;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fields_create_complete_projects_and_runs_without_delimited_value_parsing(bool branch)
    {
        var page = Open(); CreateProject(page, branch);
        var run = CreateRun(page, branch);
        var values = run.Editor!.OutputParameters.Single();
        values.Values[0].Text = "7,11";
        foreach (var value in new[] { " padded value ", "", "α", "first line\nsecond line" })
        { values.AddValueCommand.Execute(null); values.Values.Last().Text = value; }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var parent = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0];
        var saved = parent.Runs.Single();
        Assert.Equal(new[] { "7,11", " padded value ", "", "α", "first line\nsecond line" }, saved.ParameterSpans.Single().Values);
        Assert.Equal("Shared description", saved.Description);
        Assert.Null(RunDto.fromDomain(saved).description);
        Assert.Equal(new[] { outputDataType.SorterSetEval }, parent.OutputDataTypes);
        var reopened = Open().Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.True(reopened.DescriptionOverride.IsInherited);
        Assert.Equal("Inherited from Project defaults", reopened.DescriptionOverride.SourceLabel);
        Assert.Equal("Sorter set evaluations", reopened.OutputTypesSummary);
        if (branch)
        {
            Assert.True(reopened.SetupDatabaseOverride.IsInherited);
            Assert.Equal("Generate", reopened.SetupFunctionOverride.EffectiveValue);
            Assert.Equal("8", reopened.SetupParameters.Single().Values.Single().Text);
        }
    }

    [Fact]
    public async Task Explicit_blank_description_and_independent_setup_overrides_survive_reopening()
    {
        var page = Open(); var parent = CreateProject(page, true); var run = CreateRun(page, true);
        var fields = run.Editor!;
        fields.DescriptionOverride.IsOverridden = true;
        fields.DescriptionOverride.EffectiveValue = "";
        fields.SetupDatabaseOverride.IsOverridden = true;
        fields.SetupDatabaseOverride.EffectiveValue = "Run setup";
        parent.Editor!.Description = "Updated description";
        parent.Editor.SetupDatabaseName = "Updated setup";
        parent.Editor.SetupFunctionName = "Updated generator";
        Assert.Equal("", fields.DescriptionOverride.EffectiveValue);
        Assert.Equal("Run setup", fields.SetupDatabaseOverride.EffectiveValue);
        Assert.Equal("Updated generator", fields.SetupFunctionOverride.EffectiveValue);
        Assert.Equal("Overridden for this run", fields.DescriptionOverride.SourceLabel);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var reopened = Open();
        var runFields = reopened.Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.True(runFields.DescriptionOverride.IsOverridden);
        Assert.Equal("", runFields.DescriptionOverride.EffectiveValue);
        Assert.True(runFields.SetupDatabaseOverride.IsOverridden);
        Assert.True(runFields.SetupFunctionOverride.IsInherited);
        runFields.DescriptionOverride.IsOverridden = false;
        runFields.SetupDatabaseOverride.IsOverridden = false;
        Assert.Equal("Updated description", runFields.DescriptionOverride.EffectiveValue);
        Assert.Equal("Updated setup", runFields.SetupDatabaseOverride.EffectiveValue);
        await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
        var savedInputs = RunDto.fromDomain(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0]);
        Assert.Null(savedInputs.description);
        Assert.Null(savedInputs.setupDatabaseName);
        Assert.Null(savedInputs.setupFunctionName);
    }

    [Fact]
    public void Override_toggling_keeps_the_draft_override_and_parent_changes_refresh_only_inherited_values()
    {
        var page = Open(); var parent = CreateProject(page, true); var run = CreateRun(page, true);
        var field = run.Editor!.SetupFunctionOverride;
        field.IsOverridden = true;
        Assert.Equal("Generate", field.EffectiveValue);
        field.EffectiveValue = "Run generator";
        field.IsOverridden = false;
        parent.Editor!.SetupFunctionName = "New parent generator";
        parent.Name = "Renamed project";
        Assert.Equal("New parent generator", field.EffectiveValue);
        Assert.Equal("Inherited from Renamed project", field.SourceLabel);
        field.IsOverridden = true;
        Assert.Equal("Run generator", field.EffectiveValue);
        Assert.Equal("Overridden for this run", field.SourceLabel);
        field.EffectiveValue = "";
        Assert.NotEmpty(field.Error);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        field.IsOverridden = false;
        Assert.Empty(field.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Invalid_key_rows_and_missing_values_block_save_and_removal_restores_validity()
    {
        var page = Open(); var parent = CreateProject(page, false); var run = CreateRun(page, false);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var file = WorkspaceDefinitionFiles.Require(Root, "workspace"); var before = File.ReadAllBytes(file);
        parent = page.Roots[0].Children[0].Children[0]; run = parent.Children[0];
        parent.Editor!.AddOutputQueryKeyCommand.Execute(null);
        Assert.NotEmpty(parent.Editor.OutputQueryKeysError);
        parent.Editor.OutputQueryKeys.Last().Text = "Seed";
        Assert.Contains("unique", parent.Editor.OutputQueryKeysError);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync(); Assert.Equal(before, File.ReadAllBytes(file));
        parent.Editor.OutputQueryKeys.Last().RemoveCommand.Execute(null);
        Assert.Empty(page.Error);
        run.Editor!.OutputParameters[0].Values[0].RemoveCommand.Execute(null);
        Assert.Contains("at least one value", run.Editor.OutputParameters[0].Error);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        run.Editor.OutputParameters[0].AddValueCommand.Execute(null);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null)); // An explicitly blank parameter value is valid.
        run.Editor.AddOutputParameterCommand.Execute(null);
        run.Editor.OutputParameters.Last().Key = "Seed";
        Assert.Contains("unique", run.Editor.OutputParametersError);
        run.Editor.OutputParameters.Last().RemoveCommand.Execute(null);
        Assert.Empty(page.Error);
        page.DiscardChangesCommand.Execute(null);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal("7", page.Roots[0].Children[0].Children[0].Children[0].Editor!.OutputParameters[0].Values[0].Text);
    }

    [Fact]
    public async Task Output_selections_preserve_order_and_can_be_removed_after_reopening()
    {
        var page = Open(); var parent = CreateProject(page, false);
        parent.Editor!.OutputTypes.Single(choice => choice.Value == outputDataType.SorterModelSet).IsSelected = true;
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.Equal(new[] { outputDataType.SorterSetEval, outputDataType.SorterModelSet }, WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].OutputDataTypes);
        var reopened = Open();
        var fields = reopened.Roots[0].Children[0].Children[0].Editor!;
        fields.OutputTypes.Single(choice => choice.Value == outputDataType.SorterSetEval).IsSelected = false;
        await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
        Assert.Equal(new[] { outputDataType.SorterModelSet }, WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].OutputDataTypes);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
