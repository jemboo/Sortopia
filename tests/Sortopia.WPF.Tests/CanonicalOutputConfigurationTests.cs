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
    public void Project_and_module_round_trips_store_only_per_output_types_and_query_keys()
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings();
        settings.resultsDataTypes = [
            new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = [] },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, queryParameterKeys = ["ChannelCount", "Repl"] }
        ];
        var projectBytes = MessagePackSetup.serialize(settings);
        var moduleBytes = MessagePackSetup.serialize(ModuleSettingsDto.fromProjectSettings(settings));
        foreach (var bytes in new[] { projectBytes, moduleBytes })
        {
            var json = MessagePackSerializer.ConvertToJson(bytes, MessagePackSetup.options, TestContext.Current.CancellationToken);
            Assert.Contains("\"resultsDataTypes\"", json);
            Assert.DoesNotContain("resultsSortopiaDataTypes", json);
            Assert.DoesNotContain("resultsQueryParameterKeys", json);
        }
        foreach (var type in new[] { typeof(projectSettingsDto), typeof(moduleSettingsDto) })
        {
            Assert.Null(type.GetProperty("resultsSortopiaDataTypes"));
            Assert.Null(type.GetProperty("resultsQueryParameterKeys"));
        }
        var projectSettings = MessagePackSetup.deserialize<projectSettingsDto>(projectBytes);
        var moduleSettings = ModuleSettingsDto.toProjectSettings(MessagePackSetup.deserialize<moduleSettingsDto>(moduleBytes));
        foreach (var restored in new[] { projectSettings, moduleSettings })
        {
            Assert.Empty(restored.resultsDataTypes[0].queryParameterKeys);
            Assert.Equal(new[] { "ChannelCount", "Repl" }, restored.resultsDataTypes[1].queryParameterKeys);
            var source = new WorkspaceSourceProject(Guid.NewGuid(), "Source", true, restored,
                new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Source").Editor!);
            Assert.True(WorkspaceSourceContracts.HasOutput(source, "SortableTests", ""));
            Assert.True(WorkspaceSourceContracts.HasOutput(source, "SorterModelSet", ""));
            Assert.Empty(WorkspaceSourceContracts.Resolve(source, "SortableTests", "").QueryKeys);
            Assert.Equal(new[] { "ChannelCount", "Repl" }, WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "").QueryKeys);
        }
        var domain = SettingsDto.resultsToDomain(projectSettings);
        Assert.Equal(2, domain.SortopiaDataTypes.Length);
        Assert.Equal(new[] { "ChannelCount", "Repl" }, domain.QueryParameterKeys);
    }

    [Fact]
    public void Missing_canonical_outputs_are_rejected_instead_of_inferred()
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings();
        settings.resultsDataTypes = null!;
        Assert.Equal("resultsDataTypes", Assert.Throws<ArgumentException>(() => SettingsDto.toDomain(hierarchyKind.Root, settings)).ParamName);
        Assert.Equal("resultsDataTypes", Assert.Throws<ArgumentException>(() => SettingsDto.resultsToDomain(settings)).ParamName);
        var module = ModuleSettingsDto.fromProjectSettings(settings);
        Assert.Equal("resultsDataTypes", Assert.Throws<ArgumentException>(() => ModuleSettingsDto.toDomain(hierarchyKind.Root, module)).ParamName);
    }
}
