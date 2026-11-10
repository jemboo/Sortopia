using System.IO;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceInspectorTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-inspector-");
    private WorkspacePageViewModel Open(bool branch)
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(temporary.FullName);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        return page;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Summary_tracks_values_errors_sources_and_remembers_the_item_for_each_entity(bool branch)
    {
        var page = Open(branch); var module = page.Selected!;
        Assert.DoesNotContain(page.InspectorItems, item => item.Key == "resultsDatabase");
        Assert.Equal(branch, page.InspectorItems.Any(item => item.Key == "setupFunction"));
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        var function = page.InspectorItems.Single(item => item.Key == "resultsFunction");
        Assert.Same(module.Editor, function.Source);
        page.SelectConfigurationItemCommand.Execute(function);
        Assert.Equal("resultsFunction", page.ActiveItemKey); Assert.True(function.IsActive);
        project.Editor!.ResultsFunctionName = "";
        Assert.True(function.HasError); Assert.Equal("Needs attention", function.Status);
        Assert.Same(function, page.InspectorItems.Single(item => item.Key == "resultsFunction"));
        var issue = page.SaveIssues.First(issue => ReferenceEquals(issue.Node, project) && issue.Message == function.Error);
        page.Selected = module; page.ReviewSaveIssueCommand.Execute(issue);
        Assert.Same(project, page.Selected); Assert.Equal("resultsFunction", page.ActiveItemKey);
        project.Editor.ResultsFunctionOverride.IsOverridden = false;
        function = page.InspectorItems.Single(item => item.Key == "resultsFunction");
        Assert.False(function.HasError);
        page.SelectInspectorSourceCommand.Execute(function);
        Assert.Same(module, page.Selected); Assert.Equal("resultsFunction", page.ActiveItemKey);
        page.Selected = project; Assert.Equal("resultsFunction", page.ActiveItemKey);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        page.SelectConfigurationItem("resultsSpans");
        WorkspaceParameterSetsPreviewTests.AddSpan(run.Editor!, "StageLength", false, "1", "2");
        Assert.Contains("2 parameter set", page.InspectorItems.Single(item => item.Key == "resultsPreview").Value);
        Assert.Contains("StageLength", page.InspectorItems.Single(item => item.Key == "resultsSpans").Value);
        page.Selected = project; page.Selected = run;
        Assert.Equal("resultsSpans", page.ActiveItemKey);
        page.SelectConfigurationItem("missing-key"); Assert.Equal("resultsSpans", page.ActiveItemKey);
        page.Selected = page.Roots[0]; Assert.Equal(new[] { "name", "location" }, page.InspectorItems.Select(item => item.Key));
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
