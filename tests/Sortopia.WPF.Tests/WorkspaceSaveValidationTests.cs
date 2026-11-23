using System.IO;
using System.Text.Json;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
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
        module.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(branch));
        module.Editor.ResultsDatabaseName = "Cannot define this on module";
        module.Editor.SetupDatabaseName = "Cannot define this on module";
        Assert.False(module.Editor.ShowDatabaseSettings);
        Assert.False(module.Editor.ResultsDatabaseField.CanEditValue);
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        Assert.Equal("Results", project.Editor!.ResultsDatabaseName);
        Assert.True(project.Editor.ResultsDatabaseField.CanEditValue);
        Assert.Null(project.Editor.ResultsDatabaseField.InheritedSource);
        project.Editor.ResultsDatabaseName = "Project results";
        if (branch) project.Editor.SetupDatabaseName = "Project setup";
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        run.Editor!.ResultsDatabaseName = "Cannot override"; run.Editor.SetupDatabaseName = "Cannot override";
        Assert.Equal("Project results", run.Editor.ResultsDatabaseName);
        Assert.Same(project.Editor, run.Editor.ResultsDatabaseField.InheritedSource);
        Assert.False(run.Editor.SetupDatabaseField.CanEditValue);
        if (branch) Assert.Equal("Project setup", run.Editor.SetupDatabaseName);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadWorkspace(page.WorkspaceFolder);
        var selected = saved.ResolveRun(run.Id).Run;
        var dto = saved.CreateEditDocument().Modules[0];
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.Definition.inheritedDefaults), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.Definition.localOverrides), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database", JsonSerializer.Serialize(dto.Projects[0].Runs[0]), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Project results", selected.ResultsDatabaseName);
        if (branch) Assert.Equal("Project setup", selected.SetupDatabaseName.Value);
        var reopened = Open();
        var fields = reopened.Roots[0].Children[0].Children[0].Editor!;
        Assert.Equal("Project results", fields.ResultsDatabaseName);
        Assert.False(fields.IsReadOnly);
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
        Assert.Contains("Define on this module", issue.Message); Assert.Contains("catalog", issue.Message);
        Assert.DoesNotContain("ancestor", issue.Message);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        project.Editor!.Sections.ResultsSettings = false; page.Selected = page.Roots[0];
        page.ReviewSaveIssueCommand.Execute(issue);
        Assert.Same(project, page.Selected); Assert.True(project.Editor.Sections.ResultsSettings);
        project.Parent!.Editor!.ResultsFunctionName = "sortable-tests.standard"; project.Parent.Editor.SetupFunctionName = "sortable-tests-with-prefix.standard";
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
        page.Selected!.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
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
        page.Selected!.Editor!.LoadFixtureSettings(WorkspaceHierarchyTests.EditorSettings(false)); page.AddProjectCommand.Execute(null);
        page.AddRunCommand.Execute(null);
        var editor = page.Selected!.Editor!;
        WorkspaceParameterSetsPreviewTests.AddSpan(page.Selected!, "ChannelCount", false, "not an integer");
        var issue = Assert.Single(page.SaveIssues);
        Assert.Equal("Results parameter spans", issue.Section);
        Assert.Contains("ChannelCount", issue.Message); Assert.Contains("Whole number", issue.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Contains("draft", page.SaveHint);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace)]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    [InlineData(WorkspaceEntityKind.Run)]
    public void Validation_follows_the_selected_node_without_weakening_workspace_save_checks(WorkspaceEntityKind kind)
    {
        var page = Open(); page.AddModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        Assert.Empty(page.SaveIssues);
        var nodes = page.Roots[0].DescendantsAndSelf().ToArray();
        var invalid = nodes.Single(node => node.Kind == kind);
        var originalName = invalid.Name;
        invalid.NameInput = " ";
        page.Selected = nodes.First(node => node != invalid);
        Assert.True(page.HasSaveIssues);
        Assert.True(invalid.HasValidationIssues);
        Assert.True(invalid.NeedsAttention);
        Assert.All(nodes.Where(node => node != invalid), node => Assert.False(node.HasValidationIssues));
        var affected = new HashSet<WorkspaceTreeNode>();
        for (var node = invalid; node is not null; node = node.Parent) affected.Add(node);
        Assert.All(nodes, node => Assert.Equal(affected.Contains(node), node.NeedsAttention));
        Assert.Empty(page.SelectedSaveIssues);
        Assert.False(page.HasSelectedSaveIssues);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        foreach (var node in nodes) node.IsExpanded = false;
        page.ReviewNodeErrorsCommand.Execute(page.Roots[0]);
        Assert.Same(invalid, page.Selected);
        for (var parent = invalid.Parent; parent is not null; parent = parent.Parent) Assert.True(parent.IsExpanded);
        Assert.Same(invalid, Assert.Single(page.SelectedSaveIssues).Node);
        Assert.True(page.HasSelectedSaveIssues);
        invalid.NameInput = originalName;
        Assert.False(invalid.HasValidationIssues);
        Assert.False(invalid.NeedsAttention);
        Assert.All(nodes, node => Assert.False(node.NeedsAttention));
        Assert.Empty(page.SelectedSaveIssues);
        Assert.False(page.HasSelectedSaveIssues);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        page.Selected = null;
        Assert.Empty(page.SelectedSaveIssues);
    }

    [Fact]
    public void Tree_selection_shows_only_its_own_errors_and_refreshes_after_a_fix()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var model = Open(); model.AddModuleCommand.Execute(null);
                var module = model.Selected!;
                module.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
                model.AddProjectCommand.Execute(null); var first = model.Selected!;
                model.Selected = module; model.AddProjectCommand.Execute(null); var second = model.Selected!;
                var originalName = first.Name; first.NameInput = " ";
                model.Selected = model.Roots[0];
                var page = new Sortopia.WPF.Views.Pages.Workspace.WorkspacePage { DataContext = model };
                void Layout(int width = 1000)
                {
                    page.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                    page.Measure(new System.Windows.Size(width, 800));
                    page.Arrange(new System.Windows.Rect(0, 0, width, 800)); page.UpdateLayout();
                }
                Layout();
                var summary = (System.Windows.Controls.Border)page.FindName("SaveValidationSummary");
                var issues = (System.Windows.Controls.ItemsControl)page.FindName("SaveIssuesList");
                Assert.Equal(System.Windows.Visibility.Collapsed, summary.Visibility);
                Assert.Empty(issues.Items);
                var tree = (System.Windows.Controls.TreeView)page.FindName("WorkspaceTree");
                var rootItem = (System.Windows.Controls.TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(model.Roots[0]);
                var moduleItem = (System.Windows.Controls.TreeViewItem)rootItem.ItemContainerGenerator.ContainerFromItem(module);
                var firstItem = (System.Windows.Controls.TreeViewItem)moduleItem.ItemContainerGenerator.ContainerFromItem(first);
                var secondItem = (System.Windows.Controls.TreeViewItem)moduleItem.ItemContainerGenerator.ContainerFromItem(second);
                static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject parent)
                {
                    for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                    {
                        var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                        yield return child;
                        foreach (var descendant in Descendants(child)) yield return descendant;
                    }
                }
                System.Windows.Shapes.Path ErrorIcon(System.Windows.Controls.TreeViewItem item) =>
                    Descendants(item).OfType<System.Windows.Shapes.Path>().Single(shape => shape.Name == "NodeIconShape" && ReferenceEquals(shape.DataContext, item.DataContext));
                var warning = ((System.Windows.Media.SolidColorBrush)page.FindResource("Workspace.Warning")).Color;
                void CheckError(System.Windows.Shapes.Path icon, bool expected) => Assert.Equal(expected, ((System.Windows.Media.SolidColorBrush)icon.Stroke).Color == warning);
                var firstIndicator = ErrorIcon(firstItem);
                var secondIndicator = ErrorIcon(secondItem);
                var rootIndicator = ErrorIcon(rootItem);
                var moduleIndicator = ErrorIcon(moduleItem);
                var fill = Descendants(firstItem).OfType<System.Windows.Shapes.Path>().Single(shape => shape.Name == "NodeIconFill");
                Assert.DoesNotContain(Descendants(firstItem).OfType<System.Windows.FrameworkElement>(), element => element.Name == "NodeIconError");
                first.SetExecutionCounts(new(2, 0)); Layout();
                Assert.Equal(System.Windows.Visibility.Collapsed, fill.Visibility);
                first.SetExecutionCounts(new(2, 1)); Layout();
                Assert.Equal(System.Windows.Visibility.Visible, fill.Visibility);
                Assert.Equal(new System.Windows.Rect(0, 12, 24, 12), Assert.IsType<System.Windows.Media.RectangleGeometry>(fill.Clip).Rect);
                Assert.Equal(WorkspaceExecutionFill.Half, model.Roots[0].ExecutionFill);
                first.SetExecutionCounts(new(2, 2)); Layout();
                Assert.Equal(new System.Windows.Rect(0, 0, 24, 24), Assert.IsType<System.Windows.Media.RectangleGeometry>(fill.Clip).Rect);
                Assert.Equal(WorkspaceExecutionFill.Full, model.Roots[0].ExecutionFill);
                CheckError(firstIndicator, true);
                CheckError(secondIndicator, false);
                CheckError(rootIndicator, true);
                CheckError(moduleIndicator, true);
                moduleItem.IsExpanded = false; Layout();
                CheckError(moduleIndicator, true);
                Assert.Contains("descendant", module.IconDescription);
                first.NameInput = originalName; Layout();
                CheckError(moduleIndicator, false);
                CheckError(rootIndicator, false);
                first.NameInput = " "; Layout();
                CheckError(moduleIndicator, true);
                rootItem.IsExpanded = false; Layout();
                var rootIcon = Descendants(rootItem).OfType<System.Windows.Controls.Button>().Single(button => button.Name == "NodeIconButton" && ReferenceEquals(button.DataContext, rootItem.DataContext));
                var invoke = (System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(rootIcon)
                    .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)!;
                invoke.Invoke(); Layout(); Layout();
                Assert.True(rootItem.IsExpanded);
                Assert.True(moduleItem.IsExpanded);
                Assert.Same(first, model.Selected);
                Assert.True(firstItem.IsSelected);
                Assert.Equal("name", model.ActiveItemKey);
                Assert.Equal(System.Windows.Visibility.Visible, summary.Visibility);
                Assert.Same(first, Assert.IsType<WorkspaceSaveIssue>(Assert.Single(issues.Items.Cast<object>())).Node);
                secondItem.IsSelected = true; Layout();
                Assert.Same(second, model.Selected);
                Assert.Equal(System.Windows.Visibility.Collapsed, summary.Visibility);
                Assert.Empty(issues.Items);
                CheckError(firstIndicator, true);
                firstItem.IsSelected = true; Layout();
                Assert.Equal(System.Windows.Visibility.Visible, summary.Visibility);
                first.NameInput = originalName; Layout();
                Assert.Equal(System.Windows.Visibility.Collapsed, summary.Visibility);
                Assert.Empty(issues.Items);
                CheckError(firstIndicator, false);
                secondItem.IsSelected = true; Layout();
                first.NameInput = " "; Layout();
                CheckError(firstIndicator, true);
                Assert.Equal(System.Windows.Visibility.Collapsed, summary.Visibility);
                first.NameInput = originalName; Layout();
                CheckError(firstIndicator, false);
                CheckError(rootIndicator, false);
                var toolbar = (System.Windows.Controls.Grid)page.FindName("WorkspaceToolbar");
                var nodeToolbar = (Sortopia.WPF.Views.Shared.JustifiedWrapPanel)page.FindName("WorkspaceNodeToolbar");
                var path = (System.Windows.Controls.TextBox)page.FindName("WorkspaceFolderPath");
                Assert.InRange(path.ActualWidth, 350, 500);
                Assert.InRange(toolbar.ActualHeight, 32, 44);
                Assert.Null(page.FindName("InspectFilesButton"));
                Assert.Equal("_Back", ((System.Windows.Controls.Button)page.FindName("BackToSortopiaButton")).Content);
                void CheckAlignedRows()
                {
                    Assert.InRange(Math.Abs(toolbar.ActualWidth - nodeToolbar.ActualWidth), 0, 1);
                    var bounds = nodeToolbar.Children.Cast<System.Windows.FrameworkElement>()
                        .Where(child => child.Visibility == System.Windows.Visibility.Visible)
                        .Select(child => child.TransformToAncestor(nodeToolbar).TransformBounds(new System.Windows.Rect(child.RenderSize)));
                    foreach (var row in bounds.GroupBy(rect => Math.Round(rect.Top)))
                    {
                        Assert.InRange(row.Min(rect => rect.Left), 0, 1);
                        Assert.InRange(Math.Abs(row.Max(rect => rect.Right) - toolbar.ActualWidth), 0, 1);
                    }
                }
                CheckAlignedRows();
                Layout(640);
                Assert.InRange(toolbar.ActualHeight, 32, 44);
                CheckAlignedRows();
                foreach (var name in new[] { "WorkspaceFolderPath", "SaveWorkspaceButton", "DiscardChangesButton", "ExportWorkspaceButton", "BackToSortopiaButton" })
                {
                    var control = (System.Windows.FrameworkElement)page.FindName(name);
                    var bounds = control.TransformToAncestor(toolbar).TransformBounds(new System.Windows.Rect(control.RenderSize));
                    Assert.InRange(bounds.Right, 0, toolbar.ActualWidth);
                    Assert.InRange(bounds.Bottom, 0, toolbar.ActualHeight);
                }
                void Render(string name, int width)
                {
                    Layout(width);
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, 800, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(AppContext.BaseDirectory, name)); encoder.Save(stream);
                }
                first.NameInput = " "; moduleItem.IsExpanded = false;
                model.Selected = model.Roots[0]; model.AddBranchModuleCommand.Execute(null);
                model.Selected!.Name = "Branch module";
                model.Selected.SetExecutionCounts(new(3, 1));
                Render("workspace-tree-icons.png", 1000);
                Render("workspace-tree-icons-narrow.png", 640);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Parent_indicators_follow_multiple_errors_and_moves_between_branches()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var firstModule = page.Selected!;
        firstModule.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
        page.AddProjectCommand.Execute(null); var first = page.Selected!;
        page.Selected = firstModule; page.AddProjectCommand.Execute(null); var second = page.Selected!;
        page.Selected = page.Roots[0]; page.AddModuleCommand.Execute(null); var otherModule = page.Selected!;
        otherModule.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
        var firstName = first.Name;
        first.NameInput = " "; second.NameInput = " ";
        Assert.True(firstModule.HasDescendantIssues); Assert.False(otherModule.NeedsAttention);
        firstModule.Children.Remove(first); otherModule.Children.Add(first);
        Assert.True(firstModule.NeedsAttention); Assert.True(otherModule.NeedsAttention);
        firstModule.Children.Remove(second);
        Assert.False(firstModule.NeedsAttention); Assert.True(page.Roots[0].NeedsAttention);
        first.NameInput = firstName;
        Assert.False(otherModule.NeedsAttention); Assert.False(page.Roots[0].NeedsAttention);
    }

    [Fact]
    public void Error_navigation_skips_aggregate_warnings_and_visits_descendants_in_tree_order()
    {
        var page = Open(); page.AddModuleCommand.Execute(null); var module = page.Selected!;
        module.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(false));
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        page.Selected = module; page.AddProjectCommand.Execute(null); var second = page.Selected!;
        var runName = run.Name; var projectName = second.Name;
        run.NameInput = " "; second.NameInput = " "; module.NameInput = " ";
        project.IsExpanded = false; module.IsExpanded = false;
        page.ReviewNodeErrorsCommand.Execute(module);
        Assert.Same(run, page.Selected);
        Assert.True(project.IsExpanded); Assert.True(module.IsExpanded);
        Assert.False(project.HasOwnIssues);
        Assert.True(project.HasDescendantIssues);
        run.NameInput = runName;
        page.ReviewNodeErrorsCommand.Execute(module);
        Assert.Same(second, page.Selected);
        second.NameInput = projectName;
        page.ReviewNodeErrorsCommand.Execute(module);
        Assert.Same(module, page.Selected);
        Assert.Equal("name", page.ActiveItemKey);
    }
    public void Dispose() => temporary.Delete(recursive: true);
}
