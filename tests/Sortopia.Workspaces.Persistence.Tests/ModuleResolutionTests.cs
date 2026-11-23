using MessagePack;
using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class ModuleResolutionTests
{
    private static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Integer) =>
        new() { keyName = key, value = value, valueType = (int)type };
    private static moduleConfiguration Resolve(bool branch, moduleConfiguration parent, moduleOverridesDto local) =>
        ModuleDefaults.resolve(parent, ModuleOverridesDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, local));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_modules_preserve_their_setup_phase_before_functions_are_selected(bool setup)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var child = WorkspaceFixtures.Module(family, Guid.NewGuid(), "Child", [], []);
        var parent = WorkspaceFixtures.Module(family, Guid.NewGuid(), "Parent", [child], []);
        var restored = WorkspaceDocumentModule.cloneModule(parent);
        foreach (var module in new[] { restored, Assert.Single(restored.Modules) })
        {
            Assert.Equal(setup, module.Configuration().HasSetup);
            Assert.Equal(family, module.Family());
            Assert.Equal("", module.Configuration().ResultsFunctionName);
            Assert.Empty(module.Configuration().ResultsSortopiaDataTypes);
            Assert.Equal(setup, ModuleDefaults.toProject(module.Configuration()).HasSetup);
            if (setup)
            {
                Assert.NotNull(module.Configuration().Setup);
                Assert.Equal("", module.Configuration().Setup.Value.FunctionName);
                Assert.Empty(module.Configuration().Setup.Value.SortopiaDataTypes);
                Assert.NotNull(module.LocalOverrides().Setup);
            }
            else
            {
                Assert.Null(module.Configuration().Setup);
                Assert.Null(module.LocalOverrides().Setup);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Modules_reject_setup_mismatches_in_overrides_children_projects_and_rebinding(bool setup)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var otherFamily = setup ? hierarchyKind.Root : hierarchyKind.Branch;
        var inherited = ModuleDefaults.configuration(setup);
        Assert.Equal("local", Assert.Throws<ArgumentException>(() => ModuleDefaults.resolve(inherited, ModuleDefaults.inherited(!setup))).ParamName);
        var other = WorkspaceFixtures.Module(otherFamily, Guid.NewGuid(), "Other", [], []);
        var parent = WorkspaceFixtures.Module(family, Guid.NewGuid(), "Parent", [], []);
        Assert.Equal("moduleKind", Assert.Throws<ArgumentException>(() => WorkspaceDocumentModule.validateModule(
            WorkspaceDocumentModule.withModuleChildren(parent, [other], []))).ParamName);
        var project = WorkspacePersistenceTests.Sample(!setup).Modules[0].Projects[0];
        Assert.Equal("projectKind", Assert.Throws<ArgumentException>(() => WorkspaceDocumentModule.validateModule(
            WorkspaceDocumentModule.withModuleChildren(parent, [], [project]))).ParamName);
        Assert.Equal("local", Assert.Throws<ArgumentException>(() => WorkspaceDefinitionResolution.moduleConfiguration(
            Microsoft.FSharp.Core.FSharpOption<moduleConfiguration>.Some(inherited), other.Definition)).ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_inheritance_merges_defaults_and_exclusions_without_owning_project_queries(bool branch)
    {
        parameterBindingDto[] defaults = [Binding("ChannelCount", "16"), Binding("SortableDataFormat", "Bools", parameterValueType.Text)];
        var parent = Resolve(branch, ModuleDefaults.configuration(branch), new() {
            description = "Parent", resultsFunctionName = ResultsFunctionNames.standardSortableTests, resultsParameterDefaults = defaults,
            setupFunctionName = branch ? SetupFunctionNames.standardTests : null, setupParameterDefaults = branch ? defaults : null });
        var child = Resolve(branch, parent, new() {
            resultsParameterDefaults = [Binding("ChannelCount", "8")],
            excludedResultsDefaults = ["SortableDataFormat"],
            setupParameterDefaults = branch ? [Binding("ChannelCount", "4")] : null });
        var settings = ModuleSettingsDto.fromDomain(child);
        Assert.Equal("Parent", settings.description);
        Assert.Equal("8", Assert.Single(settings.resultsParameterDefaults).value);
        if (branch)
        {
            Assert.Equal("4", settings.setupParameterDefaults.Single(row => row.keyName == "ChannelCount").value);
            Assert.Equal("Bools", settings.setupParameterDefaults.Single(row => row.keyName == "SortableDataFormat").value);
        }
        var inherited = ModuleDefaults.toProject(child);
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var projectSettings = SettingsDto.fromDomain(inherited);
        var query = projectSettings.resultsDataTypes.Single(); query.queryParameterKeys = ["SortableDataFormat"];
        var project = ProjectResolution.resolve(inherited, ProjectLocalSettingsDto.toDomain(inherited, new() { resultsQueries = [query] }));
        Assert.Equal(new[] { "SortableDataFormat" }, SettingsDto.fromDomain(project).resultsDataTypes.Single().queryParameterKeys);
        Assert.Empty(SettingsDto.fromDomain(ModuleDefaults.toProject(child)).resultsDataTypes.Single().queryParameterKeys);
        var json = MessagePackSerializer.ConvertToJson(MessagePackSetup.serialize(settings), MessagePackSetup.options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("queryParameterKeys", json);
        Assert.DoesNotContain("DatabaseName", json);
    }

    [Fact]
    public void Selecting_a_function_filters_inherited_defaults_but_rejects_unsupported_local_defaults()
    {
        var parent = Resolve(false, ModuleDefaults.configuration(false), new() {
            resultsParameterDefaults = [Binding("ChannelCount", "8"), Binding("SorterCount", "5"), Binding("SortableDataFormat", "Bools", parameterValueType.Text)] });
        var local = new moduleOverridesDto { resultsFunctionName = ResultsFunctionNames.standardSortableTests };
        var child = Resolve(false, parent, local);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, ModuleSettingsDto.fromDomain(child).resultsParameterDefaults.Select(row => row.keyName));
        local.resultsParameterDefaults = [Binding("SorterCount", "5")];
        Assert.Contains("not required", Assert.Throws<ArgumentException>(() => Resolve(false, parent, local)).Message);
        local.resultsParameterDefaults = [Binding("ChannelCount", "0")];
        Assert.Throws<ArgumentException>(() => Resolve(false, parent, local));
        local.resultsParameterDefaults = [Binding("ChannelCount", "4"), Binding("ChannelCount", "8")];
        Assert.Throws<ArgumentException>(() => Resolve(false, parent, local));
        local.resultsParameterDefaults = null; local.excludedResultsDefaults = ["ChannelCount", "ChannelCount"];
        Assert.Contains("unique", Assert.Throws<ArgumentException>(() => Resolve(false, parent, local)).Message);
    }

    [Fact]
    public void Source_assignments_inherit_by_type_and_explicit_clears_survive_module_serialization()
    {
        var models = Guid.NewGuid(); var tests = Guid.NewGuid(); var replacement = Guid.NewGuid();
        setupSourceConfigDto Source(string kind, Guid id) => new() { dataType = new() { kind = kind, value = "" }, sourceProjectId = id };
        var parent = Resolve(true, ModuleDefaults.configuration(true), new() {
            setupFunctionName = SetupFunctionNames.evaluationSource,
            setupSources = [Source("SorterModelSet", models), Source("SortableTests", tests)] });
        var child = Resolve(true, parent, new() { setupSources = [Source("SorterModelSet", replacement)] });
        var cleared = Resolve(true, child, new() { setupSources = [Source("SortableTests", Guid.Empty)] });
        var dto = ModuleSettingsDto.fromDomain(cleared);
        var restored = ModuleSettingsDto.toDomain(hierarchyKind.Branch,
            MessagePackSetup.deserialize<moduleSettingsDto>(MessagePackSetup.serialize(dto)));
        Assert.Equal(cleared, restored);
        Assert.Equal(replacement, dto.setupSources.Single(row => row.dataType.kind == "SorterModelSet").sourceProjectId);
        Assert.Equal(Guid.Empty, dto.setupSources.Single(row => row.dataType.kind == "SortableTests").sourceProjectId);
        var generated = Resolve(true, restored, new() { setupFunctionName = SetupFunctionNames.standardTests });
        Assert.Empty(ModuleSettingsDto.fromDomain(generated).setupSources);
        Assert.Empty(SettingsDto.fromDomain(ModuleDefaults.toProject(generated)).setupDataTypes.Single().queryParameterKeys);
    }
}
