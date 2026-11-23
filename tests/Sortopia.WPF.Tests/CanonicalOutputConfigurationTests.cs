using MessagePack;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class CanonicalOutputConfigurationTests
{
    [Fact]
    public void Projects_store_per_output_query_keys_and_source_readers_preserve_them()
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings();
        settings.resultsDataTypes = [
            new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = [] },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, queryParameterKeys = ["ChannelCount", "Repl"] }
        ];
        var bytes = MessagePackSetup.serialize(settings);
        var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackSetup.options, TestContext.Current.CancellationToken);
        Assert.Contains("\"resultsDataTypes\"", json);
        Assert.DoesNotContain("resultsSortopiaDataTypes", json);
        Assert.DoesNotContain("resultsQueryParameterKeys", json);
        var restored = MessagePackSetup.deserialize<projectSettingsDto>(bytes);
        Assert.Empty(restored.resultsDataTypes[0].queryParameterKeys);
        Assert.Equal(new[] { "ChannelCount", "Repl" }, restored.resultsDataTypes[1].queryParameterKeys);
        var source = new WorkspaceSourceProject(Guid.NewGuid(), "Source", true, restored,
            new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Source").Editor!);
        Assert.True(WorkspaceSourceContracts.HasOutput(source, "SortableTests", ""));
        Assert.True(WorkspaceSourceContracts.HasOutput(source, "SorterModelSet", ""));
        Assert.Empty(WorkspaceSourceContracts.Resolve(source, "SortableTests", "").QueryKeys);
        Assert.Equal(new[] { "ChannelCount", "Repl" }, WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "").QueryKeys);
        var domain = SettingsDto.resultsToDomain(restored);
        Assert.Equal(2, domain.SortopiaDataTypes.Length);
        Assert.Equal(new[] { "ChannelCount", "Repl" }, domain.QueryParameterKeys);
    }

    [Fact]
    public void Modules_store_functions_defaults_and_sources_without_project_query_or_database_fields()
    {
        var dto = WorkspaceHierarchyTests.ModuleSettings(true);
        var bytes = MessagePackSetup.serialize(dto);
        var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackSetup.options, TestContext.Current.CancellationToken);
        foreach (var field in new[] { "resultsDataTypes", "setupDataTypes", "queryParameterKeys", "DatabaseName" })
            Assert.DoesNotContain(field, json);
        Assert.Contains("setupSources", json);
        var restored = ModuleSettingsDto.toDomain(hierarchyKind.Branch, MessagePackSetup.deserialize<moduleSettingsDto>(bytes));
        Assert.Equal(bytes, MessagePackSetup.serialize(ModuleSettingsDto.fromDomain(restored)));
        var project = ModuleDefaults.toProject(restored);
        Assert.All(SettingsDto.fromDomain(project).resultsDataTypes, output => Assert.Empty(output.queryParameterKeys));
        Assert.All(SettingsDto.fromDomain(project).setupDataTypes, output => Assert.Empty(output.queryParameterKeys));
        foreach (var type in new[] { typeof(moduleConfiguration), typeof(moduleSetupConfig), typeof(ModuleDocument) })
        {
            Assert.Null(type.GetProperty("ResultsQueryParameterKeys"));
            Assert.Null(type.GetProperty("SetupQueryParameterKeys"));
            Assert.Null(type.GetProperty("SetupDataTypes"));
        }
    }

    [Fact]
    public void Projects_require_saved_outputs_while_modules_derive_them_from_the_function()
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings();
        settings.resultsDataTypes = null!;
        Assert.Equal("resultsDataTypes", Assert.Throws<ArgumentException>(() => SettingsDto.toDomain(hierarchyKind.Root, settings)).ParamName);
        Assert.Equal("resultsDataTypes", Assert.Throws<ArgumentException>(() => SettingsDto.resultsToDomain(settings)).ParamName);
        var module = ModuleSettingsDto.toDomain(hierarchyKind.Root, WorkspaceHierarchyTests.ModuleSettings());
        Assert.Equal(Sortopia.Functions.ResultsFunctionCatalog.require(settings.resultsFunctionName).SortopiaDataTypes,
            module.ResultsSortopiaDataTypes);
    }
}
