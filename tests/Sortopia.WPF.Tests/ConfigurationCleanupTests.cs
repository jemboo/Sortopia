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
        module.Editor!.LoadFixtureSettings(WorkspaceHierarchyTests.ProjectSettings(true));
        if (setup) module.Editor.SetupFunctionName = "not-registered";
        else module.Editor.ResultsFunctionName = "not-registered";
        Assert.Contains("Unknown", setup ? module.Editor.SetupFunctionError : module.Editor.ResultsFunctionError);
        var settings = WorkspaceHierarchyTests.ProjectSettings(true);
        if (setup) settings.setupFunctionName = "not-registered";
        else settings.resultsFunctionName = "not-registered";
        Assert.Contains("Unknown", Assert.Throws<ArgumentException>(() => WorkspaceFixtures.Project(Guid.NewGuid(), "P",
            SettingsDto.toDomain(hierarchyKind.Branch, settings), [])).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sealed_versions_round_trip_and_mismatched_execution_fails_before_work(bool setup)
    {
        var run = RunExecutionTests.MakeRun(true);
        var parent = WorkspaceFixtures.RestoreProject(run.ParentProjectId, "Project",
            run.ProjectConfiguration, ProjectResolution.local(run.ProjectConfiguration), true, [run]);
        var dto = WorkspaceDocumentModule.cloneProject(parent);
        if (setup)
        {
            dto.Definition.configuration.setupFunctionVersion = "previous";
            dto.Definition.configuration.setupDataTypes[0].dataType.value = "previous output";
        }
        else
        {
            dto.Definition.configuration.resultsFunctionVersion = "previous";
            dto.Definition.configuration.resultsDataTypes[0].dataType.value = "previous output";
        }
        var restored = dto;
        var again = WorkspaceDocumentModule.cloneProject(restored);
        var saved = again.ResolvedRuns()[0].ProjectConfiguration;
        Assert.Equal("previous", setup ? saved.Setup.Value.FunctionVersion : saved.Results.FunctionVersion);
        var report = RunExecution.execute(new CatalogTestHost(again.ResolvedRuns()[0], Token), null!);
        Assert.Equal(runExecutionStatus.Failed, report.Status);
        Assert.Contains("fixed at version previous", report.Error);
        Assert.Empty(report.Setup); Assert.Empty(report.Results);
        var node = WorkspaceTreeNode.FromProjectDocument(again);
        var editor = node.Editor!;
        Assert.Equal("previous", setup ? editor.ToProjectSettings().setupFunctionVersion : editor.ToProjectSettings().resultsFunctionVersion);
        var runEditor = node.Children.Single().Editor!;
        Assert.Equal("previous output", setup ? runEditor.InheritedSetupDataTypes.First().DataType.Text : runEditor.ResultsOutputContracts.First().Text);
    }

    [Fact]
    public void Source_lookup_uses_the_sealed_version_after_the_catalog_has_changed()
    {
        var value = RunExecutionTests.MakeRun(false, WorkspaceHierarchyTests.ProjectSettings());
        var parent = WorkspaceFixtures.RestoreProject(value.ParentProjectId, "Source",
            value.ProjectConfiguration, ProjectResolution.local(value.ProjectConfiguration), true,
            [value]);
        var dto = parent; dto.Definition.configuration.resultsFunctionVersion = "previous";
        parent = dto;
        var modelSettings = RunExecutionTests.Settings(false);
        modelSettings.resultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        modelSettings.resultsParameterDefaults = RunExecutionTests.UniformModelDefaults(8);
        var models = RunExecutionTests.MakeProject("Models", false, modelSettings);
        var consumerSettings = RunExecutionTests.Settings(true);
        consumerSettings.setupFunctionName = SetupFunctionNames.evaluationSource;
        consumerSettings.setupParameterDefaults = [.. modelSettings.resultsParameterDefaults, RunExecutionTests.Binding("SortableDataFormat", "Bools")];
        consumerSettings.setupDataTypes = [new() { dataType = new() { kind = "SortableTests", value = "" }, sourceProjectId = parent.Definition.id, queryParameterKeys = [] },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = models.Definition.id, queryParameterKeys = [] }];
        var consumer = RunExecutionTests.MakeProject("Consumer", true, consumerSettings);
        var world = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Sources", [], [parent, models]),
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Consumers", [], [consumer])]);
        var document = WorkspaceFileStore.CreateDocument(temporary.FullName, world, Token).Workspace;
        var scope = document.ResolveRun(consumer.Runs[0].id);
        var config = parent.ResultsSnapshot().Results;
        var parameters = RunParameterSets.create(parent.ResolvedRuns()[0]).Results[0];
        var dataType = sortopiaDataType.NewSortableTests("");
        var payload = CatalogTestHost.Invoke(false, config.FunctionName, parameters).Head;
        var store = new SortopiaFileDb(WorkspaceData.scopedProjectFolder(scope, temporary.FullName, parent.Definition.id), parent.Definition.id);
        var plan = OutputPlan.create(parent.Definition.id, config.DatabaseName, false, config.FunctionName, config.FunctionVersion,
            ResultsFunctionCatalog.require(config.FunctionName).Outputs, config.DataTypes, parameters, ListModule.Empty<storedDataReference>());
        var storage = new FileRunDatabase(store, plan, Token);
        var writer = (IRunDatabase)storage;
        writer.Save(storage.Outputs.Output(dataType, ListModule.Empty<storedDataReference>()), FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => payload));
        var source = WorkspaceData.loadScopedSource(scope, temporary.FullName,
            new setupDataTypeConfig(dataType, parent.Definition.id, ListModule.Empty<string>()), parameters, Token);
        Assert.Equal(storage.Outputs.Stored.Head.Reference, source.Reference);
        var currentKey = DataKey.create(parent.Definition.id, config.DatabaseName, false, config.FunctionName,
            ResultsFunctionCatalog.require(config.FunctionName).Version, dataType, config.DataTypes[dataType], parameters, ListModule.Empty<storedDataReference>());
        Assert.NotEqual(currentKey.Id, source.Reference.Id);
        Assert.IsType<sortopiaStorageData.SortableTests>(source.Value);
    }

    [Fact]
    public void Override_contracts_and_parameter_registry_exclude_removed_configuration()
    {
        foreach (var type in new[] { typeof(projectLocalSettings), typeof(projectSetupSettings), typeof(moduleOverrides), typeof(moduleSetupOverrides) })
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
