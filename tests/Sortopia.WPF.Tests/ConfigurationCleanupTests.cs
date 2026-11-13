using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ConfigurationCleanupTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-contract-cleanup-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Selected_unknown_functions_fail_validation_before_execution(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Module", family: hierarchyKind.Branch);
        module.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings(true));
        if (setup) module.Editor.SetupFunctionName = "not-registered";
        else module.Editor.ResultsFunctionName = "not-registered";
        Assert.Contains("Unknown", setup ? module.Editor.SetupFunctionError : module.Editor.ResultsFunctionError);
        var settings = WorkspaceHierarchyTests.ProjectSettings(true);
        if (setup) settings.setupFunctionName = "not-registered";
        else settings.resultsFunctionName = "not-registered";
        Assert.Contains("Unknown", Assert.Throws<ArgumentException>(() => project.create(Guid.NewGuid(), "P", "P",
            SettingsDto.toDomain(hierarchyKind.Branch, settings), [])).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sealed_versions_round_trip_and_mismatched_execution_fails_before_work(bool setup)
    {
        var run = RunExecutionTests.MakeRun(true);
        var parent = project.restore(run.ParentProjectId, "Project", "Project",
            projectConfiguration.NewBranchProjectConfig(((run.BranchRun)run).Item.ProjectConfiguration), Defaults.inherited(true), true, [run]);
        var dto = ProjectDto.fromDomain(parent);
        if (setup)
        {
            dto.configuration.setupFunctionVersion = "previous";
            dto.configuration.setupDataTypes[0].dataType.value = "previous output";
        }
        else
        {
            dto.configuration.resultsFunctionVersion = "previous";
            dto.configuration.resultsDataTypes[0].dataType.value = "previous output";
        }
        var restored = WorkspaceCodec.decodeProject(Sortopia.Core.Serialization.MessagePackSetup.serialize(dto));
        var again = WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(restored));
        var saved = ((run.BranchRun)again.Runs[0]).Item.ProjectConfiguration;
        Assert.Equal("previous", setup ? saved.Setup.FunctionVersion : saved.Results.FunctionVersion);
        var report = RunExecution.execute(new CatalogTestHost(again.Runs[0], Token), null!);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Contains("fixed at version previous", report.Error);
        Assert.Empty(report.Setup); Assert.Empty(report.Results);
        var node = WorkspaceTreeNode.FromProject(again);
        var editor = node.Editor!;
        Assert.Equal("previous", setup ? editor.ToProjectSettings().setupFunctionVersion : editor.ToProjectSettings().resultsFunctionVersion);
        var runEditor = node.Children.Single().Editor!;
        Assert.Equal("previous output", setup ? runEditor.InheritedSetupDataTypes.First().DataType.Text : runEditor.ResultsOutputContracts.First().Text);
    }

    [Fact]
    public void Source_lookup_uses_the_sealed_version_after_the_catalog_has_changed()
    {
        var value = RunExecutionTests.MakeRun(false, WorkspaceHierarchyTests.ProjectSettings());
        var parent = project.restore(value.ParentProjectId, value.ProjectName, "Source",
            projectConfiguration.NewRootProjectConfig(((run.RootRun)value).Item.ProjectConfiguration), Defaults.inherited(false), true,
            [value]);
        var dto = ProjectDto.fromDomain(parent); dto.configuration.resultsFunctionVersion = "previous";
        parent = ProjectDto.toDomain(dto);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root,
            Guid.NewGuid(), "Sources", "Sources", [], [parent])]);
        world = WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token).Definition;
        parent = world.ProjectIndex[parent.Id];
        var config = parent.Snapshot.Results;
        var parameters = RunParameterSets.create(parent.Runs[0]).Results[0];
        var dataType = sortopiaDataType.NewSortableTests("");
        var payload = CatalogTestHost.Invoke(false, config.FunctionName, parameters).Head;
        var store = WorkspaceData.openProject(world, temporary.FullName, parent.Id);
        var plan = OutputPlan.create(parent.Id, config.DatabaseName, false, config.FunctionName, config.FunctionVersion,
            config.DataTypes, ResultsFunctionCatalog.require(config.FunctionName).OutputDependencies, parameters, ListModule.Empty<storedDataReference>());
        var writer = (IRunDatabase)new FileRunDatabase(store, plan, Token);
        writer.SaveIfMissing(dataType, ListModule.Empty<storedDataReference>(), FuncConvert.FromFunc<Unit, functionData>(_ => payload));
        var source = WorkspaceData.loadSource(world, temporary.FullName,
            new setupDataTypeConfig(dataType, parent.Id, ListModule.Empty<string>()), parameters, Token);
        Assert.Equal(writer.StoredOutputs.Head.Reference, source.Reference);
        var currentKey = DataKey.create(parent.Id, config.DatabaseName, false, config.FunctionName,
            ResultsFunctionCatalog.require(config.FunctionName).Version, dataType, config.DataTypes[dataType], parameters, ListModule.Empty<storedDataReference>());
        Assert.NotEqual(currentKey.Id, source.Reference.Id);
        Assert.IsType<functionData.SortableTests>(source.Value);
    }

    [Fact]
    public void Override_contracts_and_parameter_registry_exclude_removed_configuration()
    {
        foreach (var type in new[] { typeof(rootDefaultOverrides), typeof(branchDefaultOverrides), typeof(rootModuleOverrides), typeof(branchModuleOverrides) })
        {
            Assert.Null(type.GetProperty("ResultsSortopiaDataTypes"));
            Assert.Null(type.GetProperty("ResultsQueryParameterKeys"));
            Assert.Null(type.GetProperty("SetupDataTypes"));
        }
        Assert.Equal(new[] { "DataType", "SourceProjectId" }, typeof(setupSourceConfig).GetProperties().Select(p => p.Name));
        foreach (var name in new[] { "SourceGenerationInterval", "SourceMutationMod", "DatabaseName", "ProjectName", "RunName" })
            Assert.DoesNotContain(ParamKeys.all, key => key.Name == name);
        Assert.All(ResultsFunctionCatalog.functions.Values, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Version)));
        Assert.All(SetupFunctionCatalog.functions.Values, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Version)));
    }

    public void Dispose() => temporary.Delete(true);
}
