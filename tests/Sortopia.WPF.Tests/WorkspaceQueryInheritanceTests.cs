using System.IO;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceQueryInheritanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runs_inherit_project_projections_and_cannot_change_them(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(RunExecutionTests.Settings(true));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Shared queries", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var first = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "First", family: hierarchyKind.Branch);
        var second = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Second", family: hierarchyKind.Branch);
        project.Children.Add(first); project.Children.Add(second);
        WorkspaceParameterSetsPreviewTests.AddSpan(first, "SortableDataFormat", setup, "Bools", "Ints");
        WorkspaceParameterSetsPreviewTests.AddSpan(second, "SortableDataFormat", setup, "BitVector512");
        WorkspaceOutputQuery Output(WorkspaceTreeNode node) => Assert.Single(setup ? node.Editor!.SetupQueryOutputs : node.Editor!.ResultsQueryOutputs);
        WorkspaceQueryKeyChoice Key(WorkspaceTreeNode node) => Assert.Single(Output(node).Keys);
        Assert.True(project.Editor!.CanEditQuerySelection);
        foreach (var run in new[] { first, second })
        {
            Assert.False(run.Editor!.CanEditQuerySelection);
            Assert.Contains("Inherited from project ‘Shared queries’", run.Editor.QueryConfigurationHint);
            Assert.Contains("Read-only", run.Editor.QueryConfigurationHint);
            Assert.False(Key(run).CanChange);
            Key(run).Selected = true;
            Assert.False(Key(run).Selected);
            Assert.False(Key(project).Selected);
            Assert.Contains("Edit query projections on the project", Output(run).Summary);
        }
        Key(project).Selected = true;
        foreach (var run in new[] { first, second })
        {
            Assert.True(Key(run).Selected);
            Key(run).Selected = false;
            Assert.True(Key(run).Selected);
            Assert.True(Key(project).Selected);
        }
        Key(project).Selected = false;
        Assert.False(Key(first).Selected); Assert.False(Key(second).Selected);
        project.Editor.Freeze();
        Assert.False(project.Editor.CanEditQuerySelection);
        Assert.False(Key(first).CanChange);
        Assert.Contains("resume editing", first.Editor!.QueryConfigurationHint);
        Assert.Contains("Inherited from project", first.Editor.QueryConfigurationHint);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Project_link_opens_matching_projection_settings_and_preserves_unsaved_run_inputs(bool setup, bool sealedProject)
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-query-inheritance-");
        try
        {
            var page = WorkspaceRunExecutionTests.Create(temporary.FullName, setup);
            var run = page.Selected!;
            var project = run.Parent!;
            var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SortableDataFormat", setup, "Bools", "Ints");
            if (sealedProject) project.Editor!.Freeze();
            var target = setup ? "setupOutputs" : "resultsTypes";
            Assert.Same(project.Editor, page.InspectorItems.Single(item => item.Key == target).Source);
            page.SelectConfigurationItem(setup ? "setupSpans" : "resultsSpans");
            Assert.True(page.EditProjectQueryProjectionsCommand.CanExecute(null));
            page.EditProjectQueryProjectionsCommand.Execute(target);
            Assert.Same(project, page.Selected);
            Assert.Equal(target, page.ActiveItemKey);
            Assert.Equal(!sealedProject, project.Editor!.CanEditQuerySelection);
            Assert.Same(span, Assert.Single(setup ? run.Editor!.SetupParameters : run.Editor!.ResultsParameters));
            Assert.Equal(new[] { "Bools", "Ints" }, span.Values.Select(value => value.Text));
            Assert.True(page.HasUnsavedChanges);
            Assert.Empty(Directory.GetFiles(page.WorkspaceFolder, "*.msgpack", SearchOption.AllDirectories));
        }
        finally { temporary.Delete(true); }
    }
}
