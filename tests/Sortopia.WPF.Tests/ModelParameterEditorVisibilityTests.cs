using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sortopia.Functions;
using Sortopia.Runs;
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
        module.Editor!.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
        module.Editor.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch); project.Children.Add(run);
        return (module, project, run);
    }
    private static IEnumerable<WorkspaceParameterBinding> Local(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
    private static IEnumerable<WorkspaceParameterBinding> Visible(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.VisibleSetupParameterDefaults : editor.VisibleResultsParameterDefaults;
    private static IEnumerable<WorkspaceParameterBindingPreview> Inherited(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.VisibleInheritedSetupDefaults : editor.VisibleInheritedOutputDefaults;
    private static IEnumerable<WorkspaceParameterSpan> Spans(WorkspaceConfigurationViewModel editor, bool setup) =>
        setup ? editor.VisibleSetupParameters : editor.VisibleResultsParameters;
    private static void Family(WorkspaceConfigurationViewModel editor, bool setup, string family) => Local(editor, setup).Single(row => row.Key == "SorterModelType").Value.Text = family;
    private static void Fields(IEnumerable<string> keys, bool aMax, bool probability)
    {
        var set = keys.ToHashSet();
        Assert.Equal(aMax, set.Contains("AMax")); Assert.Equal(probability, set.Contains("ActivationProbability"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_and_inherited_editors_follow_family_changes_without_losing_values(bool setup)
    {
        var (module, project, run) = Hierarchy(); var editor = module.Editor!;
        Local(editor, setup).Single(row => row.Key == "AMax").Value.Text = "7";
        Local(editor, setup).Single(row => row.Key == "ActivationProbability").Value.Text = "0.8";
        foreach (var family in new[] { "Simple", "SimpleR", "SimpleS", "SimpleR", "Unknown", "Simple" })
        {
            Family(editor, setup, family);
            Fields(Visible(editor, setup).Select(row => row.Key), family == "SimpleR", family == "SimpleS");
            Fields(Local(editor, setup).Single(row => row.Key == "SorterModelType").KeyChoices, family == "SimpleR", family == "SimpleS");
            Fields(Inherited(project.Editor!, setup).Select(row => row.Key), family == "SimpleR", family == "SimpleS");
            Fields(Inherited(run.Editor!, setup).Select(row => row.Key), family == "SimpleR", family == "SimpleS");
            Fields(Visible(editor, !setup).Select(row => row.Key), false, false);
            var saved = editor.ToProjectSettings();
            var values = setup ? saved.setupParameterDefaults : saved.resultsParameterDefaults;
            Assert.Equal("7", values.Single(row => row.keyName == "AMax").value);
            Assert.Equal("0.8", values.Single(row => row.keyName == "ActivationProbability").value);
        }
        Family(editor, setup, "SimpleR");
        Inherited(project.Editor!, setup).Single(row => row.Key == "AMax").OverrideCommand!.Execute(null);
        Local(project.Editor!, setup).Single().Value.Text = "9";
        Inherited(project.Editor!, setup).Single(row => row.Key == "SorterModelType").OverrideCommand!.Execute(null);
        Family(project.Editor!, setup, "SimpleS");
        Fields(Visible(project.Editor!, setup).Select(row => row.Key), false, false); // probability is inherited
        Fields(Inherited(run.Editor!, setup).Select(row => row.Key), false, true);
        Family(project.Editor!, setup, "SimpleR");
        Assert.Equal("9", Visible(project.Editor!, setup).Single(row => row.Key == "AMax").Value.Text);
        var rows = Visible(project.Editor!, setup);
        Local(project.Editor!, setup).Single(row => row.Key == "AMax").Value.Text = "10";
        Assert.Same(rows, Visible(project.Editor!, setup)); // value edits keep WPF rows and keyboard focus
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_family_spans_show_both_controls_and_preserve_hidden_spans_and_parameter_sets(bool setup)
    {
        var (_, _, run) = Hierarchy(); var editor = run.Editor!;
        var families = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterModelType", setup, "SimpleR", "SimpleS");
        var aMax = WorkspaceParameterSetsPreviewTests.AddSpan(run, "AMax", setup, "1", "3");
        var probability = WorkspaceParameterSetsPreviewTests.AddSpan(run, "ActivationProbability", setup, "0.25", "0.75");
        Fields(Spans(editor, setup).Select(row => row.Key), true, true);
        Fields(families.KeyChoices, true, true);
        families.Values[1].Text = "Simple";
        Fields(Spans(editor, setup).Select(row => row.Key), true, false);
        Fields(families.KeyChoices, true, false);
        families.Values[0].Text = "SimpleS";
        Fields(Spans(editor, setup).Select(row => row.Key), false, true);
        Fields(families.KeyChoices, false, true);
        families.Values[0].Text = "Simple";
        Fields(Spans(editor, setup).Select(row => row.Key), false, false);
        Assert.Equal(new[] { "1", "3" }, aMax.Values.Select(value => value.Text));
        Assert.Equal(new[] { "0.25", "0.75" }, probability.Values.Select(value => value.Text));
        var dto = editor.ToRunInputs(Guid.NewGuid(), "Run", Guid.NewGuid());
        var storedSpans = setup ? dto.setupParameterSpans : dto.resultsParameterSpans;
        Assert.Equal(3, storedSpans.Length);
        Assert.Empty(setup ? editor.SetupParametersError : editor.ResultsParametersError);
        Assert.Equal(8, (setup ? editor.SetupParameterSetsPreview : editor.ResultsParameterSetsPreview).Rows.Count());
        families.Values[1].Text = "SimpleR";
        Assert.Same(aMax, Spans(editor, setup).Single(row => row.Key == "AMax"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Other_family_inputs_can_be_repaired_without_changing_model_selection_or_defaults(bool setup)
    {
        var (module, _, run) = Hierarchy(); var editor = run.Editor!;
        var span = WorkspaceParameterSetsPreviewTests.AddSpan(run, "AMax", setup);
        Assert.DoesNotContain(span, Spans(editor, setup));
        Assert.NotEmpty(setup ? editor.SetupParametersError : editor.ResultsParametersError);
        if (setup) editor.ShowOtherSetupModelInputs = true; else editor.ShowOtherResultsModelInputs = true;
        Assert.Contains(span, Spans(editor, setup));
        Assert.Contains("AMax", span.KeyChoices);
        span.AddValueCommand.Execute(null); span.Values[0].Text = "2";
        Assert.Empty(setup ? editor.SetupParametersError : editor.ResultsParametersError);
        Assert.Equal("Simple", Local(module.Editor!, setup).Single(row => row.Key == "SorterModelType").Value.Text);
        if (setup) editor.ShowOtherSetupModelInputs = false; else editor.ShowOtherResultsModelInputs = false;
        Assert.DoesNotContain(span, Spans(editor, setup));
        Assert.Empty(setup ? editor.SetupParametersError : editor.ResultsParametersError);
        Assert.DoesNotContain(setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults, row => row.Key == "AMax");
    }

    [Fact]
    public void Wpf_default_and_span_lists_update_for_each_phase()
    {
        Exception? failure = null;
        var thread = new Thread(() => {
            try {
                var (module, _, run) = Hierarchy();
                var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = module.Editor, ActiveItemKey = "resultsDefaults" };
                void Layout()
                {
                    view.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.Measure(new Size(620, double.PositiveInfinity)); view.Arrange(new Rect(0, 0, 620, view.DesiredSize.Height)); view.UpdateLayout();
                }
                ItemsControl List(string name) => (ItemsControl)view.FindName(name);
                foreach (var setup in new[] { false, true })
                {
                    view.ActiveItemKey = setup ? "setupDefaults" : "resultsDefaults"; Layout();
                    var list = List(setup ? "SetupParameterDefaultsList" : "ResultsParameterDefaultsList");
                    Fields(list.Items.Cast<WorkspaceParameterBinding>().Select(row => row.Key), false, false);
                    Family(module.Editor!, setup, "SimpleR"); Layout();
                    Fields(list.Items.Cast<WorkspaceParameterBinding>().Select(row => row.Key), true, false);
                    Family(module.Editor!, setup, "SimpleS"); Layout();
                    Fields(list.Items.Cast<WorkspaceParameterBinding>().Select(row => row.Key), false, true);
                }
                var families = WorkspaceParameterSetsPreviewTests.AddSpan(run, "SorterModelType", false, "SimpleR");
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "AMax", false, "2");
                WorkspaceParameterSetsPreviewTests.AddSpan(run, "ActivationProbability", false, "0.5");
                view.DataContext = run.Editor; view.ActiveItemKey = "resultsSpans"; Layout();
                var spans = List("ResultsParametersList");
                Fields(spans.Items.Cast<WorkspaceParameterSpan>().Select(row => row.Key), true, false);
                families.Values[0].Text = "SimpleS"; Layout();
                Fields(spans.Items.Cast<WorkspaceParameterSpan>().Select(row => row.Key), false, true);
            } catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
