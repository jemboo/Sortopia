using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class ModelParameterEditorVisibilityTests
{
    private static (WorkspaceTreeNode Module, WorkspaceTreeNode Project, WorkspaceTreeNode Run) Hierarchy()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        Family(module.Editor!, true, "Simple"); Family(module.Editor!, false, "Simple");
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        return (module, project, run);
    }
    private static IEnumerable<WorkspaceParameterBinding> Local(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
    private static IEnumerable<WorkspaceParameterBinding> Visible(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.LocalSetupParameterDefaults : editor.LocalResultsParameterDefaults;
    private static IEnumerable<WorkspaceParameterBindingPreview> Inherited(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults;
    private static void Family(WorkspaceConfigurationViewModel editor, bool setup, string family)
    {
        var name = "sorter-model-set.uniform.mssi" + ModelFamilyResultsTests.Suffix(family);
        if (setup) editor.SetupFunctionName = name; else editor.ResultsFunctionName = name;
        (setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
    }
    private static void Fields(IEnumerable<string> keys, string family)
    {
        var set = keys.ToHashSet();
        Assert.DoesNotContain("SorterModelType", set);
        Assert.Equal(family == "SimpleR", set.Contains("AMax"));
        Assert.Equal(family == "SimpleR", set.Contains("AInitial"));
        Assert.Equal(family == "SimpleS", set.Contains("ActivationProbability"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Changing_functions_updates_inherited_inputs_and_saves_only_the_selected_family(bool setup)
    {
        var (module, project, run) = Hierarchy(); var editor = module.Editor!;
        Family(editor, setup, "SimpleR");
        Local(editor, setup).Single(row => row.Key == "AMax").Value.Text = "7";
        Local(editor, setup).Single(row => row.Key == "AInitial").Value.Text = "-3";
        Family(editor, setup, "SimpleS");
        Local(editor, setup).Single(row => row.Key == "ActivationProbability").Value.Text = "0.8";
        foreach (var family in new[] { "Simple", "SimpleR", "SimpleS", "SimpleR", "Simple" })
        {
            Family(editor, setup, family);
            Fields(Visible(editor, setup).Select(row => row.Key), family);
            Fields(Visible(editor, setup).First().KeyChoices, family);
            Fields(Inherited(project.Editor!, setup).Select(row => row.Key), family);
            Fields(Inherited(run.Editor!, setup).Select(row => row.Key), family);
            Fields(Visible(editor, !setup).Select(row => row.Key), "Simple");
            var saved = editor.ToProjectSettings();
            Fields((setup ? saved.setupParameterDefaults : saved.resultsParameterDefaults).Select(row => row.keyName), family);
        }
        Family(editor, setup, "SimpleR");
        Assert.Equal("7", Visible(editor, setup).Single(row => row.Key == "AMax").Value.Text);
        Assert.Equal("-3", Visible(editor, setup).Single(row => row.Key == "AInitial").Value.Text);
        Inherited(project.Editor!, setup).Single(row => row.Key == "AMax").OverrideCommand!.Execute(null);
        var rows = Visible(project.Editor!, setup);
        Local(project.Editor!, setup).Single(row => row.Key == "AMax").Value.Text = "10";
        Assert.Same(rows, Visible(project.Editor!, setup));
        Assert.Equal("10", Inherited(run.Editor!, setup).Single(row => row.Key == "AMax").Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SimpleR_spans_offer_only_relevant_keys_and_validate_every_start(bool setup)
    {
        var (module, _, run) = Hierarchy(); Family(module.Editor!, setup, "SimpleR");
        var initial = WorkspaceParameterSetsPreviewTests.AddSpan(run, "AInitial", setup, "-1", "1");
        Assert.Contains("AInitial", initial.KeyChoices);
        Assert.DoesNotContain("ActivationProbability", initial.KeyChoices);
        Assert.DoesNotContain("SorterModelType", initial.KeyChoices);
        Assert.Empty(setup ? run.Editor!.SetupParametersError : run.Editor!.ResultsParametersError);
        Assert.Equal(2, (setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).Rows.Count());
        initial.Values[1].Text = "2";
        Assert.Contains("[-AMax, AMax]", setup ? run.Editor.SetupParametersError : run.Editor.ResultsParametersError);
    }

    [Fact]
    public void Wpf_default_lists_update_when_the_function_family_changes()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var (module, _, _) = Hierarchy();
                var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = module.Editor };
                void Layout()
                {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.Measure(new Size(620, double.PositiveInfinity)); view.Arrange(new Rect(0, 0, 620, view.DesiredSize.Height)); view.UpdateLayout();
                }
                foreach (var setup in new[] { false, true })
                {
                    view.ActiveItemKey = setup ? "setupDefaults" : "resultsDefaults";
                    var list = (ItemsControl)view.FindName(setup ? "SetupParameterDefaultsList" : "ResultsParameterDefaultsList");
                    foreach (var family in new[] { "Simple", "SimpleR", "SimpleS" })
                    {
                        Family(module.Editor!, setup, family); Layout();
                        Fields(list.Items.Cast<WorkspaceParameterBinding>().Select(row => row.Key), family);
                    }
                }
            } catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
