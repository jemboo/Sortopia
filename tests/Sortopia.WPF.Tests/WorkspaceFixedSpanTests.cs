using System.IO;
using Sortopia.Runs;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

[Collection("Workspace editor rendering")]
public sealed class WorkspaceFixedSpanTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-fixed-spans-");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private WorkspacePageViewModel Open()
    {
        Settings.Save(Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName);
        var page = new WorkspacePageViewModel(() => { }, _ => null, Settings);
        if (page.HasSavedWorkspace) return page;
        page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Editor!.LoadFixtureSettings(RunExecutionTests.Settings(true));
        page.AddProjectCommand.Execute(null); page.AddRunCommand.Execute(null);
        return page;
    }

    [Fact]
    public async Task Project_transition_updates_existing_runs_and_preserves_local_defaults_and_values_across_reopening()
    {
        var page = Open(); var run = page.Selected!; var project = run.Parent!;
        project.Editor!.InheritedOutputDefaults.Single(row => row.Key == "SortableDataFormat").OverrideCommand!.Execute(null);
        var defaultRows = project.Editor.EditableResultsParameterDefaults;
        project.Editor.ResultsParameterDefaults.Single().Value.Text = "Ints";
        Assert.Same(defaultRows, project.Editor.EditableResultsParameterDefaults);
        project.Editor.ResultsQueryOutputs.Single().FixedParameters.Single(row => row.Key == "SortableDataFormat").UseAsQueryKeyCommand.Execute(null);
        var span = Assert.Single(run.Editor!.DeclaredResultsParameters);
        Assert.Empty(span.Values); Assert.Empty(project.Editor.EditableResultsParameterDefaults);
        Assert.Equal("Ints", project.Editor.ToLocalSettings().resultsParameterDefaults.Single().value);
        Assert.DoesNotContain(project.Editor.ToProjectSettings().resultsParameterDefaults, row => row.keyName == span.Key);
        Assert.False(span.RemoveCommand.CanExecute(null));
        span.RemoveCommand.Execute(null); Assert.Same(span, Assert.Single(run.Editor.DeclaredResultsParameters));
        span.AddValueCommand.Execute(null); span.Values[0].Text = "Bools";
        var rows = run.Editor.DeclaredResultsParameters;
        span.Values[0].Text = "Ints"; Assert.Same(rows, run.Editor.DeclaredResultsParameters);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);

        page = Open(); WorkspaceRunExecutionTests.SelectRun(page); run = page.Selected!; project = run.Parent!;
        span = Assert.Single(run.Editor!.DeclaredResultsParameters); Assert.Equal("Ints", span.Values[0].Text);
        project.Editor!.ExcludedResultsDefaults.Single().RestoreCommand.Execute(null);
        Assert.Empty(run.Editor.DeclaredResultsParameters); Assert.Same(span, Assert.Single(run.Editor.RepairResultsParameters));
        Assert.Equal("Ints", project.Editor.ToProjectSettings().resultsParameterDefaults.Single(row => row.keyName == span.Key).value);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page); run = page.Selected!; project = run.Parent!;
        span = Assert.Single(run.Editor!.RepairResultsParameters); Assert.Equal("Ints", span.Values[0].Text);
        project.Editor!.ResultsQueryOutputs.Single().FixedParameters.Single(row => row.Key == span.Key).UseAsQueryKeyCommand.Execute(null);
        Assert.Same(span, Assert.Single(run.Editor.DeclaredResultsParameters)); Assert.Empty(run.Editor.RepairResultsParameters);
        Assert.Empty(run.Editor.ResultsParametersError);
    }

    [Fact]
    public async Task Switching_an_unfinished_local_default_to_a_query_preserves_the_draft_without_blocking_run_values()
    {
        var page = Open(); var run = page.Selected!; var project = run.Parent!;
        project.Editor!.InheritedSetupDefaults.Single(row => row.Key == "ChannelCount").OverrideCommand!.Execute(null);
        project.Editor.SetupParameterDefaults.Single().Value.Text = "unfinished";
        project.Editor.SetupQueryOutputs.Single().FixedParameters.Single(row => row.Key == "ChannelCount").UseAsQueryKeyCommand.Execute(null);
        var span = Assert.Single(run.Editor!.DeclaredSetupParameters); span.AddValueCommand.Execute(null); span.Values[0].Text = "4";
        await page.SaveWorkspaceAsync(); Assert.True(page.Error.Length == 0, page.Error);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page); run = page.Selected!; project = run.Parent!;
        Assert.Equal("unfinished", project.Editor!.ToLocalSettings().setupParameterDefaults.Single().value);
        Assert.True(page.GenerateRunParametersCommand.CanExecute(null), page.GenerationHint);
        project.Editor.SetupQueryOutputs.Single().Keys.Single(row => row.Key == "ChannelCount").Selected = false;
        project.Editor.InheritedSetupDefaults.Single(row => row.Key == "SortableDataFormat").OverrideCommand!.Execute(null);
        project.Editor.SetupParameterDefaults.Single(row => row.Key == "SortableDataFormat").Value.Text = "Ints";
        Assert.Equal("ChannelCount", Assert.Single(project.Editor.ExcludedSetupDefaults).Key);
        Assert.DoesNotContain(project.Editor.ToProjectSettings().setupParameterDefaults, row => row.keyName == "ChannelCount");
        project.Editor.SetupQueryOutputs.Single().Keys.Single(row => row.Key == "ChannelCount").Selected = true;
        project.Editor.ExcludedSetupDefaults.Single().RestoreCommand.Execute(null);
        Assert.Contains("ChannelCount", project.Editor.SetupParameterDefaultsError);
        Assert.Same(run.Editor!.SetupParameters.Single(), Assert.Single(run.Editor.RepairSetupParameters));
    }

    [Fact]
    public void Editing_run_values_does_not_refresh_project_or_sibling_query_choices_and_locks_are_enforced()
    {
        var page = Open(); var first = page.Selected!; var project = first.Parent!;
        project.Editor!.ResultsQueryOutputs.Single().FixedParameters.Single(row => row.Key == "SortableDataFormat").UseAsQueryKeyCommand.Execute(null);
        page.Selected = project; page.AddRunCommand.Execute(null); var second = page.Selected!;
        var refreshes = 0;
        foreach (var editor in new[] { project.Editor, second.Editor! })
            editor.PropertyChanged += (_, args) => { if (args.PropertyName is "" or nameof(WorkspaceConfigurationViewModel.ResultsQueryOutputs)) refreshes++; };
        var span = Assert.Single(first.Editor!.DeclaredResultsParameters);
        span.AddValueCommand.Execute(null); span.Values[0].Text = "Ints";
        Assert.Equal(0, refreshes); Assert.Empty(second.Editor!.ResultsParameters.Single().Values);
        first.Editor.SetGeneratedParametersLock(true);
        Assert.False(span.AddValueCommand.CanExecute(null)); span.Values[0].Text = "Bools"; Assert.Equal("Ints", span.Values[0].Text);
        Assert.Null(typeof(WorkspaceParameterSpan).GetProperty("KeyChoices"));
        Assert.False(typeof(WorkspaceParameterSpan).GetProperty("Key")!.SetMethod!.IsPublic);
        Assert.Null(typeof(WorkspaceConfigurationViewModel).GetProperty("AddResultsParameterCommand"));
    }

    [Fact]
    public async Task Obsolete_and_duplicate_inputs_remain_repairable_without_losing_their_saved_types()
    {
        var page = Open(); var run = page.Selected!;
        var dto = run.Editor!.ToRunInputs(run.Id, run.Name, run.Parent!.Id);
        dto.resultsParameterSpans = [new() { keyName = "LegacyCount", values = ["7"], valueType = (int)parameterValueType.Integer }];
        run.Editor.LoadRun(dto);
        Assert.Empty(run.Editor.DeclaredResultsParameters);
        Assert.Equal(parameterValueType.Integer, Assert.Single(run.Editor.RepairResultsParameters).ValueType);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.OperationError);
        page = Open(); WorkspaceRunExecutionTests.SelectRun(page); run = page.Selected!;
        var retained = Assert.Single(run.Editor!.RepairResultsParameters); Assert.Equal("7", retained.Values[0].Text);
        Assert.Equal(parameterValueType.Integer, retained.ValueType);
        retained.RemoveCommand.Execute(null); Assert.Empty(run.Editor.ResultsParameters);
        run.Parent!.Editor!.ResultsQueryOutputs.Single().FixedParameters.Single(row => row.Key == "SortableDataFormat").UseAsQueryKeyCommand.Execute(null);
        var primary = run.Editor.ResultsParameters.Single(); primary.AddValueCommand.Execute(null); primary.Values[0].Text = "Bools";
        run.Editor.AddDraftSpan(false, "SortableDataFormat", ["Ints"]);
        Assert.Same(primary, Assert.Single(run.Editor.DeclaredResultsParameters));
        Assert.Contains("unique", run.Editor.ResultsParametersError);
        Assert.Single(run.Editor.RepairResultsParameters).RemoveCommand.Execute(null);
        Assert.Empty(run.Editor.ResultsParametersError); Assert.Equal("Bools", primary.Values[0].Text);
    }

    [Fact]
    public void Run_editor_renders_readonly_keys_and_separate_repair_actions()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var page = Open(); var run = page.Selected!;
                run.Parent!.Editor!.ResultsQueryOutputs.Single().FixedParameters.Single(row => row.Key == "SortableDataFormat").UseAsQueryKeyCommand.Execute(null);
                var span = Assert.Single(run.Editor!.DeclaredResultsParameters); span.AddValueCommand.Execute(null); span.Values[0].Text = "Bools";
                run.Editor.AddDraftSpan(false, "Repl", ["7"]);
                var view = new Sortopia.WPF.Views.Pages.Workspace.WorkspaceConfigurationEditor { DataContext = run.Editor, ActiveItemKey = "resultsSpans" };
                var border = new System.Windows.Controls.Border { Padding = new System.Windows.Thickness(20), Child = view,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 31)) };
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
                border.Measure(new System.Windows.Size(650, double.PositiveInfinity));
                border.Arrange(new System.Windows.Rect(0, 0, 650, border.DesiredSize.Height)); border.UpdateLayout();
                foreach (var repair in new[] { false, true })
                {
                    var list = (System.Windows.Controls.ItemsControl)view.FindName(repair ? "ResultsRepairParametersList" : "ResultsParametersList");
                    Assert.Single(list.Items);
                    var container = (System.Windows.Controls.ContentPresenter)list.ItemContainerGenerator.ContainerFromIndex(0);
                    var key = (System.Windows.Controls.TextBlock)list.ItemTemplate.FindName("ParameterKeyLabel", container);
                    Assert.Equal(repair ? "Repl" : "SortableDataFormat", key.Text);
                    var discard = (System.Windows.Controls.Button)list.ItemTemplate.FindName("DiscardRetainedParameterButton", container);
                    Assert.Equal(repair ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed, discard.Visibility);
                    Assert.True(list.ActualHeight > 0);
                }
                if (Environment.GetEnvironmentVariable("SORTOPIA_FIXED_SPANS_SCREENSHOT") is { Length: > 0 } path)
                {
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(650, (int)Math.Ceiling(border.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(border); var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var file = File.Create(path); png.Save(file);
                }
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Shared_contract_returns_structured_issues_for_editor_and_runtime_rules()
    {
        var contract = PhaseParameterContract.create(new[] { "ChannelCount", "SortableDataFormat", "Repl" },
            new[] { new[] { "ChannelCount" }, new[] { "ChannelCount", "Repl" } });
        Assert.Equal(new[] { "ChannelCount", "Repl" }, contract.SpanKeys);
        Assert.Equal(new[] { "SortableDataFormat" }, contract.DefaultKeys);
        var issues = PhaseParameterContract.keyIssues(contract, Array.Empty<string>(), new[] { "ChannelCount", "SortableDataFormat" });
        Assert.Contains(issues, issue => issue.Kind == parameterIssueKind.MissingDefault && issue.Key == "SortableDataFormat");
        Assert.Contains(issues, issue => issue.Kind == parameterIssueKind.MissingSpan && issue.Key == "Repl");
        Assert.Contains(issues, issue => issue.Kind == parameterIssueKind.UnexpectedSpan && issue.Key == "SortableDataFormat");
        Assert.Equal(parameterIssueKind.EmptySpan, Assert.Single(PhaseParameterContract.spanValuesIssues("ChannelCount", 0)).Kind);
    }

    public void Dispose() => temporary.Delete(true);
}
