using System.IO;
using System.Text.Json;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceSaveValidationTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-save-validation-");
    private WorkspacePageViewModel Open()
    {
        var folder = Path.Combine(temporary.FullName, "Workspace"); Directory.CreateDirectory(folder);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(folder);
        return new(() => { }, _ => null, settings);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Databases_are_project_owned_and_absent_from_module_and_run_inputs(bool branch)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        module.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        module.Editor.ResultsDatabaseName = "Cannot define this on module";
        module.Editor.SetupDatabaseName = "Cannot define this on module";
        Assert.False(module.Editor.ShowDatabaseSettings);
        Assert.False(module.Editor.ResultsDatabaseOverride.CanOverride);
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        Assert.Equal("Results", project.Editor!.ResultsDatabaseName);
        Assert.True(project.Editor.ResultsDatabaseOverride.CanEditValue);
        Assert.False(project.Editor.ResultsDatabaseOverride.CanOverride);
        project.Editor.ResultsDatabaseName = "Project results";
        if (branch) project.Editor.SetupDatabaseName = "Project setup";
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        run.Editor!.ResultsDatabaseName = "Cannot override"; run.Editor.SetupDatabaseName = "Cannot override";
        Assert.Equal("Project results", run.Editor.ResultsDatabaseName);
        Assert.Same(project.Editor, run.Editor.ResultsDatabaseOverride.InheritedSource);
        Assert.False(run.Editor.SetupDatabaseOverride.CanOverride);
        if (branch) Assert.Equal("Project setup", run.Editor.SetupDatabaseName);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(page.WorkspaceFolder);
        var dto = ModuleDto.fromDomain(saved.Modules[0]);
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.configuration), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.localOverrides), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.projects[0].runs[0]), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Project results", saved.Modules[0].Projects[0].Runs[0].ResultsDatabaseName);
        if (branch) Assert.Equal("Project setup", saved.Modules[0].Projects[0].Runs[0].SetupDatabaseName.Value);
        var reopened = Open();
        var fields = reopened.Roots[0].Children[0].Children[0].Editor!;
        Assert.Equal("Project results", fields.ResultsDatabaseName);
        Assert.True(fields.IsReadOnly);
    }

    [Fact]
    public void Missing_functions_name_the_section_action_and_affected_project()
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Name = "Shared module";
        page.AddProjectCommand.Execute(null); var project = page.Selected!; project.Name = "SingleStage";
        var issue = Assert.Single(page.SaveIssues, value => value.Message.Contains("No results function"));
        Assert.Contains("SingleStage", issue.Location); Assert.Contains("Shared module", issue.Location);
        Assert.Equal("Results settings", issue.Section);
        Assert.Contains("Override on this module", issue.Message); Assert.Contains("catalog", issue.Message);
        Assert.DoesNotContain("ancestor", issue.Message);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        project.Editor!.Sections.ResultsSettings = false; page.Selected = page.Roots[0];
        page.ReviewSaveIssueCommand.Execute(issue);
        Assert.Same(project, page.Selected); Assert.True(project.Editor.Sections.ResultsSettings);
        project.Parent!.Editor!.ResultsFunctionName = "Evaluate"; project.Parent.Editor.SetupFunctionName = "Generate";
        Assert.Empty(page.SaveIssues); Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        project.Editor.ResultsDatabaseName = " ";
        var database = Assert.Single(page.SaveIssues);
        Assert.Contains("results database", database.Message); Assert.Contains("this project", database.Message);
        Assert.DoesNotContain("override", database.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace)]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    [InlineData(WorkspaceEntityKind.Run)]
    public async Task Invalid_names_are_explained_before_saving_and_keep_the_rejected_draft(WorkspaceEntityKind kind)
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(false));
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        var node = page.Roots[0].DescendantsAndSelf().Single(value => value.Kind == kind);
        var previousName = node.Name;
        node.NameInput = " ";
        Assert.Equal(" ", node.NameInput); Assert.Equal(previousName, node.Name);
        var issue = Assert.Single(page.SaveIssues);
        Assert.Same(node, issue.Node); Assert.Equal("Name", issue.Section); Assert.Contains("blank", issue.Message);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync(); Assert.NotEmpty(page.Error);
        node.NameInput = previousName;
        Assert.Empty(page.SaveIssues); Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    [Fact]
    public void Parameter_validation_names_the_key_and_its_editor_section()
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadProject(WorkspaceHierarchyTests.EditorSettings(false)); page.AddProjectCommand.Execute(null);
        page.AddRunCommand.Execute(null);
        var editor = page.Selected!.Editor!;
        WorkspaceParameterSetsPreviewTests.AddSpan(editor, "ChannelCount", false, "not an integer");
        var issue = Assert.Single(page.SaveIssues);
        Assert.Equal("Results parameter spans", issue.Section);
        Assert.Contains("ChannelCount", issue.Message); Assert.Contains("Whole number", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(issue.Location, page.SaveHint);
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
