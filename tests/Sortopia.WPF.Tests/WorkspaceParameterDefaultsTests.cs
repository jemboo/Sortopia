using System.Globalization;
using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
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
        schemaVersion = 11, id = Guid.NewGuid(), runKind = branch ? "branch" : "root", parentProjectId = WorkspaceTestFiles.Id("parameter-defaults-project"), runName = "Run",
        resultsParameterSpans = spans ?? [], setupParameterSpans = setup ?? []
    };
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
    public void Defaults_fill_each_combination_and_spans_replace_matching_values(bool branch)
    {
        var parent = Parent(branch, [Default("Seed", parameterValueType.Integer, "7"), Default("ChannelCount", parameterValueType.Integer, "8"),
            Default("Message", parameterValueType.Text, " α, fixed\nvalue ")], branch ? [Default("Width", parameterValueType.Integer, "12")] : []);
        var run = RunDto.toDomain(parent, Inputs(branch, [Span("Seed", "11", "13"), Span("ChannelCount", "4", "6", "8")]));
        Assert.Equal("7", run.ResultsParameterDefaults.Single(value => value.KeyName == "Seed").FormattedValue);
        var expanded = RunParameterSets.create(run);
        Assert.Equal(branch, expanded.Setup is not null);
        var sets = expanded.Results;
        Assert.Equal(6, sets.Length);
        Assert.Equal(new[] { "11:4", "11:6", "11:8", "13:4", "13:6", "13:8" }, sets.Select(set => $"{set.TryFindText("Seed").Value}:{set.TryFindText("ChannelCount").Value}"));
        Assert.All(sets, set => Assert.Equal(" α, fixed\nvalue ", set.TryFindText("Message").Value));
        Assert.Equal("11", QueryParams.forResults(run, sortopiaDataType.NewSorterSetEval(""), sets[0]).Properties["Seed"]);
        var defaultsOnly = RunDto.toDomain(parent, Inputs(branch));
        Assert.Equal("7", Assert.Single(Sets(defaultsOnly)).TryFindText("Seed").Value);
        if (branch)
        {
            var setup = Assert.Single(RunParameterSets.create(defaultsOnly).Setup.Value);
            Assert.Equal("12", setup.TryFindText("Width").Value);
            Assert.Null(setup.TryFindText("Seed"));
            Assert.Null(sets[0].TryFindText("Width"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runs_without_defaults_or_spans_have_one_empty_set_per_function(bool branch)
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsQueryParameterKeys = [];
        var parent = project.create(WorkspaceTestFiles.Id("parameter-defaults-project"), "Project", "Project",
            SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
        var expanded = RunParameterSets.create(RunDto.toDomain(parent, Inputs(branch)));
        Assert.Empty(Assert.Single(expanded.Results).ParamMap);
        if (branch)
        {
            Assert.Empty(Assert.Single(expanded.Setup.Value).ParamMap);
            Assert.NotSame(expanded.Results, expanded.Setup.Value);
        }
        else Assert.Null(expanded.Setup);
    }

    [Fact]
    public void Typed_defaults_and_spans_use_invariant_formats_and_existing_typed_accessors()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var id = Guid.NewGuid();
            var parent = Parent(false, [Default("Seed", parameterValueType.Integer, "+007"), Default("ChannelCount", parameterValueType.Integer, "8"),
                Default("SeedSoss", parameterValueType.Unsigned64, ulong.MaxValue.ToString(CultureInfo.InvariantCulture)),
                Default("ActivationProbability", parameterValueType.Floating, "2.5e-1"), Default("SetupCompleted", parameterValueType.Boolean, "true"),
                Default("Id", parameterValueType.Guid, id.ToString("B")), Default("Message", parameterValueType.Text, "")]);
            var value = Assert.Single(Sets(RunDto.toDomain(parent, Inputs(false))));
            Assert.Equal("7", value.TryFindText("Seed").Value);
            Assert.Equal(8, value.TryFind(ParamKeys.channelCountKey).Value);
            Assert.Equal(ulong.MaxValue, value.TryFind(ParamKeys.seedSossKey).Value);
            Assert.Equal(.25, value.TryFind(ParamKeys.activationProbabilityKey).Value);
            Assert.True(value.TryFind(ParamKeys.setupCompletedKey).Value);
            Assert.Equal(id, value.TryFind(ParamKeys.idKey).Value);
            Assert.Equal("", value.TryFind(ParamKeys.messageKey).Value);
            var spanRun = RunDto.toDomain(parent, Inputs(false, [Span("ActivationProbability", "0.5", "1e-1"), Span("SetupCompleted", "false")]));
            var sets = Sets(spanRun);
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
        var parent = Parent(false, [Default("Seed", parameterValueType.Integer, "7"), Default("Custom", type,
            type == parameterValueType.Guid ? Guid.Empty.ToString() : type == parameterValueType.Boolean ? "True" : type == parameterValueType.Text ? "" : "0")]);
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false, [Span("Custom", invalid!)])));
    }

    [Fact]
    public void Duplicate_defaults_wrong_key_types_missing_query_values_and_empty_spans_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => Parent(false, [Default("Seed", parameterValueType.Integer, "1"), Default("Seed", parameterValueType.Integer, "2")]));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("ChannelCount", parameterValueType.Text, "8")));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("", parameterValueType.Text, "")));
        Assert.Throws<ArgumentException>(() => ParameterBindingDto.toDomain(Default("Custom", (parameterValueType)99, "")));
        var parent = Parent(false, []);
        Assert.Contains("Seed", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false))).Message);
        parent = Parent(false, [Default("Seed", parameterValueType.Integer, "7")]);
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false, [Span("Seed")])));
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, Inputs(false, [Span("Seed", "8"), Span("Seed", "9")])));
        Assert.Single(ParameterSets.create(ListModule.Empty<parameterBinding>(), ListModule.Empty<parameterSpan>()));
        var excessive = ListModule.OfSeq(Enumerable.Range(0, 31).Select(i => ParameterSpan.create(ParamKey.text($"Key{i}"), ListModule.OfArray(new[] { "a", "b" }))));
        Assert.Throws<ArgumentException>(() => ParameterSets.create(ListModule.Empty<parameterBinding>(), excessive));
    }

    [Fact]
    public void Branch_setup_and_execution_defaults_and_spans_remain_independent_after_round_trip()
    {
        var parent = Parent(true, [Default("Seed", parameterValueType.Integer, "3"), Default("ChannelCount", parameterValueType.Integer, "8")],
            [Default("Width", parameterValueType.Integer, "4"), Default("ChannelCount", parameterValueType.Integer, "16")]);
        var value = RunDto.toDomain(parent, Inputs(true, [Span("Seed", "5", "7")], [Span("Width", "10", "12", "14")]));
        parent = project.create(parent.Id, parent.ProjectName, parent.FolderName, parent.Configuration, [value]);
        var restored = WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(parent));
        var saved = ((run.BranchRun)restored.Runs[0]).Item;
        var expanded = RunParameterSets.create(restored.Runs[0]);
        Assert.Equal(2, expanded.Results.Length);
        Assert.Equal(3, expanded.Setup.Value.Length);
        Assert.Equal(new[] { 5, 7 }, expanded.Results.Select(set => Assert.IsType<parameterValue.IntegerValue>(set.TryFind("Seed").Value.Value).Item));
        Assert.Equal(new[] { 10, 12, 14 }, expanded.Setup.Value.Select(set => Assert.IsType<parameterValue.IntegerValue>(set.TryFind("Width").Value.Value).Item));
        Assert.All(expanded.Results, set => { Assert.Equal(8, set.TryFind(ParamKeys.channelCountKey).Value); Assert.Null(set.TryFind("Width")); });
        Assert.All(expanded.Setup.Value, set => { Assert.Equal(16, set.TryFind(ParamKeys.channelCountKey).Value); Assert.Null(set.TryFind("Seed")); });
        Assert.Equal(parent.ResultsParameterDefaults, restored.ResultsParameterDefaults);
        Assert.Equal("4", saved.SetupParameterDefaults.Single(defaultValue => defaultValue.KeyName == "Width").FormattedValue);
        var dto = ProjectDto.fromDomain(restored);
        dto.localOverrides.setupParameterDefaults[0].value = "invalid integer";
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeProject(MessagePackSetup.serialize(dto)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_defaults_inherit_through_modules_validate_run_values_and_survive_reopening(bool branch)
    {
        var page = Open(); (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsParameterDefaults = [Default("Seed", parameterValueType.Integer, "7"), Default("ChannelCount", parameterValueType.Integer, "8")];
        settings.setupParameterDefaults = branch ? [Default("Width", parameterValueType.Integer, "12")] : [];
        module.Editor!.LoadProject(settings);
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.AddProjectCommand.Execute(null); var parent = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        Assert.Empty(page.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.False(run.Editor!.HasMissingResultsParameters);
        Assert.Same(module.Editor, run.Editor.ResultsParameterDefaultsOverride.InheritedSource);
        run.Editor.AddResultsParameterCommand.Execute(null);
        var span = run.Editor.ResultsParameters.Single(); span.Key = "Seed"; span.Values.Single().Text = "wrong";
        Assert.Equal(parameterValueType.Integer, span.ValueType);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        span.Values.Single().Text = "11";
        Assert.Equal("Overridden by run span", run.Editor.InheritedOutputDefaults.Single(value => value.Key == "Seed").Usage);
        parent.Editor!.InheritedOutputDefaults.Single(value => value.Key == "ChannelCount").OverrideCommand!.Execute(null);
        Assert.Single(parent.Editor.ResultsParameterDefaults);
        parent.Editor.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "4";
        module.Editor.ResultsParameterDefaults.Single(value => value.Key == "ChannelCount").Value.Text = "16";
        Assert.Equal("4", run.Editor.InheritedOutputDefaults.Single(value => value.Key == "ChannelCount").Value);
        parent.Editor.ResultsParameterDefaults.Single().RemoveCommand.Execute(null);
        Assert.Equal("16", run.Editor.InheritedOutputDefaults.Single(value => value.Key == "ChannelCount").Value);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var savedProject = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].Projects[0];
        Assert.Equal(2, OverridesDto.fromDomain(savedProject.LocalOverrides).resultsParameterDefaults.Length);
        var set = Assert.Single(Sets(savedProject.Runs[0]));
        Assert.Equal("11", set.TryFindText("Seed").Value);
        Assert.Equal(16, set.TryFind(ParamKeys.channelCountKey).Value);
        var reopened = Open();
        run = reopened.Roots[0].Children[0].Children[0].Children[0].Children[0];
        Assert.Equal(parameterValueType.Integer, run.Editor!.ResultsParameters[0].ValueType);
        Assert.Equal("16", run.Editor.InheritedOutputDefaults.Single(value => value.Key == "ChannelCount").Value);
        run.Editor.ResultsParameters.Clear();
        Assert.Empty(reopened.Error);
        parent = run.Parent!;
        parent.Editor!.ResultsParameterDefaultsOverride.IsOverridden = true;
        parent.Editor.ResultsParameterDefaults.Clear();
        Assert.False(run.Editor.HasMissingResultsParameters);
        Assert.True(parent.IsSealed);
        parent.Editor.ResultsParameterDefaultsOverride.IsOverridden = false;
        Assert.Empty(reopened.Error);
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
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        var module = page.Selected!;
        module.Editor!.LoadProject(RunExecutionTests.Settings(branch));
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        var owner = onProject ? project : module;
        var row = WorkspaceParameterInheritanceTests.AddDefault(owner.Editor!, "RngType", "invalid RNG", setup);
        Assert.True(row.Value.HasChoices); Assert.Contains("RngType", row.Error);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Contains(page.SaveIssues, issue => ReferenceEquals(issue.Node, owner)
            && issue.Section == (setup ? "Setup parameter defaults" : "Results parameter defaults") && issue.Message.Contains("RngType"));
        Assert.Empty((setup ? run.Editor!.SetupParameterSetsPreview : run.Editor!.ResultsParameterSetsPreview).Rows);
        if (branch) Assert.Single((setup ? run.Editor.ResultsParameterSetsPreview : run.Editor.SetupParameterSetsPreview).Rows);

        row.Value.Text = "SplitMix64";
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        Assert.Single((setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview).Rows);
        Assert.Same(owner.Editor, (setup ? run.Editor.InheritedSetupDefaults : run.Editor.InheritedOutputDefaults)
            .Single(value => value.Key == "RngType").Source.InheritedSource);
        if (!onProject)
        {
            (setup ? project.Editor!.InheritedSetupDefaults : project.Editor!.InheritedOutputDefaults)
                .Single(value => value.Key == "RngType").OverrideCommand!.Execute(null);
            var local = (setup ? project.Editor.SetupParameterDefaults : project.Editor.ResultsParameterDefaults).Single();
            Assert.True(local.Value.HasChoices); Assert.Equal("SplitMix64", local.Value.Text);
            Assert.Same(module.Editor, local.Source!.InheritedSource);
            local.Value.Text = "NET"; Assert.NotEmpty(local.Error); Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
            local.Value.Text = "LCG"; Assert.Empty(local.Error); Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
            local.RemoveCommand.Execute(null);
            Assert.Equal("SplitMix64", (setup ? run.Editor.InheritedSetupDefaults : run.Editor.InheritedOutputDefaults)
                .Single(value => value.Key == "RngType").Value);
        }
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var savedRun = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0];
        var sets = RunParameterSets.create(savedRun);
        Assert.Equal("SplitMix64", Assert.Single(setup ? sets.Setup.Value : sets.Results).TryFind(ParamKeys.rngTypeKey).Value);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
