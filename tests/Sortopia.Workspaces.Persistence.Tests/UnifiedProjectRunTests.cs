using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class UnifiedProjectRunTests
{
    private static parameterBindingDto[] DefaultsFor(int width) => [
        new() { keyName = "ChannelCount", value = width.ToString(), valueType = (int)parameterValueType.Integer },
        new() { keyName = "SortableDataFormat", value = "Bools", valueType = (int)parameterValueType.Text }];

    private static projectConfiguration Inherited(bool setup, int width = 8)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var module = ModuleDefaults.resolve(ModuleDefaults.configuration(setup), ModuleOverridesDto.toDomain(family, new() {
            description = "Inherited description", resultsFunctionName = ResultsFunctionNames.standardSortableTests,
            resultsParameterDefaults = DefaultsFor(width), setupFunctionName = setup ? SetupFunctionNames.standardTests : null,
            setupParameterDefaults = setup ? DefaultsFor(width) : null }));
        return ModuleDefaults.toProject(module);
    }

    private static ProjectDocument Draft(bool setup) => WorkspaceFixtures.DraftProject(Guid.NewGuid(), "Project", Inherited(setup), ProjectResolution.initial(Inherited(setup)), []);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_owned_names_and_queries_do_not_inherit_and_survive_rebinding_and_sealing(bool setup)
    {
        var inherited = Inherited(setup);
        var settings = SettingsDto.fromDomain(inherited);
        settings.resultsDatabaseName = "Ancestor results";
        settings.resultsDataTypes[0].queryParameterKeys = ["ChannelCount"];
        settings.resultsParameterDefaults = settings.resultsParameterDefaults.Where(value => value.keyName != "ChannelCount").ToArray();
        if (setup)
        {
            settings.setupDatabaseName = "Ancestor setup";
            settings.setupDataTypes[0].queryParameterKeys = ["ChannelCount"];
            settings.setupParameterDefaults = settings.setupParameterDefaults.Where(value => value.keyName != "ChannelCount").ToArray();
        }
        var parent = SettingsDto.toDomain(setup ? hierarchyKind.Branch : hierarchyKind.Root, settings);
        var initial = ProjectResolution.initial(parent);
        Assert.Equal("Results", initial.ResultsDatabaseName);
        Assert.Empty(initial.ResultsQueries.Single().QueryParameterKeys);
        if (setup) { Assert.Equal("Setup", initial.Setup.Value.DatabaseName); Assert.Empty(initial.Setup.Value.Queries.Single().QueryParameterKeys); }
        var local = ProjectLocalSettingsDto.fromDomain(initial);
        local.resultsDatabaseName = "Own results";
        local.resultsQueries[0].queryParameterKeys = ["ChannelCount"];
        local.excludedResultsDefaults = ["ChannelCount"];
        if (setup)
        {
            local.setupDatabaseName = "Own setup"; local.setupQueries[0].queryParameterKeys = ["ChannelCount"];
            local.excludedSetupDefaults = ["ChannelCount"];
        }
        var draft = WorkspaceFixtures.DraftProject(Guid.NewGuid(), "Project", parent, ProjectLocalSettingsDto.toDomain(parent, local), []);
        var rebound = WorkspaceDefinitionResolution.projectConfiguration(FSharpOption<projectConfiguration>.Some(Inherited(setup, 16)), draft.Definition);
        Assert.Equal("Own results", rebound.ResultsDatabaseName);
        Assert.Equal(new[] { "ChannelCount" }, rebound.ResultsQueryParameterKeys);
        Assert.DoesNotContain(rebound.ResultsParameterDefaults, value => value.KeyName == "ChannelCount");
        var sealedProject = WorkspaceDocumentModule.cloneProject(draft);
        sealedProject.Definition.state = ProjectLifecycle.seal(SettingsDto.fromDomain(rebound), sealedProject.Definition.state);
        Assert.Equal(rebound, WorkspaceDefinitionResolution.projectConfiguration(FSharpOption<projectConfiguration>.Some(Inherited(setup, 32)), sealedProject.Definition));
        Assert.Equal(draft.LocalSettings(), sealedProject.LocalSettings());
        var restored = WorkspaceDocumentModule.cloneProject(sealedProject);
        Assert.Equal(sealedProject.Configuration(), restored.Configuration());
    }

    private static runConfig Inputs(ProjectDocument parent, FSharpOption<FSharpList<parameterSpan>>? setup) =>
        new(Guid.NewGuid(), "Run", parent.Definition.id, null, ListModule.Empty<parameterSpan>(), setup, null, null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_inputs_round_trip_without_sealing_the_draft_and_preserve_empty_setup(bool setup)
    {
        var draft = Draft(setup);
        Assert.False(draft.Definition.isSealed);
        Assert.Equal(draft.Configuration(), draft.Snapshot().Configuration);
        var parent = WorkspaceFixtures.Seal(draft);
        var spans = setup ? FSharpOption<FSharpList<parameterSpan>>.Some(ListModule.Empty<parameterSpan>()) : null;
        var run = Run.create(parent.Snapshot(), Inputs(parent, spans));
        Assert.Equal(setup, run.HasSetup);
        Assert.Equal("Inherited description", run.Description);
        var sets = RunParameterSets.create(run);
        Assert.Single(sets.Results);
        if (setup) Assert.Single(sets.Setup.Value);
        else Assert.Null(sets.Setup);

        var attached = WorkspaceDocumentModule.withRuns(draft, [RunDto.fromDomain(run)]);
        Assert.False(attached.Definition.isSealed);
        var restored = WorkspaceDocumentModule.cloneProject(attached);
        Assert.Equal(attached.Configuration(), restored.Configuration());
        var saved = Assert.Single(restored.ResolvedRuns());
        Assert.Equal(setup, saved.HasSetup);
        if (setup) Assert.Empty(saved.SetupParameterSpans.Value);
        else Assert.Null(saved.SetupParameterSpans);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Setup_inputs_overrides_and_parent_modules_must_match_the_project(bool setup)
    {
        var parent = WorkspaceFixtures.Seal(Draft(setup));
        var wrong = setup ? null : FSharpOption<FSharpList<parameterSpan>>.Some(ListModule.Empty<parameterSpan>());
        Assert.Equal("setupParameterSpans", Assert.Throws<ArgumentException>(() => Run.create(parent.Snapshot(), Inputs(parent, wrong))).ParamName);
        Assert.Throws<ArgumentException>(() => ProjectResolution.resolve(parent.Configuration(), ProjectResolution.initial(Inherited(!setup))));
        var draft = Draft(setup);
        Assert.Throws<ArgumentException>(() => WorkspaceDefinitionResolution.projectConfiguration(
            FSharpOption<projectConfiguration>.Some(Inherited(!setup)), draft.Definition));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Drafts_follow_module_changes_while_sealed_projects_and_runs_keep_their_snapshot(bool setup)
    {
        var draft = Draft(setup);
        var parent = WorkspaceFixtures.Seal(draft);
        var spans = setup ? FSharpOption<FSharpList<parameterSpan>>.Some(ListModule.Empty<parameterSpan>()) : null;
        var run = Run.create(parent.Snapshot(), Inputs(parent, spans));
        var updated = WorkspaceDefinitionResolution.projectConfiguration(FSharpOption<projectConfiguration>.Some(Inherited(setup, 16)), draft.Definition);
        Assert.False(draft.Definition.isSealed);
        Assert.Equal("16", updated.ResultsParameterDefaults.Single(binding => binding.KeyName == "ChannelCount").FormattedValue);
        Assert.Equal(parent.Configuration(), WorkspaceDefinitionResolution.projectConfiguration(FSharpOption<projectConfiguration>.Some(Inherited(setup, 16)), parent.Definition));
        Assert.Equal("8", run.ResultsParameterDefaults.Single(binding => binding.KeyName == "ChannelCount").FormattedValue);
        if (setup) Assert.Equal("8", run.Setup.Value.ParameterDefaults.Single(binding => binding.KeyName == "ChannelCount").FormattedValue);
        var changedSnapshot = ProjectSnapshot.create(parent.Definition.id, parent.Definition.projectName, updated);
        Assert.Equal("project", Assert.Throws<ArgumentException>(() => RunExecutionScopeModule.create(run,
            [new runProjectLocation(changedSnapshot.ResultsSnapshot, "Module/Project")])).ParamName);
    }
}
