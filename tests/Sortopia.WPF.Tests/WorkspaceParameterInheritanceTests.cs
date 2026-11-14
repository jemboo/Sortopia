using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceParameterInheritanceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-parameter-inheritance-");
    private string Root => Path.Combine(temporary.FullName, "GuiSpace");
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }
    internal static WorkspaceParameterBinding AddDefault(WorkspaceConfigurationViewModel editor, string key, string value, bool setup = false)
    {
        (setup ? editor.AddSetupParameterDefaultCommand : editor.AddResultsParameterDefaultCommand).Execute(null);
        var row = (setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults).Last();
        row.Key = key;
        row.Value.Text = value;
        return row;
    }
    internal static (WorkspaceTreeNode Parent, WorkspaceTreeNode Child) Hierarchy(WorkspacePageViewModel page, bool branch)
    {
        foreach (var name in new[] { "SortableTests", "Prefix", "cc16", "SingleStage" })
        {
            (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
            page.Selected!.Name = name;
        }
        var child = page.Selected!;
        var parent = child.Parent!;
        AddDefault(parent.Editor!, "ChannelCount", "16");
        AddDefault(child.Editor!, "StageLength", "1");
        if (branch)
        {
            AddDefault(parent.Editor!, "ChannelCount", "32", setup: true);
            AddDefault(child.Editor!, "StageLength", "2", setup: true);
        }
        return (parent, child);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Defaults_merge_by_key_keep_their_sources_and_remain_live_after_reopening(bool branch)
    {
        var page = Open();
        var (parent, child) = Hierarchy(page, branch);
        var inherited = Assert.Single(child.Editor!.InheritedOutputDefaults);
        Assert.Equal("ChannelCount", inherited.Key);
        Assert.Equal("16", inherited.Value);
        Assert.Same(parent.Editor, inherited.Source.InheritedSource);
        Assert.Equal("Inherited from cc16", inherited.Source.SourceLabel);
        Assert.Equal("StageLength", Assert.Single(child.Editor.LocalResultsParameterDefaults).Key);
        page.SelectValueSourceCommand.Execute(inherited.Source.InheritedSource);
        Assert.Same(parent, page.Selected);

        // Override one key without copying other ancestors' bindings into local storage.
        inherited.OverrideCommand!.Execute(null);
        var local = child.Editor.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount");
        Assert.Same(parent.Editor, local.Source!.InheritedSource);
        local.Value.Text = "8";
        Assert.Empty(child.Editor.InheritedOutputDefaults);
        parent.Editor!.ResultsParameterDefaults.Single().Value.Text = "20";
        Assert.Equal("8", local.Value.Text);
        parent.Name = "cc20";
        Assert.Contains("cc20", local.Source.SourceLabel);
        local.RemoveCommand.Execute(null);
        Assert.Equal("20", Assert.Single(child.Editor.InheritedOutputDefaults).Value);
        if (branch)
        {
            var setup = Assert.Single(child.Editor.InheritedSetupDefaults);
            Assert.Equal("32", setup.Value);
            Assert.Same(parent.Editor, setup.Source.InheritedSource);
            setup.OverrideCommand!.Execute(null);
            var setupLocal = child.Editor.SetupParameterDefaults.Single(value => value.Key == "ChannelCount");
            setupLocal.Value.Text = "64";
            Assert.Equal("20", Assert.Single(child.Editor.InheritedOutputDefaults).Value);
            setupLocal.RemoveCommand.Execute(null);
        }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].Modules[0].Modules[0];
        Assert.Equal(new[] { "ChannelCount", "StageLength" }, saved.ResultsParameterDefaults.Select(value => value.KeyName));
        Assert.Equal(new[] { "20", "1" }, saved.ResultsParameterDefaults.Select(value => value.FormattedValue));
        var overrides = ModuleOverridesDto.fromDomain(saved.LocalOverrides);
        Assert.Equal("StageLength", Assert.Single(overrides.resultsParameterDefaults).keyName);
        if (branch)
        {
            var settings = ModuleSettingsDto.fromDomain(saved.Configuration);
            Assert.Equal(new[] { "32", "2" }, settings.setupParameterDefaults.Select(value => value.value));
            Assert.Equal("StageLength", Assert.Single(overrides.setupParameterDefaults).keyName);
        }
        var reopened = Open();
        parent = reopened.Roots[0].Children[0].Children[0].Children[0];
        child = parent.Children[0];
        Assert.Same(parent.Editor, Assert.Single(child.Editor!.InheritedOutputDefaults).Source.InheritedSource);
        parent.Editor!.ResultsParameterDefaults.Single().Value.Text = "24";
        Assert.Equal("24", Assert.Single(child.Editor.InheritedOutputDefaults).Value);
        child.Editor.ResultsParameterDefaults.Single().RemoveCommand.Execute(null);
        await reopened.SaveWorkspaceAsync();
        Assert.Empty(reopened.Error);
        saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].Modules[0].Modules[0];
        Assert.Equal("24", Assert.Single(saved.ResultsParameterDefaults).FormattedValue);
        Assert.Empty(ModuleOverridesDto.fromDomain(saved.LocalOverrides).resultsParameterDefaults);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Runs_receive_merged_typed_defaults_and_sealing_stops_ancestor_changes(bool branch)
    {
        var page = Open(); var (ancestor, module) = Hierarchy(page, branch);
        module.Editor!.ResultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSorterModels;
        if (branch) module.Editor.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformMssiSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null); if (branch) module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        page.AddProjectCommand.Execute(null); var parent = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        Assert.Same(ancestor.Editor, run.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Source.InheritedSource);
        Assert.Same(module.Editor, run.Editor.InheritedOutputDefaults.Single(row => row.Key == "StageLength").Source.InheritedSource);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        ancestor = page.Roots[0].Children[0].Children[0].Children[0]; module = ancestor.Children[0]; parent = module.Children[0]; run = parent.Children[0];
        Assert.True(parent.IsSealed); ancestor.Editor!.ResultsParameterDefaults.Single().Value.Text = "24";
        Assert.Equal("16", run.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
        Assert.Equal(16, Assert.Single(run.Editor.ResultsParameterSetsPreview.Rows).ParameterSet.TryFind(ParamKeys.channelCountKey).Value);
    }

    [Fact]
    public void Duplicate_local_keys_are_rejected_even_when_they_override_an_inherited_key()
    {
        var page = Open();
        var (_, child) = Hierarchy(page, false);
        AddDefault(child.Editor!, "ChannelCount", "8");
        var duplicate = AddDefault(child.Editor!, "ChannelCount", "4");
        Assert.Contains("unique", child.Editor!.ResultsParameterDefaultsError);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        var inherited = ListModule.OfArray(new[] { ParameterBinding.create(ParamKeys.channelCountKey, 16) });
        var local = ListModule.OfArray(new[] { ParameterBinding.create(ParamKeys.channelCountKey, 8), ParameterBinding.create(ParamKeys.channelCountKey, 4) });
        Assert.Throws<ArgumentException>(() => Defaults.mergeParameterDefaults(inherited, local));
        duplicate.RemoveCommand.Execute(null);
        Assert.Empty(child.Editor.ResultsParameterDefaultsError);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
