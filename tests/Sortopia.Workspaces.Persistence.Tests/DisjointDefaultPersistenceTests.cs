using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class DisjointDefaultPersistenceTests
{
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
            var project = Sortopia.Workspaces.project.createInherited(Guid.NewGuid(), "Project", "Project", ModuleDefaults.toProject(effective),
                OverridesDto.toDomain(family, new projectOverridesDto { resultsQueries = [query], setupQueries = branch ? [query] : null }), []);
            var module = workspaceModule.createInherited(Guid.NewGuid(), "Detached", "Detached", inherited, local, [], [project]);
            var parentModule = workspaceModule.createInherited(Guid.NewGuid(), "Parent", "Parent", ModuleDefaults.configuration(branch), parentOverrides, [module], []);
            var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [parentModule]);
            var root = Path.Combine(temporary.FullName, "Workspace"); Directory.CreateDirectory(root);
            WorkspaceFileStore.CreateWorkspace(root, world, TestContext.Current.CancellationToken);
            var loaded = WorkspaceFileStore.LoadModuleFolder(Path.Combine(root, "modules", "Parent", "modules", "Detached"), TestContext.Current.CancellationToken);
            Assert.Equal(new[] { "ChannelCount" }, ModuleOverridesDto.fromDomain(loaded.LocalOverrides).excludedResultsDefaults);
            Assert.Equal(new[] { "SortableDataFormat" }, loaded.ResultsParameterDefaults.Select(binding => binding.KeyName));
            var saved = loaded.Projects.Single();
            Assert.Equal(new[] { "ChannelCount" }, saved.ResultsQueryParameterKeys);
            Assert.Equal(new[] { "SortableDataFormat" }, saved.ResultsParameterDefaults.Select(binding => binding.KeyName));
            var inputs = new runDto { schemaVersion = 14, id = Guid.NewGuid(), parentProjectId = saved.Id, runName = "Trial", runKind = branch ? "branch" : "root",
                resultsParameterSpans = [new() { keyName = "ChannelCount", values = ["8", "16"] }], setupParameterSpans = branch ? [new() { keyName = "ChannelCount", values = ["8", "16"] }] : [] };
            var run = RunDto.toDomain(saved, inputs);
            var sets = RunParameterSets.create(run); Assert.Equal(2, sets.Results.Length);
            if (branch) { Assert.Equal(2, sets.Setup.Value.Length); Assert.Equal(new[] { "ChannelCount" }, ModuleOverridesDto.fromDomain(loaded.LocalOverrides).excludedSetupDefaults); }
        }
        finally { temporary.Delete(true); }
    }
}
