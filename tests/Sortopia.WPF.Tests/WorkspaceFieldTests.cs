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
        node.Parent!.Editor!.ResultsFunctionName = "Evaluate";
        fields.AddOutputTypeCommand.Execute(null);
        fields.AddResultsQueryKeyCommand.Execute(null);
        fields.ResultsQueryKeys.Single().Text = "Seed";
        if (branch)
        {
            fields.SetupDatabaseName = "Setup";
            node.Parent!.Editor!.SetupFunctionName = "Generate";
            WorkspaceHierarchyTests.AddSetupSource(node);
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
        fields.AddMissingResultsParametersCommand.Execute(null);
        var output = fields.ResultsParameters.Single();
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
        var values = run.Editor!.ResultsParameters.Single();
        values.Values[0].Text = "7,11";
        foreach (var value in new[] { " padded value ", "", "α", "first line\nsecond line" })
        { values.AddValueCommand.Execute(null); values.Values.Last().Text = value; }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var parent = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0];
        var saved = parent.Runs.Single();
        Assert.Equal(new[] { "7,11", " padded value ", "", "α", "first line\nsecond line" }, saved.ResultsParameterSpans.Single().Values);
        Assert.Equal("Shared description", saved.Description);
        Assert.Null(RunDto.fromDomain(saved).descriptionOverride);
        Assert.Equal(new[] { sortopiaDataType.NewSorterSetEval("") }, parent.ResultsSortopiaDataTypes);
        var reopened = Open().Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.True(reopened.DescriptionOverride.IsInherited);
        Assert.Equal("Inherited from Project defaults", reopened.DescriptionOverride.SourceLabel);
        Assert.Equal("SorterSetEval", Assert.Single(reopened.InheritedOutputTypes).kind);
        if (branch)
        {
            Assert.True(reopened.SetupDatabaseOverride.IsInherited);
            Assert.Equal("Generate", reopened.SetupFunctionOverride.EffectiveValue);
            Assert.Equal("8", reopened.SetupParameters.Single().Values.Single().Text);
        }
    }

    [Fact]
    public async Task Blank_description_overrides_and_project_databases_survive_reopening()
    {
        var page = Open(); var parent = CreateProject(page, true); var run = CreateRun(page, true);
        var fields = run.Editor!;
        fields.DescriptionOverride.IsOverridden = true;
        fields.DescriptionOverride.EffectiveValue = "";
        fields.SetupDatabaseOverride.IsOverridden = true;
        fields.SetupDatabaseOverride.EffectiveValue = "Run setup";
        parent.Editor!.Description = "Updated description";
        parent.Editor.SetupDatabaseName = "Updated setup";
        parent.Parent!.Editor!.SetupFunctionName = "Updated generator";
        Assert.Equal("", fields.DescriptionOverride.EffectiveValue);
        Assert.Equal("Updated setup", fields.SetupDatabaseOverride.EffectiveValue);
        Assert.Equal("Updated generator", fields.SetupFunctionOverride.EffectiveValue);
        Assert.Equal("Overridden for this run", fields.DescriptionOverride.SourceLabel);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var reopened = Open();
        var runFields = reopened.Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.True(runFields.DescriptionOverride.IsOverridden);
        Assert.Equal("", runFields.DescriptionOverride.EffectiveValue);
        Assert.False(runFields.SetupDatabaseOverride.CanOverride);
        Assert.True(runFields.SetupDatabaseOverride.IsInherited);
        Assert.True(runFields.SetupFunctionOverride.IsInherited);
        runFields.DescriptionOverride.IsOverridden = false;
        runFields.SetupDatabaseOverride.IsOverridden = false;
        Assert.Equal("Updated description", runFields.DescriptionOverride.EffectiveValue);
        Assert.Equal("Updated setup", runFields.SetupDatabaseOverride.EffectiveValue);
        await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
        var savedInputs = RunDto.fromDomain(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0]);
        Assert.Null(savedInputs.descriptionOverride);
        Assert.Null(typeof(runDto).GetProperty("setupDatabaseNameOverride"));

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
        parent.Parent!.Editor!.SetupFunctionName = "New parent generator";
        parent.Name = "Renamed project";
        Assert.Equal("New parent generator", field.EffectiveValue);
        Assert.Same(parent.Parent.Editor, field.InheritedSource);
        field.IsOverridden = true;
        Assert.Equal("New parent generator", field.EffectiveValue);
        Assert.False(field.CanOverride);
        field.EffectiveValue = "";
        Assert.Empty(field.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        field.IsOverridden = false;
        Assert.Empty(field.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    [Fact]
    public async Task Invalid_key_rows_and_missing_values_block_save_and_removal_restores_validity()
    {
        var page = Open(); var parent = CreateProject(page, false); var run = CreateRun(page, false);
        parent.Editor!.AddResultsQueryKeyCommand.Execute(null);
        Assert.NotEmpty(parent.Editor.ResultsQueryKeysError);
        parent.Editor.ResultsQueryKeys.Last().Text = "Seed";
        Assert.Contains("unique", parent.Editor.ResultsQueryKeysError);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync(); Assert.Null(WorkspaceDefinitionFiles.Find(Root, "workspace"));
        parent.Editor.ResultsQueryKeys.Last().RemoveCommand.Execute(null);
        Assert.Empty(page.Error);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        parent = page.Roots[0].Children[0].Children[0]; run = parent.Children[0];
        run.Editor!.ResultsParameters[0].Values[0].RemoveCommand.Execute(null);
        Assert.Contains("at least one value", run.Editor.ResultsParameters[0].Error);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        run.Editor.ResultsParameters[0].AddValueCommand.Execute(null);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null)); // An explicitly blank parameter value is valid.
        run.Editor.AddResultsParameterCommand.Execute(null);
        run.Editor.ResultsParameters.Last().Key = "Seed";
        Assert.Contains("unique", run.Editor.ResultsParametersError);
        run.Editor.ResultsParameters.Last().RemoveCommand.Execute(null);
        Assert.Empty(page.Error);
        page.DiscardChangesCommand.Execute(null);
        Assert.False(page.HasUnsavedChanges);
        Assert.Equal("7", page.Roots[0].Children[0].Children[0].Children[0].Editor!.ResultsParameters[0].Values[0].Text);
    }

    [Fact]
    public async Task Output_selections_preserve_order_and_can_be_removed_after_reopening()
    {
        var page = Open(); var parent = CreateProject(page, false);
        parent.Editor!.AddOutputTypeCommand.Execute(null);
        parent.Editor.OutputTypes.Last().Kind = "SorterModelSet";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.Equal(new[] { sortopiaDataType.NewSorterSetEval(""), sortopiaDataType.NewSorterModelSet("") }, WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].ResultsSortopiaDataTypes);
        var reopened = Open();
        var fields = reopened.Roots[0].Children[0].Children[0].Editor!;
        fields.OutputTypes.Single(choice => choice.Kind == "SorterSetEval").RemoveCommand.Execute(null);
        await reopened.SaveWorkspaceAsync(); Assert.Empty(reopened.Error);
        Assert.Equal(new[] { sortopiaDataType.NewSorterModelSet("") }, WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].ResultsSortopiaDataTypes);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Query_key_choices_follow_phase_defaults_and_preserve_custom_keys_after_reopening(bool branch, bool onProject)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var ancestor = page.Selected!;
        ancestor.Editor!.LoadProject(WorkspaceHierarchyTests.EditorSettings(branch));
        var inherited = WorkspaceParameterInheritanceTests.AddDefault(ancestor.Editor, "InheritedResult", "value");
        if (branch) WorkspaceParameterInheritanceTests.AddDefault(ancestor.Editor, "InheritedSetup", "value", true);
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        if (onProject) page.AddProjectCommand.Execute(null);
        var owner = page.Selected!; var editor = owner.Editor!;
        WorkspaceParameterInheritanceTests.AddDefault(editor, "LocalResult", "value");
        editor.AddResultsQueryKeyCommand.Execute(null);
        var results = editor.ResultsQueryKeys.Single(); results.Text = "LocalResult";
        Assert.Contains("ChannelCount", results.KeyChoices);
        Assert.Contains("InheritedResult", results.KeyChoices); Assert.Contains("LocalResult", results.KeyChoices);
        Assert.DoesNotContain("InheritedSetup", results.KeyChoices);
        WorkspaceParameterInheritanceTests.AddDefault(editor, "InheritedResult", "overridden");
        Assert.Single(results.KeyChoices, key => key == "InheritedResult");
        Assert.Equal(results.KeyChoices.Order(StringComparer.Ordinal), results.KeyChoices);
        inherited.Key = "RenamedResult";
        Assert.Contains("RenamedResult", results.KeyChoices); Assert.Equal("LocalResult", results.Text);
        editor.ResultsParameterDefaults.Single(row => row.Key == "InheritedResult").RemoveCommand.Execute(null);
        Assert.DoesNotContain("InheritedResult", results.KeyChoices);
        results.Text = "CustomWithoutDefault";
        Assert.Contains(results.Text, results.KeyChoices);

        if (branch)
        {
            var local = WorkspaceParameterInheritanceTests.AddDefault(editor, "LocalSetup", "value", true);
            WorkspaceHierarchyTests.AddSetupSource(owner);
            var setup = editor.SetupDataTypes.Single().QueryKeys.Single(); setup.Text = "LocalSetup";
            Assert.Contains("ChannelCount", setup.KeyChoices); Assert.Contains("InheritedSetup", setup.KeyChoices);
            Assert.Contains("LocalSetup", setup.KeyChoices); Assert.DoesNotContain("LocalResult", setup.KeyChoices);
            Assert.DoesNotContain("InheritedSetup", results.KeyChoices); Assert.DoesNotContain("LocalSetup", results.KeyChoices);
            WorkspaceParameterInheritanceTests.AddDefault(ancestor.Editor, "AddedSetupLater", "value", true);
            Assert.Contains("AddedSetupLater", setup.KeyChoices);
            local.RemoveCommand.Execute(null);
            Assert.Equal("LocalSetup", setup.Text); Assert.Contains("LocalSetup", setup.KeyChoices);
        }
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var reopened = Open().Roots[0].Children[0].Children[0];
        if (onProject) reopened = reopened.Children.Single(node => node.Id == owner.Id);
        var restored = reopened.Editor!;
        Assert.Equal("CustomWithoutDefault", restored.ResultsQueryKeys.Single().Text);
        Assert.Contains("RenamedResult", restored.ResultsQueryKeys.Single().KeyChoices);
        Assert.Contains("LocalResult", restored.ResultsQueryKeys.Single().KeyChoices);
        if (branch)
        {
            var setup = restored.SetupDataTypes.Single().QueryKeys.Single();
            Assert.Equal("LocalSetup", setup.Text); Assert.Contains("InheritedSetup", setup.KeyChoices);
            Assert.Contains("AddedSetupLater", setup.KeyChoices);
        }
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
