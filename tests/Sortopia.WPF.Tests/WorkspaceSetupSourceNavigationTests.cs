using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceSetupSourceNavigationTests
{
    [Theory]
    [InlineData(WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Project)]
    [InlineData(WorkspaceEntityKind.Run)]
    public void Setup_grid_shows_missing_sources_and_opens_their_selectors_then_parameter_values(WorkspaceEntityKind kind)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Directory.CreateTempSubdirectory("Sortopia-setup-inputs-");
            Window? window = null;
            try
            {
                var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt"));
                settings.Save(Directory.CreateDirectory(Path.Combine(folder.FullName, "Workspace")).FullName);
                var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
                WorkspaceTreeNode Add(WorkspaceTreeNode parent, WorkspaceEntityKind nodeKind, string name, hierarchyKind family)
                {
                    var child = new WorkspaceTreeNode(nodeKind, name, family: family); parent.Children.Add(child); return child;
                }
                WorkspaceTreeNode Source(string name, string function)
                {
                    var owner = Add(model.Roots[0], WorkspaceEntityKind.Module, name, hierarchyKind.Root);
                    owner.Editor!.ResultsFunctionName = function;
                    return Add(owner, WorkspaceEntityKind.Project, name + " project", hierarchyKind.Root);
                }
                var models = Source("Models", ResultsFunctionNames.uniformMsuf4SimpleSorterModels);
                var sourceRun = Add(models, WorkspaceEntityKind.Run, "Source run", hierarchyKind.Root);
                WorkspaceParameterSetsPreviewTests.AddSpan(sourceRun, "ChannelCount", false, "16");
                WorkspaceParameterSetsPreviewTests.AddSpan(sourceRun, "Repl", false, "0", "1");
                models.Editor!.ResultsQueryOutputs.Single().Keys.Single(key => key.Key == "ChannelCount").Selected = true;
                models.Editor.ResultsQueryOutputs.Single().Keys.Single(key => key.Key == "Repl").Selected = true;
                var tests = Source("Tests", ResultsFunctionNames.standardSortableTests);
                var testsRun = Add(tests, WorkspaceEntityKind.Run, "Tests run", hierarchyKind.Root);
                WorkspaceParameterSetsPreviewTests.AddSpan(testsRun, "ChannelCount", false, "16");
                WorkspaceParameterSetsPreviewTests.AddSpan(testsRun, "SortableDataFormat", false, "Bools");
                foreach (var key in tests.Editor!.ResultsQueryOutputs.Single().Keys) key.Selected = true;
                var module = Add(model.Roots[0], WorkspaceEntityKind.Module, "Sgd", hierarchyKind.Branch);
                module.Editor!.SetupFunctionName = SetupFunctionNames.evaluationSource;
                var project = Add(module, WorkspaceEntityKind.Project, "Project", hierarchyKind.Branch);
                var run = Add(project, WorkspaceEntityKind.Run, "Run", hierarchyKind.Branch);
                var node = kind == WorkspaceEntityKind.Module ? module : kind == WorkspaceEntityKind.Project ? project : run;
                var owner = node.IsRun ? project : node;
                model.Selected = node; model.SelectConfigurationItem("setupFunction");
                var view = new WorkspacePage { DataContext = model };
                window = new Window { Content = view, Width = 1100, Height = 750, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                window.Show();
                var editor = (WorkspaceConfigurationEditor)view.FindName("ConfigurationFields");
                var table = (ItemsControl)editor.FindName("SetupFunctionParametersTable");
                void Draw() { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
                void Show(WorkspaceTreeNode selected) { model.Selected = selected; model.SelectConfigurationItem("setupFunction"); Draw(); }
                WorkspaceFunctionParameter Row(string name, string? requiredBy = null) => table.Items.Cast<WorkspaceFunctionParameter>()
                    .First(row => row.Name == name && (requiredBy is null || row.RequiredBy == requiredBy));
                void Click(string name, string? requiredBy = null)
                {
                    var container = (ContentPresenter)table.ItemContainerGenerator.ContainerFromItem(Row(name, requiredBy));
                    var button = (Button)table.ItemTemplate.FindName("OpenParameterValueButton", container);
                    Assert.True(button.IsEnabled); Assert.True(button.IsTabStop);
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); Draw();
                }
                ComboBox SourceInput(string type)
                {
                    var rows = (ItemsControl)editor.FindName("SetupDataTypesList");
                    var row = rows.Items.Cast<WorkspaceSetupDataTypeEntry>().Single(row => row.DataType.Kind == type);
                    return (ComboBox)rows.ItemTemplate.FindName("SetupSourceProjectInput", (ContentPresenter)rows.ItemContainerGenerator.ContainerFromItem(row));
                }
                CheckBox SourceToggle(string type)
                {
                    var rows = (ItemsControl)editor.FindName("SetupDataTypesList");
                    var row = rows.Items.Cast<WorkspaceSetupDataTypeEntry>().Single(row => row.DataType.Kind == type);
                    return (CheckBox)rows.ItemTemplate.FindName("SetupSourceOverrideCheckBox", (ContentPresenter)rows.ItemContainerGenerator.ContainerFromItem(row));
                }
                Draw();
                Assert.False(node.Editor!.FunctionsReady);
                Assert.Contains(model.InspectorItems, item => item.Key == "setupOutputs" && item.Group == "Setup");
                Assert.Equal(2, table.Items.Count); Assert.True(table.ActualHeight > 0);
                Assert.All(table.Items.Cast<WorkspaceFunctionParameter>(), row => Assert.Equal(WorkspaceParameterSupply.None, row.Supply));
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth), (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"setup-source-inputs-{kind}.png"))) encoder.Save(file);
                Click("SorterModelSet");
                Assert.Same(owner, model.Selected); Assert.Equal("setupOutputs", model.ActiveItemKey);
                Assert.True(((FrameworkElement)editor.FindName("SetupOutputsItem")).IsVisible);
                Assert.Same(SourceInput("SorterModelSet"), FocusManager.GetFocusedElement(window));
                Assert.Equal(owner.Editor!.IsModule ? "Define on this module" : "Define on this project", SourceToggle("SorterModelSet").Content);
                Assert.Equal("Define SorterModelSet source", new CheckBoxAutomationPeer(SourceToggle("SorterModelSet")).GetName());
                Assert.False(SourceToggle("SortableTests").IsChecked);
                Assert.Empty(owner.Editor!.SetupParameterDefaults); Assert.Empty(run.Editor!.SetupParameters);
                SourceInput("SorterModelSet").SelectedValue = models.Id; Draw();
                if (node.IsRun)
                    project.Editor!.SetupQueryOutputs.Single(output => output.Kind == "SorterModelSet").Keys.Single(key => key.Key == "ChannelCount").Selected = true;
                module.Editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
                Show(node); Assert.Equal("Models project", Row("SorterModelSet").Value);
                Assert.Equal(WorkspaceParameterSupply.Default, Row("SorterModelSet").Supply);
                // A source's parameters are usable immediately, before the other source is selected.
                Assert.Equal(4, table.Items.Count); Assert.Equal("Not set", Row("SortableTests").Value);
                Assert.Equal("Not set", Row("ChannelCount").Value);
                Assert.True(Row("ChannelCount").CanNavigate);
                Click("ChannelCount");
                Assert.Same(node, model.Selected); Assert.Equal(node.IsRun ? "setupSpans" : "setupDefaults", model.ActiveItemKey);
                Assert.Equal("ChannelCount", node.IsRun ? Assert.Single(node.Editor!.SetupParameters).Key : Assert.Single(node.Editor!.SetupParameterDefaults).Key);
                var focused = Assert.IsType<TextBox>(FocusManager.GetFocusedElement(window));
                Assert.Equal("TypedValueInput", focused.Name);
                Assert.True(editor.IsAncestorOf(focused));
                focused.Text = "16"; Draw();
                Show(node);
                Assert.Equal(node.IsRun ? "[16]" : "16", Row("ChannelCount").Value);
                Assert.Equal(node.IsRun ? WorkspaceParameterSupply.Span : WorkspaceParameterSupply.Default, Row("ChannelCount").Supply);
                bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth), (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor); encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"setup-partial-source-parameters-{kind}.png"))) encoder.Save(file);
                Click("SortableTests"); Assert.Same(SourceInput("SortableTests"), FocusManager.GetFocusedElement(window));
                SourceInput("SortableTests").SelectedValue = tests.Id; Draw();
                Show(node);
                Assert.Equal(new[] { "SorterModelSet", "ChannelCount", "Repl", "SortableTests", "ChannelCount", "SortableDataFormat" },
                    table.Items.Cast<WorkspaceFunctionParameter>().Select(row => row.Name));
                Assert.Equal(node.IsRun ? "[16]" : "16", Row("ChannelCount", "SorterModelSet").Value);
                Assert.Equal(node.IsRun ? "[16]" : "16", Row("ChannelCount", "SortableTests").Value);
                Assert.False(Row("ChannelCount").IsSourceInput);
                Click("ChannelCount", "SortableTests");
                Assert.Same(node, model.Selected); Assert.Equal(node.IsRun ? "setupSpans" : "setupDefaults", model.ActiveItemKey);
                Assert.Equal("ChannelCount", node.IsRun ? Assert.Single(node.Editor!.SetupParameters).Key : Assert.Single(node.Editor!.SetupParameterDefaults).Key);
                Assert.IsType<TextBox>(FocusManager.GetFocusedElement(window)).Text = "32"; Draw(); Show(node);
                Assert.All(table.Items.Cast<WorkspaceFunctionParameter>().Where(row => row.Name == "ChannelCount"),
                    row => Assert.Equal(node.IsRun ? "[32]" : "32", row.Value));
                bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth), (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor); encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"setup-source-groups-{kind}.png"))) encoder.Save(file);

                // The two checkboxes control independent choices, which may live on different ancestors.
                var testsOwner = owner == module ? project : module;
                var sharedTests = testsOwner.Editor!.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests");
                sharedTests.SourceOverride!.IsOverridden = true; sharedTests.SourceProjectId = tests.Id;
                model.Selected = owner; model.SelectConfigurationItem("setupOutputs"); Draw();
                var testsRow = owner.Editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests");
                var toggle = SourceToggle("SortableTests");
                Assert.Equal(owner.Editor.IsModule ? "Define on this module" : "Override on this project", toggle.Content);
                Assert.Equal(owner.Editor.IsModule ? "Define SortableTests source" : "Override SortableTests source", new CheckBoxAutomationPeer(toggle).GetName());
                Assert.True(toggle.IsChecked);
                ((IToggleProvider)new CheckBoxAutomationPeer(toggle).GetPattern(PatternInterface.Toggle)!).Toggle(); Draw();
                Assert.False(toggle.IsChecked); Assert.False(SourceInput("SortableTests").IsEnabled);
                Assert.True(SourceInput("SorterModelSet").IsEnabled);
                Assert.Equal(models.Id, owner.Editor.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId);
                if (owner != module) Assert.Contains("Sgd", testsRow.SourceOverride!.SourceLabel);
                bitmap = new RenderTargetBitmap((int)Math.Ceiling(editor.ActualWidth), (int)Math.Ceiling(editor.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(editor); encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(AppContext.BaseDirectory, $"setup-source-overrides-{kind}.png"))) encoder.Save(file);
                Show(run); Click("SortableTests");
                Assert.Same(testsOwner, model.Selected); Assert.Equal("setupOutputs", model.ActiveItemKey);
                Assert.Same(SourceInput("SortableTests"), FocusManager.GetFocusedElement(window));

                // Inherited source links select the node where the source choice is defined.
                Show(run); Click("SorterModelSet");
                Assert.Same(owner, model.Selected); Assert.Same(SourceInput("SorterModelSet"), FocusManager.GetFocusedElement(window));
                var source = owner.Editor.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet");
                source.SourceProjectId = Guid.NewGuid(); Show(run);
                Assert.Equal("Missing source project", Row("SorterModelSet").Value);
                Assert.Equal(WorkspaceParameterSupply.None, Row("SorterModelSet").Supply);
                Click("SorterModelSet"); Assert.Same(owner, model.Selected);
                Assert.Same(SourceInput("SorterModelSet"), FocusManager.GetFocusedElement(window));

                // Locked source selections remain inspectable without changing the captured settings.
                source.SourceProjectId = models.Id; owner.Editor.SetGeneratedParametersLock(true); Show(run);
                Click("SorterModelSet"); Assert.Same(owner, model.Selected);
                Assert.False(source.SourceOverride!.CanEditValue);
                var previews = (ItemsControl)editor.FindName("InheritedSetupDataTypesList");
                Assert.Contains(previews.Items.Cast<WorkspaceSetupDataTypeEntry>(), row => row.SourceProjectId == models.Id);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); folder.Delete(true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
