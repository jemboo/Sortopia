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
        Assert.False(results.IsEnabled); Assert.Empty(results.Choices);
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
        Assert.True(picker.IsEnabled);
        var tree = (TreeView)picker.FindName("FunctionTree");
        var root = tree.Items.Cast<FunctionNameNode>().Single(node => node.Segment == "sorter-model-set");
        Assert.True(root.IsExpanded);
        Assert.True(root.Children.Single(node => node.Segment == "uniform").Children.Single(node => node.Segment == "mssi").IsSelected);
        child.Editor.ResultsFunctionName = "unavailable.saved-function"; Layout(view);
        Assert.Equal("unavailable.saved-function", picker.SelectedFunction);
        Assert.Equal(Visibility.Collapsed, ((StackPanel)picker.FindName("FunctionDetails")).Visibility);
        Assert.False(((Button)picker.FindName("ChooseFunction")).IsEnabled);
        child.Editor.ResultsFunctionOverride.IsOverridden = false; Layout(view);
        Assert.Equal(ResultsFunctionNames.uniformMssiSorterModels, picker.SelectedFunction);
        Assert.False(picker.IsEnabled);

        var isolated = new FunctionNameSelector { Choices = new[] { "a.b", "a.c" }, SelectedFunction = "a.b" };
        Layout(isolated);
        isolated.Choices = new[] { "a.c" }; Layout(isolated);
        Assert.Equal("a.b", isolated.SelectedFunction);
        var choose = (Button)isolated.FindName("ChooseFunction"); Assert.False(choose.IsEnabled);
        Click(choose); Assert.Equal("a.b", isolated.SelectedFunction);
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
