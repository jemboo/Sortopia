using System.Globalization;
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

public sealed class WorkspaceParameterDefaultsTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-parameter-defaults-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static parameterBindingDto Default(string key, parameterValueType type, string value) => new() { keyName = key, valueType = (int)type, value = value };
    private static parameterSpanDto Span(string key, params string[] values) => new() { keyName = key, values = values };
    private static project Parent(bool branch, parameterBindingDto[] defaults, parameterBindingDto[]? setup = null)
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsParameterDefaults = defaults;
        settings.setupParameterDefaults = setup ?? [];
        return project.create(WorkspaceTestFiles.Id("parameter-defaults-project"), "Project", "Project", SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
    }
    private static runDto Inputs(bool branch, parameterSpanDto[]? spans = null, parameterSpanDto[]? setup = null) => new()
    {
        schemaVersion = 14, id = Guid.NewGuid(), runKind = branch ? "branch" : "root", parentProjectId = WorkspaceTestFiles.Id("parameter-defaults-project"), runName = "Run",
        resultsParameterSpans = spans ?? [], setupParameterSpans = setup ?? []
    };
    private static parameterSet[] Expand(parameterBindingDto[] defaults, params parameterSpanDto[] spans) => ParameterSets.create(
        ListModule.OfSeq(defaults.Select(ParameterBindingDto.toDomain)), ListModule.OfSeq(spans.Select(span => ParameterSpan.create(ParamKey.text(span.keyName), ListModule.OfArray(span.values)))));
    private static projectSettingsDto ModelSettings(bool branch)
    {
        var value = WorkspaceHierarchyTests.ProjectSettings(branch);
        value.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.uniformMssiSorterModels; value.resultsParameterDefaults = RunExecutionTests.UniformModelDefaults(8);
        if (branch) { value.setupFunctionName = Sortopia.Functions.SetupFunctionNames.uniformMssiSorterModels; value.setupParameterDefaults = RunExecutionTests.UniformModelDefaults(16); }
        return value;
    }
    private static parameterSet[] Sets(run value) => RunParameterSets.create(value).Results;
    private WorkspacePageViewModel Open()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        return new(() => { }, _ => null, settings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disjoint_inputs_fill_each_combination(bool branch)
    {
        var value = RunExecutionTests.MakeRun(branch, ModelSettings(branch), [Span("StageLength", "11", "13"), Span("ChannelCount", "4", "6", "8")]);
        var expanded = RunParameterSets.create(value); Assert.Equal(branch, expanded.Setup is not null);
        Assert.DoesNotContain(value.ResultsParameterDefaults, row => row.KeyName == "StageLength");
        Assert.Equal(new[] { "11:4", "11:6", "11:8", "13:4", "13:6", "13:8" }, expanded.Results.Select(set => $"{set.TryFindText("StageLength").Value}:{set.TryFindText("ChannelCount").Value}"));
        Assert.All(expanded.Results, set => Assert.Equal("Simple", set.TryFind(ParamKeys.sorterModelTypeKey).Value));
        if (branch) Assert.Equal(16, Assert.Single(expanded.Setup!.Value).TryFind(ParamKeys.channelCountKey).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parameter_expansion_without_defaults_or_spans_returns_one_empty_set(bool branch)
    {
        var results = Expand([]); Assert.Empty(Assert.Single(results).ParamMap);
        if (branch) { var setup = Expand([]); Assert.Empty(Assert.Single(setup).ParamMap); Assert.NotSame(results, setup); }
    }

    [Fact]
    public void Typed_defaults_and_spans_use_invariant_formats_and_existing_typed_accessors()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var id = Guid.NewGuid();
            parameterBindingDto[] defaults = [Default("Seed", parameterValueType.Integer, "+007"), Default("ChannelCount", parameterValueType.Integer, "8"),
                Default("SeedSoss", parameterValueType.Unsigned64, ulong.MaxValue.ToString(CultureInfo.InvariantCulture)),
                Default("ActivationProbability", parameterValueType.Floating, "2.5e-1"), Default("SetupCompleted", parameterValueType.Boolean, "true"),
                Default("Id", parameterValueType.Guid, id.ToString("B")), Default("Message", parameterValueType.Text, "")];
            var value = Assert.Single(Expand(defaults));
            Assert.Equal("7", value.TryFindText("Seed").Value);
            Assert.Equal(8, value.TryFind(ParamKeys.channelCountKey).Value);
            Assert.Equal(ulong.MaxValue, value.TryFind(ParamKeys.seedSossKey).Value);
            Assert.Equal(.25, value.TryFind(ParamKeys.activationProbabilityKey).Value);
            Assert.True(value.TryFind(ParamKeys.setupCompletedKey).Value);
            Assert.Equal(id, value.TryFind(ParamKeys.idKey).Value);
            Assert.Equal("", value.TryFind(ParamKeys.messageKey).Value);
            var sets = Expand(defaults.Where(binding => binding.keyName is not ("ActivationProbability" or "SetupCompleted")).ToArray(), Span("ActivationProbability", "0.5", "1e-1"), Span("SetupCompleted", "false"));
            Assert.Equal(new[] { "0.5", "0.1" }, sets.Select(set => set.TryFindText("ActivationProbability").Value));
            Assert.All(sets, set => Assert.Equal("False", set.TryFindText("SetupCompleted").Value));
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Theory]
    [InlineData(parameterValueType.Integer, "2147483648")]
    [InlineData(parameterValueType.Integer, "2.5")]
    [InlineData(parameterValueType.Unsigned64, "-1")]
    [InlineData(parameterValueType.Unsigned64, "18446744073709551616")]
    [InlineData(parameterValueType.Floating, "NaN")]
    [InlineData(parameterValueType.Floating, "Infinity")]
    [InlineData(parameterValueType.Floating, "0,5")]
    [InlineData(parameterValueType.Boolean, "yes")]
    [InlineData(parameterValueType.Guid, "not-a-guid")]
    [InlineData(parameterValueType.Text, null)]
    public void Invalid_values_are_rejected_at_the_domain_and_editor_boundaries(parameterValueType type, string? invalid)
    {
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("Custom", type, invalid!)));
        var field = new WorkspaceTypedValue(invalid!, () => type);
        Assert.NotEmpty(field.Error);
        parameterBindingDto[] defaults = [Default("Custom", type,
            type == parameterValueType.Guid ? Guid.Empty.ToString() : type == parameterValueType.Boolean ? "True" : type == parameterValueType.Text ? "" : "0")];
        Assert.Throws<ArgumentException>(() => Expand(defaults, Span("Custom", invalid!)));
    }

    [Fact]
    public void Duplicate_defaults_wrong_key_types_missing_query_values_and_empty_spans_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Parent(false, [Default("Seed", parameterValueType.Integer, "1"), Default("Seed", parameterValueType.Integer, "2")]));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("ChannelCount", parameterValueType.Text, "8")));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("", parameterValueType.Text, "")));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("Custom", (parameterValueType)99, "")));
        var parent = Parent(false, []);
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false))).Message);
        parent = Parent(false, [Default("ChannelCount", parameterValueType.Integer, "7")]);
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false, [Span("Seed")])));
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false, [Span("Seed", "8"), Span("Seed", "9")])));
        Assert.Single(ParameterSets.create(ListModule.Empty<parameterBinding>(), ListModule.Empty<parameterSpan>()));
        var excessive = ListModule.OfSeq(Enumerable.Range(0, 31).Select(i => ParameterSpan.create(ParamKey.text($"Key{i}"), ListModule.OfArray(new[] { "a", "b" }))));
        Assert.Throws<ArgumentException>(() => ParameterSets.create(ListModule.Empty<parameterBinding>(), excessive));
    }

    [Fact]
    public void Branch_setup_and_execution_defaults_and_spans_remain_independent_after_round_trip()
    {
        var value = RunExecutionTests.MakeRun(true, ModelSettings(true), [Span("Repl", "5", "7")], [Span("Repl", "10", "12", "14")]);
        var parent = project.restore(value.ParentProjectId, value.ProjectName, "Project", projectConfiguration.NewBranchProjectConfig(((run.BranchRun)value).Item.ProjectConfiguration), Defaults.inherited(true), true, [value]);
        var restored = WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(parent)); var expanded = RunParameterSets.create(restored.Runs[0]);
        Assert.Equal(new[] { 5, 7 }, expanded.Results.Select(set => set.TryFind(ParamKeys.replKey).Value));
        Assert.Equal(new[] { 10, 12, 14 }, expanded.Setup.Value.Select(set => set.TryFind(ParamKeys.replKey).Value));
        Assert.All(expanded.Results, set => Assert.Equal(8, set.TryFind(ParamKeys.channelCountKey).Value));
        Assert.All(expanded.Setup.Value, set => Assert.Equal(16, set.TryFind(ParamKeys.channelCountKey).Value));
        var dto = ProjectDto.fromDomain(restored); dto.runs[0].setupParameterSpans.Single(span => span.keyName == "Repl").values[0] = "invalid integer";
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeProject(MessagePackSetup.serialize(dto)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_defaults_inherit_through_modules_validate_run_values_and_survive_reopening(bool branch)
    {
        var page = Open(); (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); var module = page.Selected!;
        module.Editor!.LoadProject(ModelSettings(branch)); (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.AddProjectCommand.Execute(null); var parent = page.Selected!; page.AddRunCommand.Execute(null); var run = page.Selected!;
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null)); Assert.Same(module.Editor, run.Editor!.ResultsParameterDefaultsOverride.InheritedSource);
        parent.Editor!.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").OverrideCommand!.Execute(null);
        parent.Editor.ResultsParameterDefaults.Single().Value.Text = "4";
        module.Editor.ResultsParameterDefaults.Single(row => row.Key == "ChannelCount").Value.Text = "16";
        Assert.Equal("4", run.Editor.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
        parent.Editor.ResultsParameterDefaults.Single().RemoveCommand.Execute(null);
        Assert.Equal("16", run.Editor.InheritedOutputDefaults.Single(row => row.Key == "ChannelCount").Value);
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].Projects[0];
        Assert.Equal(16, Assert.Single(Sets(saved.Runs[0])).TryFind(ParamKeys.channelCountKey).Value);
    }

    [Fact]
    public void Default_rows_resolve_known_types_and_validate_custom_types_without_losing_invalid_drafts()
    {
        var row = new WorkspaceParameterBinding("ChannelCount", parameterValueType.Text, "bad", _ => { });
        Assert.False(row.IsCustomKey);
        Assert.Equal(parameterValueType.Integer, row.ValueType);
        Assert.NotEmpty(row.Error);
        row.ValueType = parameterValueType.Text;
        Assert.Equal(parameterValueType.Integer, row.ValueType);
        row.Key = "Custom switch"; row.ValueType = parameterValueType.Boolean;
        Assert.True(row.Value.IsBoolean);
        Assert.Equal("bad", row.Value.Text);
        Assert.Null(row.Value.BooleanValue);
        row.Value.BooleanValue = false;
        Assert.Equal("False", row.Value.Text);
        Assert.Empty(row.Error);
        row.Key = "Id";
        Assert.Equal(parameterValueType.Guid, row.ValueType);
        Assert.NotEmpty(row.Error);
        row.Value.Text = Guid.NewGuid().ToString(); Assert.Empty(row.Error);
        row.Key = "RngType";
        Assert.True(row.Value.HasChoices); Assert.False(row.Value.IsTextEditor);
        Assert.Contains("SplitMix64", row.Value.Choices); Assert.NotEmpty(row.Error);
        row.Value.Text = "SplitMix64"; Assert.Empty(row.Error);
        row.Key = "ChannelCount";
        Assert.Equal("SplitMix64", row.Value.Text); Assert.NotEmpty(row.Error);
        Assert.True(row.Value.IsTextEditor); Assert.False(row.Value.HasChoices);
        row.Value.Text = "0"; Assert.Contains("1 or greater", row.Error);
        row.Value.Text = "16"; Assert.Empty(row.Error);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Key_specific_default_validation_follows_inheritance_overrides_and_save(bool branch, bool setup, bool onProject)
    {
        var page = Open(); (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); var module = page.Selected!;
        module.Editor!.LoadProject(ModelSettings(branch)); page.AddProjectCommand.Execute(null); var project = page.Selected!;
        var owner = onProject ? project : module;
        if (onProject) (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults).Single(row => row.Key == "RngType").OverrideCommand!.Execute(null);
        var row = (setup ? owner.Editor!.SetupParameterDefaults : owner.Editor!.ResultsParameterDefaults).Single(row => row.Key == "RngType");
        row.Value.Text = "invalid RNG"; Assert.True(row.Value.HasChoices); Assert.NotEmpty(row.Error); Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        row.Value.Text = "SplitMix64"; Assert.Empty(row.Error);
        page.Selected = project; page.AddRunCommand.Execute(null); var editor = page.Selected!.Editor!;
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(page.Selected!, true);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var sets = RunParameterSets.create(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0]);
        Assert.Equal("SplitMix64", Assert.Single(setup ? sets.Setup.Value : sets.Results).TryFind(ParamKeys.rngTypeKey).Value);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
