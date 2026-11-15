using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionNameSelectorTests
{
    [Fact]
    public void Both_catalogs_describe_every_function_and_previews_use_the_correct_phase()
    {
        Assert.All(SetupFunctionCatalog.functions.Values, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Description)));
        Assert.All(ResultsFunctionCatalog.functions.Values, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Description)));
        var setup = FunctionPreview.Find(SetupFunctionNames.uniformMsceSorterModels, true)!;
        var results = FunctionPreview.Find(ResultsFunctionNames.uniformMsceSorterModels, false)!;
        Assert.Contains("setup database", setup.Description);
        Assert.Contains("results database", results.Description);
        Assert.Equal(setup.RequiredParameters, results.RequiredParameters);
        Assert.Contains(setup.RequiredParameters, parameter => parameter.Name == "ExcludeSelfCe" && parameter.ValueType == "Boolean");
        Assert.Equal(new[] { "SorterModelSet \"\"" }, setup.Outputs);
        Assert.Null(FunctionPreview.Find(SetupFunctionNames.evaluationSource, false));
        Assert.Null(FunctionPreview.Find("unavailable.saved-function", true));
    }

    [Fact]
    public void Hierarchy_supports_shared_prefixes_any_depth_and_functions_that_also_have_children()
    {
        var roots = FunctionNameNode.Build(["z", "sorter-model-set.uniform.mssi", "sorter-model-set.uniform.msce",
            "sorter-model-set.uniform.mssi", "a.b.c.d", "a.b"]);
        Assert.Equal(new[] { "a", "sorter-model-set", "z" }, roots.Select(node => node.Segment));
        var branch = Assert.Single(roots[0].Children);
        Assert.True(branch.IsFunction); Assert.Equal("a.b", branch.Path);
        Assert.Equal("a.b.c.d", Assert.Single(Assert.Single(branch.Children).Children).Path);
        var models = Assert.Single(roots[1].Children);
        Assert.False(models.IsFunction);
        Assert.Equal(new[] { "msce", "mssi" }, models.Children.Select(node => node.Segment));
        Assert.True(roots[2].IsFunction);
    }

    [Fact]
    public void Picker_navigates_without_mutating_until_a_function_is_chosen_and_keeps_catalogs_separate() => OnSta(() =>
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        var model = module.Editor!;
        model.SetupFunctionOverride.IsOverridden = true;
        model.ResultsFunctionOverride.IsOverridden = true;
        var view = new WorkspaceConfigurationEditor { DataContext = model, ActiveItemKey = "setupFunction" };
        using var surface = new TestWindow(view);
        Layout(view);
        FunctionNameSelector Picker(string name) => (FunctionNameSelector)((WorkspaceOverrideEditor)view.FindName(name)).FindName("FunctionNameInput");
        var setup = Picker("SetupFunctionOverrideEditor"); var results = Picker("ResultsFunctionOverrideEditor");
        Assert.True(setup.IsSetupCatalog); Assert.False(results.IsSetupCatalog);
        Assert.Equal(SetupFunctionCatalog.functions.Keys, setup.Choices);
        Assert.DoesNotContain(ResultsFunctionNames.sorterEval, setup.Choices);
        Assert.True(results.IsEnabled); Assert.Equal(ResultsFunctionCatalog.functions.Keys, results.Choices);
        var tree = (TreeView)setup.FindName("FunctionTree");
        var button = (Button)setup.FindName("ChooseFunction");
        var popup = (Popup)setup.FindName("PickerPopup");
        var details = (StackPanel)setup.FindName("FunctionDetails");
        var description = (TextBlock)setup.FindName("FunctionDescription");
        var parameters = (ItemsControl)setup.FindName("RequiredParameters");
        var outputs = (ItemsControl)setup.FindName("OutputDataTypes");
        void UpdatePopup()
        {
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
            ((FrameworkElement)popup.Child).UpdateLayout();
        }
        var root = tree.Items.Cast<FunctionNameNode>().Single(node => node.Segment == "sorter-eval");
        root.IsExpanded = true; root.IsSelected = true; Layout(tree);
        Assert.False(button.IsEnabled);
        Assert.Equal(Visibility.Collapsed, details.Visibility);
        Click(button);
        Assert.Empty(model.SetupFunctionName);
        var source = root.Children.Single(node => node.Segment == "source");
        source.IsSelected = true; Layout(tree);
        Assert.True(button.IsEnabled);
        Assert.Equal(Visibility.Visible, details.Visibility);
        Assert.Equal(SetupFunctionCatalog.functions[SetupFunctionNames.evaluationSource].Description, description.Text);
        Assert.Equal("ChannelCount", Assert.Single(parameters.Items.Cast<FunctionParameterPreview>()).Name);
        Assert.Equal(new[] { "SortableTests \"\"", "SorterModelSet \"\"" }, outputs.Items.Cast<string>().Order());
        Assert.Empty(model.SetupFunctionName); // Merely browsing does not change defaults or the function.
        root.IsSelected = true; Layout(tree);
        Assert.Equal(Visibility.Collapsed, details.Visibility); Assert.Null(details.DataContext);
        Assert.Empty(parameters.Items); Assert.Empty(outputs.Items);
        source.IsSelected = true; Layout(tree);
        Click(button); Layout(view);
        Assert.Equal(SetupFunctionNames.evaluationSource, model.SetupFunctionName);
        Assert.Contains(ResultsFunctionNames.sorterEval, results.Choices);
        Assert.True(results.IsEnabled);
        Assert.DoesNotContain(SetupFunctionNames.evaluationSource, results.Choices);
        var resultsTree = (TreeView)results.FindName("FunctionTree");
        var resultsRoot = resultsTree.Items.Cast<FunctionNameNode>().Single(node => node.Segment == "sorter-eval");
        resultsRoot.IsExpanded = true;
        resultsRoot.Children.Single(node => node.Segment == "evaluate").IsSelected = true; Layout(resultsTree);
        Assert.Equal(ResultsFunctionCatalog.functions[ResultsFunctionNames.sorterEval].Description,
            ((TextBlock)results.FindName("FunctionDescription")).Text);
        Assert.Equal(new[] { "SorterSetEval \"\"" }, ((ItemsControl)results.FindName("OutputDataTypes")).Items.Cast<string>());
        Assert.Contains(((ItemsControl)results.FindName("RequiredParameters")).Items.Cast<FunctionParameterPreview>(), parameter => parameter.Name == "CollectSortableTests");
        Assert.Empty(model.ResultsFunctionName);

        // Reopening highlights the selected full name. Escape cancels a different candidate.
        Click((Button)setup.FindName("OpenPicker")); Layout(view);
        Assert.True(popup.IsOpen, "The function popup should stay open.");
        Assert.True(source.IsSelected, "Reopening should restore the selected function.");
        var standard = root.Children.Single(node => node.Segment == "standard");
        standard.IsExpanded = true; standard.Children.Single(node => node.Segment == "mssi").IsSelected = true;
        UpdatePopup();
        Assert.Equal(SetupFunctionCatalog.functions[SetupFunctionNames.evaluationStandardMssi].Description, description.Text);
        Assert.Contains(parameters.Items.Cast<FunctionParameterPreview>(), parameter => parameter.Name == "Repl");
        Assert.Equal(SetupFunctionNames.evaluationSource, model.SetupFunctionName);
        Assert.True(((ScrollViewer)setup.FindName("DetailsViewport")).ScrollableHeight > 0);
        Press(tree, Key.Escape);
        Assert.False(popup.IsOpen); Assert.Equal(SetupFunctionNames.evaluationSource, model.SetupFunctionName);

        Click((Button)setup.FindName("OpenPicker")); UpdatePopup();
        Assert.Equal(SetupFunctionCatalog.functions[SetupFunctionNames.evaluationSource].Description, description.Text);
        standard.Children.Single(node => node.Segment == "mssi").IsSelected = true; UpdatePopup();
        if (Environment.GetEnvironmentVariable("SORTOPIA_FUNCTION_PICKER_SNAPSHOT") is { Length: > 0 } path)
        {
            var content = (FrameworkElement)popup.Child;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) context.DrawRectangle(new VisualBrush(content), null, new Rect(content.RenderSize));
            bitmap.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
        }
        Press(tree, Key.Enter); Layout(view);
        Assert.Equal(SetupFunctionNames.evaluationStandardMssi, model.SetupFunctionName);
        Assert.False(popup.IsOpen);

        // Changing setup can filter out an existing results function, without silently replacing it.
        model.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        model.SetupFunctionName = SetupFunctionNames.standardTests; Layout(view);
        Assert.DoesNotContain(ResultsFunctionNames.sorterEval, results.Choices);
        Assert.Equal(ResultsFunctionNames.sorterEval, results.SelectedFunction);
        Assert.Equal(ResultsFunctionNames.sorterEval, model.ResultsFunctionName);
        Assert.Contains("SorterModelSet", model.FunctionCompatibilityError);
    });

    [Fact]
    public void Inherited_and_unknown_names_are_preserved_and_catalog_changes_clear_stale_candidates() => OnSta(() =>
    {
        var parent = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Parent");
        parent.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child"); parent.Children.Add(child);
        var view = new WorkspaceOverrideEditor { DataContext = child.Editor!.ResultsFunctionOverride }; Layout(view);
        var picker = (FunctionNameSelector)view.FindName("FunctionNameInput");
        Assert.False(picker.IsEnabled);
        Assert.Equal(ResultsFunctionNames.uniformMssiSorterModels, picker.SelectedFunction);
        child.Editor.ResultsFunctionOverride.IsOverridden = true; Layout(view);
        Assert.False(child.Editor.ResultsFunctionOverride.IsOverridden); Assert.False(picker.IsEnabled);
        parent.Editor.ResultsFunctionOverride.IsOverridden = false; Layout(view);
        child.Editor.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels; Layout(view);
        Assert.True(picker.IsEnabled);
        var tree = (TreeView)picker.FindName("FunctionTree");
        var root = tree.Items.Cast<FunctionNameNode>().Single(node => node.Segment == "sorter-model-set");
        Assert.True(root.IsExpanded);
        Assert.True(root.Children.Single(node => node.Segment == "uniform").Children.Single(node => node.Segment == "mssi").IsSelected);
        child.Editor.ResultsFunctionName = "unavailable.saved-function"; Layout(view);
        Assert.Equal("unavailable.saved-function", picker.SelectedFunction);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)picker.FindName("FunctionDetails")).Visibility);
        Assert.False(((Button)picker.FindName("ChooseFunction")).IsEnabled);
        parent.Editor.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        Assert.False(parent.Editor.ResultsFunctionOverride.IsOverridden);
        var legacy = parent.Editor.ToModuleOverrides(); legacy.resultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        parent.Editor.LoadModuleOverrides(legacy); Layout(view); // A saved duplicate remains repairable.
        Assert.False(((CheckBox)view.FindName("OverrideToggle")).IsEnabled);
        var inherit = (Button)view.FindName("UseInheritedFunctionButton");
        Assert.Equal(Visibility.Visible, inherit.Visibility);
        inherit.Command.Execute(null); Layout(view);
        Assert.Equal(ResultsFunctionNames.uniformMssiSorterModels, picker.SelectedFunction);
        Assert.False(picker.IsEnabled);

        var isolated = new FunctionNameSelector { Choices = new[] { "a.b", "a.c" }, SelectedFunction = "a.b" };
        Layout(isolated);
        isolated.Choices = new[] { "a.c" }; Layout(isolated);
        Assert.Equal("a.b", isolated.SelectedFunction);
        var choose = (Button)isolated.FindName("ChooseFunction"); Assert.False(choose.IsEnabled);
        Click(choose); Assert.Equal("a.b", isolated.SelectedFunction);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ancestor_definitions_disable_the_toggle_and_link_to_the_defining_module(bool setup) => OnSta(() =>
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-function-source-");
        try
        {
            var settings = new Sortopia.WPF.Settings.WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
            settings.Save(temporary.FullName);
            var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
            model.AddBranchModuleCommand.Execute(null); var parent = model.Selected!; parent.Name = "Shared functions";
            if (setup) parent.Editor!.SetupFunctionName = SetupFunctionNames.evaluationStandardMssi;
            else parent.Editor!.ResultsFunctionName = ResultsFunctionNames.sorterEval;
            var definition = setup ? parent.Editor.SetupFunctionOverride : parent.Editor.ResultsFunctionOverride;
            model.AddBranchModuleCommand.Execute(null); model.AddBranchModuleCommand.Execute(null);
            var child = model.Selected!;
            var key = setup ? "setupFunction" : "resultsFunction";
            model.SelectConfigurationItem(key);
            var page = new WorkspacePage { DataContext = model };
            using var window = new TestWindow(page);
            void Draw()
            {
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                page.Measure(new Size(1000, 800)); page.Arrange(new Rect(0, 0, 1000, 800)); page.UpdateLayout();
            }
            Draw();
            var fields = (WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            var editor = (WorkspaceOverrideEditor)fields.FindName(setup ? "SetupFunctionOverrideEditor" : "ResultsFunctionOverrideEditor");
            var toggle = (CheckBox)editor.FindName("OverrideToggle");
            Assert.Equal("Define on this module", toggle.Content);
            Assert.Equal(Visibility.Visible, toggle.Visibility); Assert.False(toggle.IsEnabled);
            Assert.False(((FunctionNameSelector)editor.FindName("FunctionNameInput")).IsEnabled);
            Assert.True((setup ? child.Editor!.ResultsFunctionOverride : child.Editor!.SetupFunctionOverride).CanOverride);
            var source = (WorkspaceValueSource)editor.FindName("ValueSource");
            var link = (System.Windows.Documents.Hyperlink)source.FindName("SourceLink");
            Assert.Equal(Visibility.Visible, ((TextBlock)source.FindName("SourceLinkText")).Visibility);
            Assert.Same(parent.Editor, link.CommandParameter);
            parent.Name = "Renamed functions"; Draw();
            Assert.Equal("Defined on Renamed functions", ((System.Windows.Documents.Run)link.Inlines.FirstInline).Text);
            Assert.True(link.Command.CanExecute(link.CommandParameter)); link.Command.Execute(link.CommandParameter);
            Assert.Same(parent, model.Selected); Assert.Equal(key, model.ActiveItemKey);

            definition.IsOverridden = false;
            model.Selected = child; model.SelectConfigurationItem(key); Draw();
            Assert.True(toggle.IsEnabled);
            definition.IsOverridden = true; Draw();
            Assert.False(toggle.IsEnabled);
            definition.EffectiveValue = ""; definition.IsOverridden = true; Draw();
            Assert.True(toggle.IsEnabled); // An empty selection is not a definition.
        }
        finally { temporary.Delete(true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Descendant_definitions_disable_the_toggle_and_link_to_every_defining_module(bool setup) => OnSta(() =>
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-descendant-functions-");
        try
        {
            var settings = new Sortopia.WPF.Settings.WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(temporary.FullName);
            var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
            model.AddBranchModuleCommand.Execute(null); var parent = model.Selected!;
            var leaves = new List<WorkspaceTreeNode>();
            foreach (var name in new[] { "A", "B" })
            {
                model.Selected = parent; model.AddBranchModuleCommand.Execute(null); model.Selected!.Name = name;
                model.AddBranchModuleCommand.Execute(null); var leaf = model.Selected!; leaf.Name = "Functions";
                if (setup) leaf.Editor!.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
                else leaf.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
                leaves.Add(leaf);
            }
            model.Selected = parent; var key = setup ? "setupFunction" : "resultsFunction"; model.SelectConfigurationItem(key);
            var page = new WorkspacePage { DataContext = model }; using var window = new TestWindow(page);
            void Draw()
            {
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                page.Measure(new Size(1000, 800)); page.Arrange(new Rect(0, 0, 1000, 800)); page.UpdateLayout();
            }
            Draw();
            var fields = (WorkspaceConfigurationEditor)page.FindName("ConfigurationFields");
            var editor = (WorkspaceOverrideEditor)fields.FindName(setup ? "SetupFunctionOverrideEditor" : "ResultsFunctionOverrideEditor");
            var toggle = (CheckBox)editor.FindName("OverrideToggle");
            var links = (ItemsControl)editor.FindName("DescendantFunctionLinks");
            Assert.Equal(Visibility.Visible, toggle.Visibility); Assert.False(toggle.IsEnabled);
            Assert.False(((FunctionNameSelector)editor.FindName("FunctionNameInput")).IsEnabled);
            var field = setup ? parent.Editor!.SetupFunctionOverride : parent.Editor!.ResultsFunctionOverride;
            Assert.Equal(new[] { "A / Functions", "B / Functions" }, field.DescendantFunctionDefinitions.Select(value => value.Path));
            field.IsOverridden = true; Assert.False(field.IsOverridden);
            Assert.True((setup ? parent.Editor.ResultsFunctionOverride : parent.Editor.SetupFunctionOverride).CanOverride);
            foreach (var leaf in leaves)
            {
                var index = leaves.IndexOf(leaf);
                var container = (ContentPresenter)links.ItemContainerGenerator.ContainerFromIndex(index);
                var link = (System.Windows.Documents.Hyperlink)links.ItemTemplate.FindName("DescendantFunctionLink", container);
                Assert.Same(leaf.Editor, link.CommandParameter);
                Assert.True(link.Command.CanExecute(link.CommandParameter)); link.Command.Execute(link.CommandParameter);
                Assert.Same(leaf, model.Selected); Assert.Equal(key, model.ActiveItemKey);
                model.Selected = parent; model.SelectConfigurationItem(key); Draw();
            }
            leaves[0].Parent!.Name = "Renamed"; Draw();
            Assert.Equal("Renamed / Functions", field.DescendantFunctionDefinitions[0].Path);
            (setup ? leaves[0].Editor!.SetupFunctionOverride : leaves[0].Editor!.ResultsFunctionOverride).IsOverridden = false; Draw();
            Assert.Single(links.Items.Cast<object>()); Assert.False(toggle.IsEnabled);
            (setup ? leaves[1].Editor!.SetupFunctionOverride : leaves[1].Editor!.ResultsFunctionOverride).IsOverridden = false; Draw();
            Assert.Empty(links.Items.Cast<object>()); Assert.True(toggle.IsEnabled);
        }
        finally { temporary.Delete(true); }
    });

    [Fact]
    public void Conflicting_module_moves_disable_paste_and_link_to_each_modules_correct_function_selector() => OnSta(() =>
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-move-function-links-");
        try
        {
            var settings = new Sortopia.WPF.Settings.WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(temporary.FullName);
            var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
            model.AddBranchModuleCommand.Execute(null); var moving = model.Selected!; moving.Name = "Moving";
            model.AddBranchModuleCommand.Execute(null); var leaf = model.Selected!; leaf.Name = "Functions";
            leaf.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
            leaf.Editor.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
            model.Selected = model.Roots[0]; model.AddBranchModuleCommand.Execute(null); var destination = model.Selected!; destination.Name = "Destination";
            destination.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
            destination.Editor.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
            model.Selected = moving; model.CutCommand.Execute(null); model.Selected = destination;
            var page = new WorkspacePage { DataContext = model }; using var window = new TestWindow(page);
            void Draw()
            {
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                page.Measure(new Size(1000, 900)); page.Arrange(new Rect(0, 0, 1000, 900)); page.UpdateLayout();
            }
            Draw();
            var paste = (Button)page.FindName("PasteEntityButton");
            var notice = (Border)page.FindName("MoveFunctionConflictNotice");
            var links = (ItemsControl)page.FindName("MoveFunctionConflictLinks");
            Assert.False(paste.IsEnabled); Assert.Equal(Visibility.Visible, notice.Visibility); Assert.Equal(4, links.Items.Count);
            Assert.Contains("more than once", Assert.IsType<string>(paste.ToolTip));
            for (var index = 0; index < 4; index++)
            {
                var container = (ContentPresenter)links.ItemContainerGenerator.ContainerFromIndex(index);
                var link = (System.Windows.Documents.Hyperlink)links.ItemTemplate.FindName("MoveFunctionConflictLink", container);
                var conflict = Assert.IsType<WorkspaceFunctionConflict>(link.CommandParameter);
                Assert.True(link.Command.CanExecute(conflict)); link.Command.Execute(conflict);
                Assert.Same(conflict.Node, model.Selected); Assert.Equal(conflict.ItemKey, model.ActiveItemKey);
                Assert.True(model.HasCutItem);
                model.Selected = destination; Draw();
            }
            destination.Editor.ResultsFunctionOverride.IsOverridden = false; Draw();
            Assert.False(paste.IsEnabled); Assert.Equal(2, links.Items.Count);
            Assert.All(links.Items.Cast<WorkspaceFunctionConflict>(), conflict => Assert.True(conflict.IsSetup));
            destination.Editor.SetupFunctionOverride.IsOverridden = false; Draw();
            Assert.True(paste.IsEnabled); Assert.Empty(links.Items); Assert.Equal(Visibility.Collapsed, notice.Visibility);
        }
        finally { temporary.Delete(true); }
    });

    [Fact]
    public void Import_conflict_links_open_workspace_function_selectors_and_original_source_folders() => OnSta(() =>
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-import-function-links-");
        try
        {
            var source = Path.Combine(temporary.FullName, "Source");
            WorkspaceImportFunctionTests.WriteModule(source, WorkspaceImportFunctionTests.Module("Source", true, false, false,
                WorkspaceImportFunctionTests.Module("Results", true, false, true), WorkspaceImportFunctionTests.Module("Setup", true, true, true)));
            var folder = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
            var settings = new Sortopia.WPF.Settings.WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(folder);
            string? opened = null;
            var model = new WorkspacePageViewModel(() => { }, _ => null, settings, pickExistingFolder: _ => source, openFolder: value => opened = value);
            model.AddBranchModuleCommand.Execute(null); var destination = model.Selected!; destination.Name = "Destination";
            destination.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
            destination.Editor.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
            model.AddExistingAsync(WorkspaceEntityKind.Module).GetAwaiter().GetResult();
            Assert.Empty(destination.Children); Assert.True(model.HasImportFunctionConflicts);
            var page = new WorkspacePage { DataContext = model }; using var window = new TestWindow(page);
            void Draw()
            {
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                page.Measure(new Size(1000, 900)); page.Arrange(new Rect(0, 0, 1000, 900)); page.UpdateLayout();
            }
            Draw();
            var notice = (Border)page.FindName("ImportFunctionConflictNotice");
            var links = (ItemsControl)page.FindName("ImportFunctionConflictLinks");
            Assert.Equal(Visibility.Visible, notice.Visibility); Assert.Equal(4, links.Items.Count);
            for (var index = 0; index < 4; index++)
            {
                var container = (ContentPresenter)links.ItemContainerGenerator.ContainerFromIndex(index);
                var link = (System.Windows.Documents.Hyperlink)links.ItemTemplate.FindName("ImportFunctionConflictLink", container);
                var conflict = Assert.IsType<WorkspaceFunctionConflict>(link.CommandParameter);
                Assert.True(link.Command.CanExecute(conflict)); link.Command.Execute(conflict);
                if (conflict.SourceFolder is null)
                { Assert.Same(destination, model.Selected); Assert.Equal(conflict.ItemKey, model.ActiveItemKey); }
                else Assert.Equal(Path.Combine(source, conflict.Node.Name), opened);
                Assert.Empty(destination.Children);
                Draw();
            }
            destination.Editor.ResultsFunctionOverride.IsOverridden = false; Draw();
            Assert.Equal(2, links.Items.Count);
            destination.Editor.SetupFunctionOverride.IsOverridden = false; Draw();
            Assert.Empty(links.Items); Assert.Empty(model.Error);
            Assert.Contains("has not been imported", model.ImportFunctionConflictSummary);
        }
        finally { temporary.Delete(true); }
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    private static void Press(TreeView tree, Key key) => tree.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(tree)!, 0, key)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    private static void Layout(FrameworkElement view, double width = 600)
    {
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        view.Measure(new Size(width, double.PositiveInfinity));
        view.Arrange(new Rect(0, 0, width, view.DesiredSize.Height)); view.UpdateLayout();
    }
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class TestWindow : IDisposable
    {
        private readonly Window window;
        public TestWindow(FrameworkElement content)
        {
            window = new Window { Content = content, Width = 650, Height = 900, Left = -20000, Top = -20000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            window.Show();
        }
        public void Dispose() => window.Close();
    }
}
