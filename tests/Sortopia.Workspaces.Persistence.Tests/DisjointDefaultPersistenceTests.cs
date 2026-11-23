using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class DisjointDefaultPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Runtime_overrides_skip_excluded_draft_values_without_modifying_the_document(bool branch)
    {
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        parameterBindingDto[] defaults = [new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = "unfinished" },
            new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }];
        var local = new projectLocalSettingsDto {
            resultsParameterDefaults = defaults, excludedResultsDefaults = ["ChannelCount"],
            setupParameterDefaults = branch ? defaults : null, excludedSetupDefaults = branch ? ["ChannelCount"] : [] };
        var parent = ProjectResolution.configuration(branch);
        var runtime = ProjectLocalSettingsDto.fromDomain(ProjectLocalSettingsDto.toDomain(parent, local));
        Assert.Equal("SortableDataFormat", Assert.Single(runtime.resultsParameterDefaults).keyName);
        if (branch) Assert.Equal("SortableDataFormat", Assert.Single(runtime.setupParameterDefaults).keyName);
        var module = new moduleOverridesDto { resultsParameterDefaults = defaults, excludedResultsDefaults = ["ChannelCount"],
            setupParameterDefaults = branch ? defaults : null, excludedSetupDefaults = branch ? ["ChannelCount"] : [] };
        Assert.Equal("SortableDataFormat", Assert.Single(ModuleOverridesDto.fromDomain(ModuleOverridesDto.toDomain(family, module)).resultsParameterDefaults).keyName);
        Assert.Equal("unfinished", local.resultsParameterDefaults[0].value);
        local.excludedResultsDefaults = [];
        Assert.Throws<ArgumentException>(() => ProjectLocalSettingsDto.toDomain(parent, local));
        if (branch)
        {
            local.excludedResultsDefaults = ["ChannelCount"]; local.excludedSetupDefaults = [];
            Assert.Throws<ArgumentException>(() => ProjectLocalSettingsDto.toDomain(parent, local));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Excluded_defaults_and_project_queries_survive_detached_module_loading(bool branch)
    {
        var temporary = Directory.CreateTempSubdirectory("Sortopia-disjoint-defaults-");
        try
        {
            var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
            parameterBindingDto[] defaults = [new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = "16" }, new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }];
            var parentOverrides = ModuleOverridesDto.toDomain(family, new moduleOverridesDto {
                resultsFunctionName = "sortable-tests.standard", resultsParameterDefaults = defaults,
                setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : null, setupParameterDefaults = branch ? defaults : null });
            var inherited = ModuleDefaults.resolve(ModuleDefaults.configuration(branch), parentOverrides);
            var local = ModuleOverridesDto.toDomain(family, new moduleOverridesDto { excludedResultsDefaults = ["ChannelCount"], excludedSetupDefaults = branch ? ["ChannelCount"] : [] });
            var effective = ModuleDefaults.resolve(inherited, local);
            var query = new resultsDataTypeConfigDto { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = ["ChannelCount"] };
            var project = WorkspaceFixtures.DraftProject(Guid.NewGuid(), "Project", ModuleDefaults.toProject(effective),
                ProjectLocalSettingsDto.toDomain(ModuleDefaults.toProject(effective), new projectLocalSettingsDto { resultsQueries = [query], setupQueries = branch ? [query] : null }), []);
            var module = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Detached", inherited, local, [], [project]);
            var parentModule = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Parent", ModuleDefaults.configuration(branch), parentOverrides, [module], []);
            var world = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [parentModule]);
            var root = Path.Combine(temporary.FullName, "Workspace"); Directory.CreateDirectory(root);
            WorkspaceFileStore.CreateDocument(root, world, TestContext.Current.CancellationToken);
            var loaded = WorkspaceFileStore.LoadModuleDocument(Path.Combine(root, "Parent", "Detached"), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "ChannelCount" }, ModuleOverridesDto.fromDomain(loaded.LocalOverrides()).excludedResultsDefaults);
            Assert.Equal(new[] { "SortableDataFormat" }, loaded.Configuration().ResultsParameterDefaults.Select(binding => binding.KeyName));
            var saved = loaded.Projects.Single();
            Assert.Equal(new[] { "ChannelCount" }, saved.Configuration().ResultsQueryParameterKeys);
            Assert.Equal(new[] { "SortableDataFormat" }, saved.Configuration().ResultsParameterDefaults.Select(binding => binding.KeyName));
            var inputs = new runDto { schemaVersion = RunDtoSchema.version, id = Guid.NewGuid(), parentProjectId = saved.Definition.id, runName = "Trial", runKind = branch ? "branch" : "root",
                resultsParameterSpans = [new() { keyName = "ChannelCount", values = ["8", "16"] }], setupParameterSpans = branch ? [new() { keyName = "ChannelCount", values = ["8", "16"] }] : [] };
            var run = RunDto.toDomain(saved.Snapshot(), inputs);
            var sets = RunParameterSets.create(run); Assert.Equal(2, sets.Results.Length);
            if (branch) { Assert.Equal(2, sets.Setup.Value.Length); Assert.Equal(new[] { "ChannelCount" }, ModuleOverridesDto.fromDomain(loaded.LocalOverrides()).excludedSetupDefaults); }
        }
        finally { temporary.Delete(true); }
    }
}
