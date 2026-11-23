using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceLocalSettingsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_names_queries_and_exclusions_survive_sealing_reopening_and_resuming(bool setup)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var root = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, "Workspace");
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: family); root.Children.Add(module);
        module.Editor!.LoadModule(WorkspaceHierarchyTests.ModuleSettings(setup));
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: family); module.Children.Add(node);
        var editor = node.Editor!;
        editor.ResultsDatabaseName = "My results";
        editor.ResultsQueryOutputs.Single().FixedParameters.Single(value => value.Key == "ChannelCount").UseAsQueryKeyCommand.Execute(null);
        if (setup)
        {
            editor.SetupDatabaseName = "My setup";
            editor.SetupQueryOutputs.Single().FixedParameters.Single(value => value.Key == "ChannelCount").UseAsQueryKeyCommand.Execute(null);
        }
        var local = editor.ToLocalSettings();
        var snapshot = WorkspaceFixtures.Seal(WorkspaceFixtures.Project(node.Id, node.Name, SettingsDto.toDomain(family, editor.ToProjectSettings()), []));
        node.ApplyProjectState(snapshot.Definition.state, preserveDraft: true);
        editor.ResultsDatabaseName = "Blocked";
        Assert.Throws<InvalidOperationException>(() => editor.LoadLocalSettings(new() { resultsDatabaseName = "Blocked" }));
        Assert.Equal("My results", editor.ResultsDatabaseName);
        Assert.Equal(new[] { "ChannelCount" }, editor.ToProjectSettings().resultsDataTypes.Single().queryParameterKeys);

        var document = root.ToDocument();
        Assert.Equal(MessagePackSetup.serialize(local), MessagePackSetup.serialize(document.Modules[0].Projects[0].Definition.localSettings));
        var reopened = WorkspaceTreeNode.FromDocument(document);
        node = reopened.Children[0].Children[0]; editor = node.Editor!;
        Assert.True(node.IsSealed); Assert.False(editor.ResultsDatabaseField.CanEditValue);
        node.ApplyProjectState(projectStateDto.NewDraft(local, module.Editor!.ToProjectSettings(), null!));
        Assert.False(node.IsSealed); Assert.True(editor.ResultsDatabaseField.CanEditValue);
        Assert.Equal(MessagePackSetup.serialize(local), MessagePackSetup.serialize(editor.ToLocalSettings()));
        editor.ExcludedResultsDefaults.Single(value => value.Key == "ChannelCount").RestoreCommand.Execute(null);
        Assert.Empty(editor.ToProjectSettings().resultsDataTypes.Single().queryParameterKeys);
        Assert.Equal("8", editor.ToProjectSettings().resultsParameterDefaults.Single(value => value.keyName == "ChannelCount").value);
        if (setup) Assert.Equal("My setup", editor.SetupDatabaseName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_editor_preserves_excluded_raw_defaults_and_rejects_locked_loads(bool setup)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: setup ? hierarchyKind.Branch : hierarchyKind.Root);
        var editor = node.Editor!;
        editor.LoadModule(WorkspaceHierarchyTests.ModuleSettings(setup));
        var local = editor.ToModuleOverrides();
        local.resultsParameterDefaults.Single(value => value.keyName == "ChannelCount").value = "unfinished";
        local.excludedResultsDefaults = ["ChannelCount"];
        if (setup)
        {
            local.setupParameterDefaults.Single(value => value.keyName == "ChannelCount").value = "unfinished";
            local.excludedSetupDefaults = ["ChannelCount"];
        }
        editor.LoadModuleOverrides(local);
        var saved = editor.ToModuleOverrides();
        Assert.Equal("unfinished", saved.resultsParameterDefaults.Single(value => value.keyName == "ChannelCount").value);
        Assert.Equal(new[] { "ChannelCount" }, saved.excludedResultsDefaults);
        Assert.DoesNotContain(editor.ToModuleSettings().resultsParameterDefaults, value => value.keyName == "ChannelCount");
        node.SetGeneratedParametersLock(true);
        Assert.Throws<InvalidOperationException>(() => editor.LoadModuleOverrides(new() { description = "Blocked" }));
        Assert.Equal(MessagePackSetup.serialize(saved), MessagePackSetup.serialize(editor.ToModuleOverrides()));
        node.SetGeneratedParametersLock(false);
        editor.LoadModuleOverrides(saved);
        Assert.Equal(MessagePackSetup.serialize(saved), MessagePackSetup.serialize(editor.ToModuleOverrides()));
        Assert.Throws<InvalidOperationException>(() => editor.LoadLocalSettings(new()));
    }
}
