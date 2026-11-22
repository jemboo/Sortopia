using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceIndependentConfigurationTests
{
    [Theory]
    [InlineData(WorkspaceEntityKind.Module, true)]
    [InlineData(WorkspaceEntityKind.Project, true)]
    [InlineData(WorkspaceEntityKind.Run, true)]
    [InlineData(WorkspaceEntityKind.Module, false)]
    [InlineData(WorkspaceEntityKind.Project, false)]
    [InlineData(WorkspaceEntityKind.Run, false)]
    public void Each_phase_can_be_configured_while_the_other_is_missing_or_incompatible(WorkspaceEntityKind kind, bool setup)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var folder = Directory.CreateTempSubdirectory("Sortopia-independent-configuration-");
            Window? window = null;
            try
            {
                var settings = new WorkspaceFolderSettings(Path.Combine(folder.FullName, "settings.txt"));
                settings.Save(Directory.CreateDirectory(Path.Combine(folder.FullName, "Workspace")).FullName);
                var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
                WorkspaceTreeNode Add(WorkspaceTreeNode parent, WorkspaceEntityKind childKind)
                {
                    var child = new WorkspaceTreeNode(childKind, childKind.ToString(), family: hierarchyKind.Branch);
                    parent.Children.Add(child); return child;
                }
                var module = Add(page.Roots[0], WorkspaceEntityKind.Module);
                var project = Add(module, WorkspaceEntityKind.Project);
                var run = Add(project, WorkspaceEntityKind.Run);
                var node = kind == WorkspaceEntityKind.Module ? module : kind == WorkspaceEntityKind.Project ? project : run;
                page.Selected = node;
                var view = new WorkspacePage { DataContext = page };
                window = new Window { Content = view, Width = 1100, Height = 750, Left = -20000, Top = -20000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                window.Show();
                var editor = (WorkspaceConfigurationEditor)view.FindName("ConfigurationFields");
                void Draw() { view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout(); }
                void Open(string key, string element)
                {
                    var item = Assert.Single(page.InspectorItems, row => row.Key == key);
                    page.SelectConfigurationItemCommand.Execute(item); Draw();
                    Assert.Equal(key, page.ActiveItemKey);
                    Assert.True(((FrameworkElement)editor.FindName(element)).IsVisible);
                }
                void CheckPhase(bool phase)
                {
                    var prefix = phase ? "setup" : "results";
                    var title = phase ? "Setup" : "Results";
                    Open(phase ? "setupOutputs" : "resultsTypes", phase ? "SetupOutputsItem" : "ResultsTypesItem");
                    Open(prefix + "Defaults", title + "DefaultsSection");
                    if (node.IsRun) Open(prefix + "Spans", title + "SpansSection");
                    else Assert.DoesNotContain(page.InspectorItems, row => row.Key == prefix + "Spans");
                }
                void SetOther(string name)
                {
                    if (setup) module.Editor!.ResultsFunctionName = name;
                    else module.Editor!.SetupFunctionName = name;
                    Draw();
                }
                Draw();
                Assert.DoesNotContain(page.InspectorItems, row => row.Key is "setupOutputs" or "resultsTypes" or "setupSpans" or "resultsSpans");
                if (setup) module.Editor!.SetupFunctionName = SetupFunctionNames.standardTests;
                else module.Editor!.ResultsFunctionName = ResultsFunctionNames.sorterEval;
                Draw(); CheckPhase(setup);
                Assert.DoesNotContain(page.InspectorItems, row => row.Key == (setup ? "resultsTypes" : "setupOutputs"));
                Assert.DoesNotContain(page.InspectorItems, row => row.Key == (setup ? "resultsDefaults" : "setupDefaults"));
                Assert.False(node.Editor!.FunctionsReady);

                // The visible editors accept defaults and spans before the other function is chosen.
                var parameter = setup ? "ChannelCount" : "SorterEvalType";
                var value = setup ? "16" : "V1";
                if (node.IsRun) WorkspaceParameterSetsPreviewTests.AddSpan(node, parameter, setup, value);
                else WorkspaceParameterInheritanceTests.AddDefault(node.Editor, parameter, value, setup);
                Draw();
                Assert.Contains(setup ? node.Editor.SetupFunctionParameters : node.Editor.ResultsFunctionParameters,
                    row => row.Name == parameter && row.Value == (node.IsRun ? $"[{value}]" : value));

                // Both editors stay accessible when the selected functions have incompatible outputs/inputs.
                SetOther(setup ? ResultsFunctionNames.sorterEval : SetupFunctionNames.standardTests);
                Assert.False(node.Editor.FunctionsReady);
                Assert.NotEmpty(node.Editor.FunctionCompatibilityError);
                Assert.Contains(node.Editor.FunctionCompatibilityError, node.Editor.ResultsFunctionError);
                CheckPhase(!setup); CheckPhase(setup);

                var activeKey = page.ActiveItemKey;
                SetOther("");
                Assert.Equal(activeKey, page.ActiveItemKey);
                CheckPhase(setup);
                SetOther("unavailable.function");
                Assert.Equal(activeKey, page.ActiveItemKey);
                CheckPhase(setup);
                Assert.Contains(setup ? node.Editor.SetupFunctionParameters : node.Editor.ResultsFunctionParameters,
                    row => row.Name == parameter && row.Value == (node.IsRun ? $"[{value}]" : value));
            }
            catch (Exception exception) { failure = exception; }
            finally { window?.Close(); folder.Delete(true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
