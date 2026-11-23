using Sortopia.Workspaces.Serialization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceParameterNavigationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Icons_open_the_defining_node_and_correct_value_editor_without_changing_the_other_phase(bool setup)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Directory.CreateTempSubdirectory("Sortopia-parameter-navigation-");
            Window? window = null;
            try
            {
                var rootFolder = Directory.CreateDirectory(Path.Combine(folder.FullName, "Workspace"));
                var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt")); settings.Save(rootFolder.FullName);
                var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
                WorkspaceTreeNode Add(WorkspaceTreeNode parent, WorkspaceEntityKind kind, string name)
                {
                    var child = new WorkspaceTreeNode(kind, name, family: hierarchyKind.Branch);
                    parent.Children.Add(child); return child;
                }
                var owner = Add(model.Roots[0], WorkspaceEntityKind.Module, "Shared defaults");
                WorkspaceParameterInheritanceTests.AddDefault(owner.Editor!, "ChannelCount", "16", setup);
                WorkspaceParameterInheritanceTests.AddDefault(owner.Editor!, "ChannelCount", "64", !setup);
                var functions = Add(owner, WorkspaceEntityKind.Module, "Functions");
                functions.Editor!.SetupFunctionName = SetupFunctionNames.standardTests;
                functions.Editor.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
                WorkspaceParameterInheritanceTests.AddDefault(functions.Editor, "SortableDataFormat", "Bools", true);
                WorkspaceParameterInheritanceTests.AddDefault(functions.Editor, "SortableDataFormat", "Bools", false);
                var project = Add(functions, WorkspaceEntityKind.Project, "Project");
                var run = Add(project, WorkspaceEntityKind.Run, "Run");
                var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SortableDataFormat", setup, "Bools", "BitVector512");
                var phase = setup ? "Setup" : "Results";
                var keyPrefix = setup ? "setup" : "results";
                model.Selected = run; model.SelectConfigurationItem(keyPrefix + "Function");
                var view = new WorkspacePage { DataContext = model };
                window = new Window { Content = view, Width = 1100, Height = 750, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                window.Show();
                var editor = (WorkspaceConfigurationEditor)view.FindName("ConfigurationFields");
                void Draw()
                {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    view.UpdateLayout();
                }
                void ShowFunction()
                {
                    model.Selected = run; model.SelectConfigurationItem(keyPrefix + "Function"); Draw();
                }
                Button Icon(string key)
                {
                    var table = (ItemsControl)editor.FindName($"{phase}FunctionParametersTable");
                    var row = table.Items.Cast<WorkspaceFunctionParameter>().Single(item => item.Name == key);
                    var container = (ContentPresenter)table.ItemContainerGenerator.ContainerFromItem(row);
                    return (Button)table.ItemTemplate.FindName("OpenParameterValueButton", container);
                }
                void Click(string key)
                {
                    var button = Icon(key); Assert.True(button.IsEnabled);
                    ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); Draw();
                }
                WorkspaceParameterValueEditor ValueEditor(string key, bool spanValue)
                {
                    var rows = (ItemsControl)editor.FindName(spanValue ? $"{phase}ParametersList" : $"{phase}ParameterDefaultsList");
                    var item = rows.Items.Cast<object>().Single(item => item switch
                    {
                        WorkspaceParameterBinding value => value.Key == key,
                        WorkspaceParameterSpan value => value.Key == key,
                        _ => false
                    });
                    var container = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromItem(item);
                    if (!spanValue) return (WorkspaceParameterValueEditor)rows.ItemTemplate.FindName("DefaultValueEditor", container);
                    var values = (ItemsControl)rows.ItemTemplate.FindName("ParameterValues", container);
                    return (WorkspaceParameterValueEditor)values.ItemTemplate.FindName("ParameterValueEditor", (ContentPresenter)values.ItemContainerGenerator.ContainerFromIndex(0));
                }
                void AssertFocused(WorkspaceParameterValueEditor valueEditor)
                {
                    var focus = Assert.IsAssignableFrom<DependencyObject>(FocusManager.GetFocusedElement(window));
                    Assert.True(ReferenceEquals(valueEditor, focus) || valueEditor.IsAncestorOf(focus));
                }
                Draw();
                Assert.Contains("Shared defaults", (string)Icon("ChannelCount").ToolTip);
                Click("ChannelCount");
                Assert.Same(owner, model.Selected); Assert.True(model.Roots[0].IsExpanded);
                Assert.Equal(keyPrefix + "Defaults", model.ActiveItemKey);
                Assert.False(owner.Editor!.ShowFunctionConfiguration);
                Assert.Equal(Visibility.Visible, ((Expander)editor.FindName($"{phase}DefaultsSection")).Visibility);
                var input = (TextBox)ValueEditor("ChannelCount", false).FindName("TypedValueInput");
                AssertFocused(ValueEditor("ChannelCount", false));
                input.Text = "24"; Draw();
                Assert.Equal("24", (setup ? run.Editor!.SetupFunctionParameters : run.Editor!.ResultsFunctionParameters).Single(row => row.Name == "ChannelCount").Value);
                Assert.Equal("64", (setup ? run.Editor.ResultsFunctionParameters : run.Editor.SetupFunctionParameters).Single(row => row.Name == "ChannelCount").Value);

                // An inherited blank default already owns this key: edit it instead of creating a conflicting run span.
                input.Text = ""; ShowFunction(); Click("ChannelCount");
                Assert.Same(owner, model.Selected); Assert.Equal(keyPrefix + "Defaults", model.ActiveItemKey);
                AssertFocused(ValueEditor("ChannelCount", false));
                Assert.DoesNotContain(setup ? run.Editor.SetupParameters : run.Editor.ResultsParameters, row => row.Key == "ChannelCount");
                input.Text = "24"; Draw();

                ShowFunction(); Click("SortableDataFormat");
                Assert.Same(run, model.Selected); Assert.Equal(keyPrefix + "Spans", model.ActiveItemKey);
                AssertFocused(ValueEditor("SortableDataFormat", true));
                var selectedSpan = Assert.IsType<WorkspaceTypedValue>(ValueEditor("SortableDataFormat", true).DataContext);
                Assert.Same(span.Values[0], selectedSpan);

                (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults).Single(row => row.Key == "ChannelCount").OverrideCommand!.Execute(null);
                (setup ? project.Editor.SetupParameterDefaults : project.Editor.ResultsParameterDefaults).Single(row => row.Key == "ChannelCount").Value.Text = "32";
                ShowFunction(); Click("ChannelCount");
                Assert.Same(project, model.Selected); Assert.Equal(keyPrefix + "Defaults", model.ActiveItemKey);
                AssertFocused(ValueEditor("ChannelCount", false));
                Assert.Equal("32", ((TextBox)ValueEditor("ChannelCount", false).FindName("TypedValueInput")).Text);

                span.Values.Clear(); ShowFunction();
                Assert.True(Icon("SortableDataFormat").IsEnabled);
                Assert.True(Icon("SortableDataFormat").IsTabStop);
                Click("SortableDataFormat");
                Assert.Same(run, model.Selected); Assert.Equal(keyPrefix + "Spans", model.ActiveItemKey);
                Assert.Equal("", Assert.Single(span.Values).Text);
                AssertFocused(ValueEditor("SortableDataFormat", true));
                ShowFunction(); Click("SortableDataFormat"); Assert.Single(span.Values);
                // A sealed project's captured default is its own source, and navigation preserves the lock.
                project.Editor.Freeze(); ShowFunction(); Click("ChannelCount");
                Assert.Same(project, model.Selected); Assert.True(project.Editor.IsReadOnly);
                Assert.Equal(keyPrefix + "Defaults", model.ActiveItemKey);
                var previews = (ItemsControl)editor.FindName($"Inherited{phase}ParameterDefaultsList");
                Assert.Contains(previews.Items.Cast<WorkspaceParameterBindingPreview>(), value => value.Key == "ChannelCount" && value.Value == "32");
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); folder.Delete(true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Theory]
    [InlineData(false, WorkspaceEntityKind.Module)]
    [InlineData(true, WorkspaceEntityKind.Module)]
    [InlineData(false, WorkspaceEntityKind.Project)]
    [InlineData(true, WorkspaceEntityKind.Project)]
    [InlineData(false, WorkspaceEntityKind.Run)]
    [InlineData(true, WorkspaceEntityKind.Run)]
    public void Empty_icons_add_only_the_selected_parameter_and_focus_its_value(bool setup, WorkspaceEntityKind kind)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Directory.CreateTempSubdirectory("Sortopia-missing-parameter-");
            Window? window = null;
            try
            {
                var rootFolder = Directory.CreateDirectory(Path.Combine(folder.FullName, "Workspace"));
                var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt")); settings.Save(rootFolder.FullName);
                var model = new WorkspacePageViewModel(() => { }, _ => null, settings);
                var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch); model.Roots[0].Children.Add(module);
                module.Editor!.SetupFunctionName = SetupFunctionNames.standardTests;
                module.Editor.ResultsFunctionName = ResultsFunctionNames.standardSortableTests;
                WorkspaceParameterInheritanceTests.AddDefault(module.Editor, "ChannelCount", "64", !setup);
                var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
                if (kind == WorkspaceEntityKind.Run)
                    foreach (var key in (setup ? project.Editor!.SetupQueryOutputs : project.Editor!.ResultsQueryOutputs).SelectMany(output => output.Keys).ToArray()) key.Selected = true;
                var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
                var node = kind == WorkspaceEntityKind.Module ? module : kind == WorkspaceEntityKind.Project ? project : run;
                var phase = setup ? "Setup" : "Results";
                var keyPrefix = setup ? "setup" : "results";
                model.Selected = node; model.SelectConfigurationItem(keyPrefix + "Function");
                var view = new WorkspacePage { DataContext = model };
                window = new Window { Content = view, Width = 1100, Height = 750, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                window.Show();
                var editor = (WorkspaceConfigurationEditor)view.FindName("ConfigurationFields");
                void Draw() { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
                void ShowFunction() { model.SelectConfigurationItem(keyPrefix + "Function"); Draw(); }
                Draw();
                var table = (ItemsControl)editor.FindName($"{phase}FunctionParametersTable");
                var missing = table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "ChannelCount");
                Assert.Equal(WorkspaceParameterSupply.None, missing.Supply);
                var container = (ContentPresenter)table.ItemContainerGenerator.ContainerFromItem(missing);
                var button = (Button)table.ItemTemplate.FindName("OpenParameterValueButton", container);
                Assert.True(button.IsEnabled); Assert.True(button.IsTabStop);
                Assert.StartsWith(node.IsRun ? "Open ChannelCount" : "Add ChannelCount", (string)button.ToolTip);
                ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke(); Draw();
                Assert.Same(node, model.Selected);
                Assert.Equal(keyPrefix + (node.IsRun ? "Spans" : "Defaults"), model.ActiveItemKey);
                var rows = (ItemsControl)editor.FindName(node.IsRun ? $"{phase}ParametersList" : $"{phase}ParameterDefaultsList");
                // Run rows already exist for every project query key. Navigation
                // should add a value only to the selected row, leaving the others empty.
                var item = Assert.Single(rows.Items.Cast<object>(), row => row switch
                {
                    WorkspaceParameterSpan span => span.Key == "ChannelCount",
                    WorkspaceParameterBinding binding => binding.Key == "ChannelCount",
                    _ => false
                });
                var rowCount = rows.Items.Count;
                WorkspaceTypedValue value;
                var rowContainer = (ContentPresenter)rows.ItemContainerGenerator.ContainerFromItem(item);
                WorkspaceParameterValueEditor valueEditor;
                if (node.IsRun)
                {
                    var span = Assert.IsType<WorkspaceParameterSpan>(item); Assert.Equal("ChannelCount", span.Key);
                    value = Assert.Single(span.Values);
                    var values = (ItemsControl)rows.ItemTemplate.FindName("ParameterValues", rowContainer);
                    valueEditor = (WorkspaceParameterValueEditor)values.ItemTemplate.FindName("ParameterValueEditor", (ContentPresenter)values.ItemContainerGenerator.ContainerFromIndex(0));
                }
                else
                {
                    var binding = Assert.IsType<WorkspaceParameterBinding>(item); Assert.Equal("ChannelCount", binding.Key);
                    value = binding.Value;
                    valueEditor = (WorkspaceParameterValueEditor)rows.ItemTemplate.FindName("DefaultValueEditor", rowContainer);
                }
                Assert.Equal("", value.Text); Assert.Equal(Sortopia.Runs.parameterValueType.Integer, value.ValueType);
                var focused = Assert.IsAssignableFrom<DependencyObject>(FocusManager.GetFocusedElement(window));
                Assert.True(valueEditor.IsAncestorOf(focused));
                // Repeated clicks while the value is still blank must reuse the row and its first field.
                model.OpenFunctionParameterCommand.Execute(missing); Draw(); Assert.Equal(rowCount, rows.Items.Count);
                if (item is WorkspaceParameterSpan existing) Assert.Same(value, Assert.Single(existing.Values));
                ((TextBox)valueEditor.FindName("TypedValueInput")).Text = "16"; Draw();
                model.OpenFunctionParameterCommand.Execute(missing); Draw(); Assert.Equal("16", value.Text);
                ShowFunction();
                var added = table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "ChannelCount");
                Assert.Equal(node.IsRun ? WorkspaceParameterSupply.Span : WorkspaceParameterSupply.Default, added.Supply);
                Assert.Equal(node.IsRun ? "[16]" : "16", added.Value);
                Assert.Equal("64", (setup ? node.Editor!.ResultsFunctionParameters : node.Editor!.SetupFunctionParameters).Single(row => row.Name == "ChannelCount").Value);
                Assert.Equal(WorkspaceParameterSupply.None, table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "SortableDataFormat").Supply);

                node.Editor.SetGeneratedParametersLock(true); Draw();
                var locked = table.Items.Cast<WorkspaceFunctionParameter>().Single(row => row.Name == "SortableDataFormat");
                Assert.False(locked.CanNavigate); Assert.Contains("locked", locked.NavigationHint);
                model.OpenFunctionParameterCommand.Execute(locked);
                Assert.DoesNotContain(setup ? node.Editor.SetupParameterDefaults : node.Editor.ResultsParameterDefaults, row => row.Key == "SortableDataFormat");
                if (node.IsRun)
                    Assert.Empty(Assert.Single(setup ? node.Editor.SetupParameters : node.Editor.ResultsParameters, row => row.Key == "SortableDataFormat").Values);
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); Dispatcher.CurrentDispatcher.InvokeShutdown(); folder.Delete(true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
