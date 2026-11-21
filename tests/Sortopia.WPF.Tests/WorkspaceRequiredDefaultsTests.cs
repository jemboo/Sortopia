using Sortopia.Functions;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceRequiredDefaultsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Population_preserves_values_and_inheritance_and_adds_only_missing_typed_entries(bool setup)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var parent = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Parent", family: family);
        if (setup) parent.Editor!.SetupFunctionName = SetupFunctionNames.uniformMssiSimpleSorterModels;
        else parent.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        var inherited = WorkspaceParameterInheritanceTests.AddDefault(parent.Editor, "ChannelCount", "16", setup);
        WorkspaceParameterInheritanceTests.AddDefault(parent.Editor, "Repl", "2", setup);
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child", family: family); parent.Children.Add(child);
        var editor = child.Editor!;
        var local = WorkspaceParameterInheritanceTests.AddDefault(editor, "Repl", "5", setup);
        var draft = WorkspaceParameterInheritanceTests.AddDefault(editor, "RngType", "unfinished edit", setup);
        var otherPhase = setup ? editor.ResultsParameterDefaults : editor.SetupParameterDefaults;
        var command = setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand;
        var rows = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        var changes = 0; command.CanExecuteChanged += (_, _) => changes++;

        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(4, rows.Count);
        Assert.Same(local, rows.Single(row => row.Key == "Repl")); Assert.Equal("5", local.Value.Text);
        Assert.Same(draft, rows.Single(row => row.Key == "RngType")); Assert.Equal("unfinished edit", draft.Value.Text);
        Assert.DoesNotContain(rows, row => row.Key == "ChannelCount");
        Assert.Empty(otherPhase);
        var added = rows.Except(new[] { local, draft }).ToArray();
        Assert.Equal(2, added.Length);
        Assert.All(added, row => { Assert.Equal(ParamKeys.tryFind(row.Key).Value.ValueType, row.ValueType); Assert.NotEmpty(row.Value.Text); Assert.Empty(row.Error); });
        Assert.False(command.CanExecute(null));
        command.Execute(null); Assert.Equal(4, rows.Count);
        Assert.True(changes > 0);

        var preview = Assert.Single(setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults);
        Assert.Same(parent.Editor, preview.Source.InheritedSource); Assert.Equal("16", preview.Value);
        inherited.Value.Text = "24";
        Assert.Equal("24", Assert.Single(setup ? editor.InheritedSetupDefaults : editor.InheritedOutputDefaults).Value);
        inherited.RemoveCommand.Execute(null);
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal("8", rows.Single(row => row.Key == "ChannelCount").Value.Text);
        Assert.Equal("5", local.Value.Text);
    }

    [Fact]
    public void Population_requires_a_module_catalog_function_and_a_missing_key()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module");
        var editor = module.Editor!;
        Assert.False(editor.AddRequiredResultsDefaultsCommand.CanExecute(null));
        editor.ResultsFunctionName = "unknown";
        Assert.False(editor.AddRequiredResultsDefaultsCommand.CanExecute(null));
        editor.ResultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        var child = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Child"); module.Children.Add(child);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); child.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        Assert.False(project.Editor!.AddRequiredResultsDefaultsCommand.CanExecute(null));
        Assert.False(run.Editor!.AddRequiredResultsDefaultsCommand.CanExecute(null));
        Assert.False(editor.AddRequiredSetupDefaultsCommand.CanExecute(null));

        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var library = editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId");
        Assert.Equal(parameterValueType.MergeLibId, library.ValueType); Assert.True(library.Value.IsLibraryId);
        Assert.False(child.Editor!.AddRequiredResultsDefaultsCommand.CanExecute(null));
        child.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.Empty(child.Editor.ResultsParameterDefaults);
        editor.ResultsFunctionName = ResultsFunctionNames.prefixSortableTests;
        Assert.True(editor.AddRequiredResultsDefaultsCommand.CanExecute(null));
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.Same(library, editor.ResultsParameterDefaults.Single(row => row.Key == "MergeLibId"));
        Assert.Equal(parameterValueType.PrefixLibId, editor.ResultsParameterDefaults.Single(row => row.Key == "PrefixLibId").ValueType);
        Assert.Equal(3, editor.ResultsParameterDefaults.Count);
    }

    [Fact]
    public void Every_catalog_function_gets_valid_nonblank_initial_parameters()
    {
        var functions = ResultsFunctionCatalog.functions.Select(entry => (Setup: false, Name: entry.Key, Keys: entry.Value.RequiredParameters,
                Support: entry.Value.ParameterSupport, InputError: entry.Value.InputError))
            .Concat(SetupFunctionCatalog.functions.Select(entry => (Setup: true, Name: entry.Key, Keys: entry.Value.RequiredParameters,
                Support: entry.Value.ParameterSupport, InputError: entry.Value.InputError)));
        foreach (var function in functions)
        {
            var suggestions = function.Support.Defaults.Invoke(new([], [], function.Keys));
            Assert.Equal(function.Keys.Select(key => key.Name), suggestions.Select(binding => binding.KeyName));
            Assert.Empty(function.InputError.Invoke(suggestions).Invoke([]));
            var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch).Editor!;
            if (function.Setup) editor.SetupFunctionName = function.Name; else editor.ResultsFunctionName = function.Name;
            (function.Setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
            var rows = function.Setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
            Assert.Equal(function.Keys.Select(key => key.Name).Order(), rows.Select(row => row.Key).Order());
            Assert.All(rows, row => { Assert.NotEmpty(row.Value.Text); Assert.Empty(row.Error); });
            var settings = editor.ToProjectSettings();
            var parameters = parameterSet.fromBindings((function.Setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(ParameterBindingDto.toDomain));
            Assert.Empty(FunctionParameters.validationError(function.Keys, parameters));
            if (function.Name == SetupFunctionNames.evaluationStandardMssiSimple)
            {
                var generated = CatalogTestHost.Invoke(true, SetupFunctionNames.evaluationStandardMssiSimple, parameters);
                Assert.Equal(100, ((sortopiaStorageData.SorterModelSet)generated.Single(value => value.IsSorterModelSet)).Item.Count);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_defaults_follow_source_keys_and_preserve_inherited_and_unfinished_values(bool realActivators)
    {
        string[] modelKeys = ["ChannelCount", "RngType", "Repl", "Seed6Weights", "UnfoldingWeights",
            .. realActivators ? new[] { "AMax", "AInitial" } : new[] { "ActivationProbability" }];
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", modelKeys);
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "SortableDataFormat");
        var parent = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Parent", () => [models, tests]);
        parent.SetupFunctionName = SetupFunctionNames.evaluationSource;
        foreach (var sourceRow in parent.SetupDataTypes) sourceRow.SourceOverride!.IsOverridden = true;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        WorkspaceParameterInheritanceTests.AddDefault(parent, "ChannelCount", "24", true);
        if (realActivators) WorkspaceParameterInheritanceTests.AddDefault(parent, "AMax", "7", true);
        var child = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => parent, () => "Child", () => [models, tests]);
        var draft = WorkspaceParameterInheritanceTests.AddDefault(child, "RngType", "unfinished edit", true);
        var repl = WorkspaceParameterInheritanceTests.AddDefault(child, "Repl", "5", true);

        child.AddRequiredSetupDefaultsCommand.Execute(null);
        var rows = child.SetupParameterDefaults;
        Assert.DoesNotContain(rows, row => row.Key == "ChannelCount" || row.Key == "AMax");
        Assert.Same(draft, rows.Single(row => row.Key == "RngType")); Assert.Equal("unfinished edit", draft.Value.Text);
        Assert.Same(repl, rows.Single(row => row.Key == "Repl")); Assert.Equal("5", repl.Value.Text);
        Assert.Equal("1,1,1;1,1,1", rows.Single(row => row.Key == "UnfoldingWeights").Value.Text);
        Assert.Equal("1,1,1,1,1,1,1", rows.Single(row => row.Key == "Seed6Weights").Value.Text);
        Assert.Equal("Bools", rows.Single(row => row.Key == "SortableDataFormat").Value.Text);
        Assert.Equal(realActivators ? "0" : "0.5", rows.Single(row => row.Key == (realActivators ? "AInitial" : "ActivationProbability")).Value.Text);
        Assert.DoesNotContain(rows, row => row.Key == "SorterSuccessFraction");
        Assert.False(child.AddRequiredSetupDefaultsCommand.CanExecute(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void New_library_defaults_match_the_inherited_channel_count(bool prefix)
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", prefix ? "PrefixLibId" : "MergeLibId");
        var parent = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Parent", () => [models, tests]);
        parent.SetupFunctionName = SetupFunctionNames.evaluationSource; foreach (var sourceRow in parent.SetupDataTypes) sourceRow.SourceOverride!.IsOverridden = true;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        parent.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        WorkspaceParameterInheritanceTests.AddDefault(parent, "ChannelCount", "16", true);
        var child = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => parent, () => "Child", () => [models, tests]);
        child.AddRequiredSetupDefaultsCommand.Execute(null);
        Assert.DoesNotContain(child.SetupParameterDefaults, row => row.Key == "ChannelCount");
        var text = child.SetupParameterDefaults.Single(row => row.Key == (prefix ? "PrefixLibId" : "MergeLibId")).Value.Text;
        if (prefix)
        {
            var id = PrefixLibId.fromString(text);
            Assert.Equal(16, id.ChannelCount); Assert.Equal(1, id.StageLength); Assert.Contains(id, PrefixLib.ids());
        }
        else
        {
            var id = MergeLibId.fromString(text);
            Assert.Equal(16, id.ChannelCount); Assert.Equal(2, id.MergeDimension); Assert.Contains(id, MergeLibId.availableForChannelCount(16));
        }
        Assert.All(child.SetupParameterDefaults, row => Assert.Empty(row.Error));
    }

    [Fact]
    public void New_channel_default_uses_existing_library_or_other_phase_without_editing_existing_values()
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "PrefixLibId");
        var editor = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Module", () => [models, tests]);
        editor.ResultsFunctionName = ResultsFunctionNames.sorterEval; editor.SetupFunctionName = SetupFunctionNames.evaluationSource;
        foreach (var sourceRow in editor.SetupDataTypes) sourceRow.SourceOverride!.IsOverridden = true;
        editor.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet").SourceProjectId = models.Id;
        editor.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").SourceProjectId = tests.Id;
        var library = PrefixLibId.toString(PrefixLib.ids().First(id => id.ChannelCount == 16 && id.StageLength == 1));
        var existing = WorkspaceParameterInheritanceTests.AddDefault(editor, "PrefixLibId", library, true);
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        Assert.Equal("16", editor.SetupParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text);
        Assert.Same(existing, editor.SetupParameterDefaults.Single(row => row.Key == "PrefixLibId")); Assert.Equal(library, existing.Value.Text);
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.DoesNotContain(editor.ResultsParameterDefaults, row => row.Key == "ChannelCount");
        var oldBlank = editor.ResultsParameterDefaults.Single(row => row.Key == "CollectSortableTests"); oldBlank.Value.Text = "";
        editor.ResultsParameterDefaults.Single(row => row.Key == "SorterEvalType").RemoveCommand.Execute(null);
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        Assert.Equal("", oldBlank.Value.Text); Assert.Equal("V1", editor.ResultsParameterDefaults.Single(row => row.Key == "SorterEvalType").Value.Text);
    }
}
