using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceFunctionParametersTests
{
    private static WorkspaceFunctionParameter Row(WorkspaceTreeNode node, string name, bool setup = false) =>
        (setup ? node.Editor!.SetupFunctionParameters : node.Editor!.ResultsFunctionParameters).Single(row => row.Name == name);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Values_follow_defaults_overrides_exclusions_and_run_spans(bool branch)
    {
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: family);
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        if (branch) module.Editor.SetupFunctionName = SetupFunctionNames.standardTests;
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child", family: family); module.Children.Add(child);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: family); child.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: family); project.Children.Add(run);
        Assert.Equal("Not set", Row(run, "ChannelCount").Value);
        Assert.Equal(WorkspaceParameterSupply.None, Row(run, "ChannelCount").Supply);
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var original = module.Editor.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount");
        original.Value.Text = "16";
        foreach (var node in new[] { module, child, project, run })
        {
            Assert.Equal("16", Row(node, "ChannelCount").Value);
            Assert.Equal(WorkspaceParameterSupply.Default, Row(node, "ChannelCount").Supply);
            Assert.Contains("Module", Row(node, "ChannelCount").Hint);
        }
        project.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").OverrideCommand!.Execute(null);
        var local = project.Editor.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount");
        local.Value.Text = "32";
        Assert.Equal("32", Row(run, "ChannelCount").Value);
        Assert.Contains("Project", Row(run, "ChannelCount").Hint);
        Assert.Equal("16", Row(child, "ChannelCount").Value);
        local.RemoveCommand.Execute(null);
        Assert.Equal("16", Row(run, "ChannelCount").Value);
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", false, "8", "16");
        Assert.Equal("[8, 16]", Row(run, "ChannelCount").Value);
        Assert.Equal(WorkspaceParameterSupply.Span, Row(run, "ChannelCount").Supply);
        Assert.Equal(WorkspaceParameterSupply.None, Row(project, "ChannelCount").Supply);
        span.Values[0].Text = "4";
        Assert.Equal("[4, 16]", Row(run, "ChannelCount").Value);
        span.Values.Clear();
        Assert.Equal("Not set", Row(run, "ChannelCount").Value);
        Assert.Equal(WorkspaceParameterSupply.None, Row(run, "ChannelCount").Supply);
        span.AddValueCommand.Execute(null); span.Values[0].Text = "8";
        Assert.Equal("[8]", Row(run, "ChannelCount").Value);
        Assert.Equal(WorkspaceParameterSupply.Span, Row(run, "ChannelCount").Supply);
        project.Editor.ExcludedResultsDefaults.Single().RestoreCommand.Execute(null);
        Assert.Equal("Default: 16; span: [8]", Row(run, "ChannelCount").Value);
        Assert.Contains("conflict", Row(run, "ChannelCount").Hint);
        span.RemoveCommand.Execute(null);
        original.Value.Text = " ";
        Assert.Equal(WorkspaceParameterSupply.None, Row(run, "ChannelCount").Supply);
        Assert.Equal("Not set", Row(run, "ChannelCount").Value);
    }

    [Fact]
    public void Required_rows_follow_function_changes_and_sealed_project_values_stay_fixed()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module");
        Assert.Empty(module.Editor!.ResultsFunctionParameters);
        Assert.Contains("Select a catalog function", module.Editor.ResultsFunctionParametersHint);
        module.Editor.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        project.Editor!.Freeze();
        var savedRows = project.Editor.ResultsFunctionParameters;
        module.Editor.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        Assert.Equal(new[] { "PrefixLibId", "SortableDataFormat" }, module.Editor.ResultsFunctionParameters.Select(row => row.Name).Order());
        Assert.Equal(savedRows, project.Editor.ResultsFunctionParameters);
        Assert.Equal(savedRows, run.Editor!.ResultsFunctionParameters);
    }

    [Fact]
    public void Setup_values_use_their_own_defaults_overrides_and_spans()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        module.Editor.SetupFunctionName = SetupFunctionNames.standardTests;
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "16", true);
        WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "32", false);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        Assert.Equal("16", Row(run, "ChannelCount", true).Value);
        project.Editor!.InheritedSetupDefaults.Single(row => row.Key == "ChannelCount").OverrideCommand!.Execute(null);
        project.Editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "64";
        Assert.Equal("64", Row(run, "ChannelCount", true).Value);
        Assert.Contains("Project", Row(run, "ChannelCount", true).Hint);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", true, "8", "16");
        Assert.Equal("[8, 16]", Row(run, "ChannelCount", true).Value);
        Assert.Equal(WorkspaceParameterSupply.Span, Row(run, "ChannelCount", true).Supply);
        Assert.Equal("32", Row(run, "ChannelCount").Value);
        Assert.Equal(WorkspaceParameterSupply.Default, Row(run, "ChannelCount").Supply);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adding_a_missing_project_default_replaces_its_exclusion_without_changing_the_ancestor(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
        module.Editor.SetupFunctionName = SetupFunctionNames.standardTests;
        var original = WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "16", setup);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults).Single().ExcludeCommand!.Execute(null);
        Assert.Equal(WorkspaceParameterSupply.None, Row(project, "ChannelCount", setup).Supply);
        Assert.True(project.Editor.PrepareMissingFunctionParameter("ChannelCount", setup));
        Assert.Empty(setup ? project.Editor.ExcludedSetupDefaults : project.Editor.ExcludedResultsDefaults);
        var added = Assert.Single(setup ? project.Editor.SetupParameterDefaults : project.Editor.ResultsParameterDefaults);
        Assert.Equal("ChannelCount", added.Key); Assert.Equal("", added.Value.Text);
        added.Value.Text = "8";
        Assert.Equal("8", Row(project, "ChannelCount", setup).Value); Assert.Equal("16", original.Value.Text);

        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", setup, "4");
        var conflict = Row(project, "ChannelCount", setup);
        Assert.Equal(WorkspaceParameterSupply.None, conflict.Supply);
        Assert.False(conflict.CanNavigate); Assert.Contains("project query key", conflict.NavigationHint);
        Assert.False(project.Editor.PrepareMissingFunctionParameter("ChannelCount", setup));
        Assert.Empty(setup ? project.Editor.SetupParameterDefaults : project.Editor.ResultsParameterDefaults);
        Assert.Equal("[4]", Row(run, "ChannelCount", setup).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Function_renders_two_columns_and_three_icons_and_refreshes_live(bool setup)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var phase = setup ? "Setup" : "Results";
                var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
                var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: family);
                module.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixMsuf4SimpleSorterModels;
                if (setup) module.Editor.SetupFunctionName = SetupFunctionNames.uniformMsuf4SimpleSorterModels;
                (setup ? module.Editor.AddRequiredSetupDefaultsCommand : module.Editor.AddRequiredResultsDefaultsCommand).Execute(null);
                var defaults = setup ? module.Editor.SetupParameterDefaults : module.Editor.ResultsParameterDefaults;
                foreach (var row in defaults.Where(row => row.Key is "OrthoGenRate" or "Repl" or "SorterCount").ToArray())
                    row.RemoveCommand.Execute(null);
                var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: family); module.Children.Add(project);
                var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: family); project.Children.Add(run);
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "OrthoGenRate", setup, "0", "0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "0.9", "1");
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "Repl", setup, "0", "1");
                var view = new WorkspaceConfigurationEditor { DataContext = run.Editor, ActiveItemKey = setup ? "setupFunction" : "resultsFunction" };
                var border = new Border { Background = new SolidColorBrush(Color.FromRgb(24, 26, 31)), Padding = new Thickness(20), Child = view };
                var table = (ItemsControl)view.FindName($"{phase}FunctionParametersTable");
                void Draw(int width)
                {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    border.Measure(new Size(width, double.PositiveInfinity));
                    border.Arrange(new Rect(0, 0, width, border.DesiredSize.Height)); border.UpdateLayout();
                }
                foreach (var width in new[] { 600, 360 })
                {
                    Draw(width);
                    Assert.Equal(Visibility.Visible, ((StackPanel)view.FindName($"{phase}FunctionItem")).Visibility);
                    Assert.True(table.ActualHeight > 0);
                    Assert.Equal(8, table.Items.Count);
                    Assert.Contains(table.Items.Cast<WorkspaceFunctionParameter>(), row => row.Name == (setup ? "ChannelCount" : "PrefixLibId"));
                    Assert.DoesNotContain(table.Items.Cast<WorkspaceFunctionParameter>(), row => row.Name == (setup ? "PrefixLibId" : "ChannelCount"));
                    for (var i = 0; i < table.Items.Count; i++)
                    {
                        var row = Assert.IsType<WorkspaceFunctionParameter>(table.Items[i]);
                        var container = (ContentPresenter)table.ItemContainerGenerator.ContainerFromIndex(i);
                        T Element<T>(string name) where T : FrameworkElement => (T)table.ItemTemplate.FindName(name, container);
                        Assert.Equal(2, Element<Grid>("FunctionParameterRow").ColumnDefinitions.Count);
                        Assert.Equal(row.Value, Element<TextBlock>("FunctionParameterValueText").Text);
                        var icon = Element<Grid>("ParameterSupplyIcon");
                        Assert.Equal(row.IconDescription, AutomationProperties.GetName(icon));
                        var geometryKey = row.Supply switch
                        {
                            WorkspaceParameterSupply.Default => "FixedParameterValueIcon",
                            WorkspaceParameterSupply.Span => "ParameterSpanIcon",
                            _ => "NoParameterValueIcon"
                        };
                        Assert.Equal(((Geometry)view.FindResource(geometryKey)).ToString(), Element<System.Windows.Shapes.Path>("ParameterSupplyShape").Data.ToString());
                        var text = Element<TextBlock>("FunctionParameterValueText");
                        Assert.True(text.TranslatePoint(new Point(text.ActualWidth, 0), icon).X <= 0);
                        Assert.True(icon.TranslatePoint(new Point(icon.ActualWidth, 0), table).X <= table.ActualWidth + 1);
                    }
                    var bitmap = new RenderTargetBitmap(width, (int)Math.Ceiling(border.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(border);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"{phase.ToLowerInvariant()}-function-parameters-{width}.png")); encoder.Save(file);
                }
                Assert.Equal(WorkspaceParameterSupply.None, Row(run, "SorterCount", setup).Supply);
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterCount", setup, "100"); Draw(600);
                var updated = table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "SorterCount");
                Assert.Equal("[100]", updated.Value); Assert.Equal(WorkspaceParameterSupply.Span, updated.Supply);
                defaults.Single(row => row.Key == "SelfSymGenRate").Value.Text = "0.25"; Draw(600);
                Assert.Equal("0.25", table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "SelfSymGenRate").Value);
                view.ActiveItemKey = setup ? "resultsFunction" : "setupFunction"; Draw(600);
                Assert.Equal(Visibility.Collapsed, ((StackPanel)view.FindName($"{phase}FunctionItem")).Visibility);
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
