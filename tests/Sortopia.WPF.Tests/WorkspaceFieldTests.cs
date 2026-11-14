using System.IO;
using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
namespace Sortopia.WPF.Tests;
public sealed class WorkspaceFieldTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-fields-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }
    private static WorkspaceTreeNode CreateProject(WorkspacePageViewModel page, bool branch)
    {
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings(branch));
        page.AddProjectCommand.Execute(null); return page.Selected!;
    }
    private static WorkspaceTreeNode CreateRun(WorkspacePageViewModel page)
    {
        page.AddRunCommand.Execute(null);
        var run = page.Selected!; WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false);
        if (run.IsBranch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        return run;
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task Fields_create_typed_runs_with_inherited_catalog_contracts(bool branch)
    {
        var page = Open(); CreateProject(page, branch); var run = CreateRun(page);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null), page.Error);
        var span = run.Editor!.ResultsParameters.Single(row => row.Key == "ChannelCount");
        span.Values[0].Text = "7"; span.AddValueCommand.Execute(null); span.Values[1].Text = "11";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0];
        Assert.Equal(new[] { "7", "11" }, saved.ResultsParameterSpans.Single(row => row.KeyName == "ChannelCount").Values);
        Assert.Equal("Keep every setting: α", saved.Description);
        Assert.Null(RunDto.fromDomain(saved).descriptionOverride);
        Assert.Equal(ResultsFunctionNames.standardSortableTests, saved.ResultsFunctionName);
        Assert.Equal(new[] { sortopiaDataType.NewSortableTests("") }, saved.ResultsSortopiaDataTypes);
    }
    [Fact]
    public async Task Blank_description_and_project_owned_databases_survive_reopening()
    {
        var page = Open(); var parent = CreateProject(page, true); var run = CreateRun(page);
        parent.Editor!.Description = "Parent description"; parent.Editor.SetupDatabaseName = "Local setup";
        run.Editor!.DescriptionOverride.IsOverridden = true; run.Editor.DescriptionOverride.EffectiveValue = "";
        Assert.False(run.Editor.SetupDatabaseOverride.CanOverride);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var reopened = Open().Roots[0].Children[0].Children[0].Children[0].Editor!;
        Assert.Equal("", reopened.Description); Assert.Equal("Local setup", reopened.SetupDatabaseName);
        reopened.DescriptionOverride.IsOverridden = false; Assert.Equal("Parent description", reopened.Description);
    }
    [Fact]
    public void Override_toggling_preserves_local_description_and_refreshes_inherited_values()
    {
        var page = Open(); var parent = CreateProject(page, false); var run = CreateRun(page);
        run.Editor!.DescriptionOverride.IsOverridden = true; run.Editor.Description = "Local";
        run.Editor.DescriptionOverride.IsOverridden = false; parent.Editor!.Description = "Changed parent";
        Assert.Equal("Changed parent", run.Editor.Description);
        run.Editor.DescriptionOverride.IsOverridden = true; Assert.Equal("Local", run.Editor.Description);
    }
    [Fact]
    public async Task Invalid_span_values_allow_draft_save_but_block_preview_until_corrected()
    {
        var page = Open(); CreateProject(page, false); var run = CreateRun(page);
        var row = run.Editor!.ResultsParameters.Single(row => row.Key == "ChannelCount"); row.Values[0].Text = "7,11";
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null)); Assert.NotEmpty(run.Editor.ResultsParameterSetsPreview.Error);
        row.Values[0].Text = "16"; await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Outputs_and_query_keys_have_no_local_override_or_editor(bool branch)
    {
        var page = Open(); var project = CreateProject(page, branch); var run = CreateRun(page);
        Assert.False(project.Editor!.OutputTypesOverride.CanOverride); Assert.False(run.Editor!.OutputTypesOverride.CanOverride);
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsDataTypes"));
        Assert.Null(typeof(moduleOverridesDto).GetProperty("resultsQueryParameterKeys"));
        Assert.Null(typeof(WorkspaceConfigurationViewModel).GetProperty("AddOutputTypeCommand"));
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, run.Editor.ResultsParameters[0].KeyChoices);
    }
    public void Dispose() => temporary.Delete(true);
}
