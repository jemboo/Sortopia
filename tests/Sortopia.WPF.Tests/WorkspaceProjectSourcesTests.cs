using System.IO;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceProjectSourcesTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-sources-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Integer) => new() { keyName = key, value = value, valueType = (int)type };
    private static projectSettingsDto Settings() => WorkspaceHierarchyTests.ProjectSettings(true);
    private static ProjectDocument Source(bool seal = true)
    {
        var settings = Settings();
        settings.resultsDatabaseName = "Upstream results";
        settings.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiSimpleEvaluationInputs;
        settings.resultsParameterDefaults = [.. RunExecutionTests.UniformModelDefaults(8), RunExecutionTests.Binding("SortableDataFormat", "Bools")];

        return RunExecutionTests.MakeProject("Source", true, settings, seal: seal);
    }
    private static ProjectDocument Consumer(ProjectDocument source, bool seal = true)
    {
        var settings = FunctionOutputContractTests.Combined(true);
        settings.setupDatabaseName = "Produced setup";
        settings.setupFunctionName = Sortopia.Functions.SetupFunctionNames.evaluationSource;
        settings.setupParameterDefaults = [.. RunExecutionTests.UniformModelDefaults(8), RunExecutionTests.Binding("SortableDataFormat", "Bools")];
        settings.setupDataTypes = [new() { dataType = new() { kind = "SortableTests", value = "" }, sourceProjectId = source.Definition.id, queryParameterKeys = ["ChannelCount"] },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = source.Definition.id, queryParameterKeys = [] }];
        return RunExecutionTests.MakeProject("Consumer", true, settings, seal: seal);
    }
    private static WorkspaceDocument Workspace(params ProjectDocument[] projects) => WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace",
        [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Library", [], projects)]);

    [Fact]
    public void Unified_entries_define_identical_map_keys_and_independent_source_and_setup_queries()
    {
        var source = Source(); var world = Workspace(source, Consumer(source));
        var parent = world.ProjectIndex().Values.Single(p => p.Definition.projectName == "Consumer");
        var scope = WorkspaceRunDocument.resolveByRunId(world, parent.Runs[0].id);
        var branch = scope.Run;
        Assert.Equal(branch.SetupSortopiaDataTypes.Value.Order(), branch.SourceProjectIds.Keys.Order());
        Assert.Equal(branch.SetupSortopiaDataTypes.Value.Order(), branch.SetupQueryParameterKeys.Value.Keys.Order());
        var parameters = Run.makeSetupParameterSets(branch).Single(); var type = sortopiaDataType.NewSortableTests("");
        var upstream = QueryParams.forSource(branch, type, parameters); var produced = QueryParams.forSetup(branch, type, parameters);
        Assert.Equal(source.Definition.id, upstream.ProjectId); Assert.Equal("Upstream results", upstream.DatabaseName);
        Assert.Equal("8", upstream.Properties["ChannelCount"]); Assert.DoesNotContain("PrefixLibId", upstream.Properties.Keys);
        Assert.Equal(parent.Definition.id, produced.ProjectId); Assert.Equal("Produced setup", produced.DatabaseName);
        Assert.Equal(new[] { "ChannelCount" }, produced.Properties.Keys); Assert.Contains("SortableDataFormat", upstream.Properties.Keys); Assert.NotEqual(upstream.Id, produced.Id);
        var decoded = RunCodec.decodeRun(scope.Parent, RunCodec.encodeRun(branch));
        var standalone = RunExecutionScopeModule.create(decoded, scope.ProjectLocations).Run;
        Assert.Equal(branch.ProjectConfiguration, standalone.ProjectConfiguration); Assert.Equal(branch.ResolvedSources, standalone.ResolvedSources);
    }

    [Theory]
    [InlineData("wrong-case")]
    [InlineData("wrong-text")]
    [InlineData("missing")]
    [InlineData("unsealed")]
    [InlineData("missing-parameter")]
    public void Invalid_source_dependencies_are_rejected(string issue)
    {
        var source = Source(issue != "unsealed");
        var consumer = Consumer(source);
        var dto = WorkspaceDocumentModule.cloneProject(consumer);
        if (issue == "wrong-case") dto.Definition.configuration.setupDataTypes.Single(entry => entry.dataType.kind == "SortableTests").dataType.kind = "SorterSet";
        if (issue == "wrong-text") dto.Definition.configuration.setupDataTypes.Single(entry => entry.dataType.kind == "SortableTests").dataType.value = "shared ";
        if (issue == "missing") dto.Definition.configuration.setupDataTypes.Single(entry => entry.dataType.kind == "SortableTests").sourceProjectId = Guid.NewGuid();
        if (issue == "missing-parameter") dto.Runs[0].setupParameterSpans = dto.Runs[0].setupParameterSpans.Where(p => p.keyName != "ChannelCount").ToArray();
        Assert.ThrowsAny<Exception>(() => WorkspaceRunDocument.resolveByRunId(Workspace(source, dto), dto.Runs[0].id));
    }

    [Fact]
    public void Copy_projections_are_checked_against_each_source_type()
    {
        var source = Source();
        var consumer = Consumer(source).ResolvedRuns()[0];
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var keys = Microsoft.FSharp.Collections.ListModule.Singleton("SortableDataFormat");
        var entry = new setupDataTypeConfig(modelsType, source.Definition.id, keys);
        var error = Assert.Throws<ArgumentException>(() => Sortopia.Functions.FunctionConfiguration.validateSetupSource(
            consumer.ProjectConfiguration.Setup.Value, entry, source.ResultsSnapshot()));
        Assert.Contains("subset", error.Message);
        Assert.Contains("SortableDataFormat", error.Message);
        Assert.Contains("Source", error.Message);
    }

    [Fact]
    public void Source_query_keys_require_values_even_when_omitted_from_the_local_projection()
    {
        var source = Source();
        var dto = Consumer(source);
        Assert.DoesNotContain(dto.Definition.configuration.setupDataTypes.SelectMany(output => output.queryParameterKeys), key => key == "Repl");
        dto.Definition.configuration.setupParameterDefaults = dto.Definition.configuration.setupParameterDefaults.Where(binding => binding.keyName != "Repl").ToArray();
        dto.Runs[0].setupParameterSpans = dto.Runs[0].setupParameterSpans.Where(span => span.keyName != "Repl").ToArray();
        var error = Assert.Throws<ArgumentException>(() => WorkspaceRunDocument.resolveByRunId(Workspace(source, dto), dto.Runs[0].id));
        Assert.Contains("Repl", error.Message);
    }

    [Fact]
    public void Duplicate_source_types_and_self_dependencies_are_rejected()
    {
        var source = Source(false); var consumer = Consumer(source, false);
        var dto = WorkspaceDocumentModule.cloneProject(consumer);
        dto.Definition.localSettings.setupSources = [dto.Definition.localSettings.setupSources[0], dto.Definition.localSettings.setupSources[0]];
        Assert.Throws<ArgumentException>(() => dto.Configuration());
        var self = Consumer(source); dto = self;
        dto.Definition.configuration.setupDataTypes.Single(row => row.dataType.kind == "SortableTests").sourceProjectId = self.Definition.id;
        Assert.Throws<ArgumentException>(() => WorkspaceRunDocument.resolveByRunId(Workspace(source, dto), dto.Runs[0].id));
        Assert.Throws<ArgumentException>(() => Workspace(source, source));
    }

    [Fact]
    public void Stored_run_requires_the_exact_parent_id_and_family()
    {
        var source = Source();
        var consumer = Consumer(source);
        var inputs = RunDto.fromDomain(consumer.ResolvedRuns()[0]);
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(source.Snapshot(), inputs));
        inputs.parentProjectId = consumer.Definition.id; inputs.runKind = "root";
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(consumer.Snapshot(), inputs));
        var world = Workspace(source);
        Assert.Throws<InvalidOperationException>(() => WorkspaceRunDocument.resolveByRunId(world, RunCodec.readRunId(RunCodec.encodeRun(consumer.ResolvedRuns()[0]))));
    }

    [Fact]
    public async Task Sealed_projects_remain_fixed_after_last_run_removal_and_offer_an_editable_new_version()
    {
        var source = Source(); var original = Workspace(source);
        WorkspaceFileStore.CreateDocument(Root, original, Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        var node = page.Roots[0].Children[0].Children[0];
        Assert.True(node.IsSealed);
        node.Editor!.ResultsFunctionName = "Changed"; node.Name = "Changed";
        Assert.Equal("sorter-eval-inputs.standard.mssi.simple", node.Editor.ResultsFunctionName); Assert.Equal("Changed", node.Name);
        Assert.Equal("Changed", node.Name);
        node.Children.Clear();
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.True(new WorkspaceDocumentIndex(saved.CreateEditDocument()).FindProject(source.Definition.id).Document.Definition.isSealed); Assert.Empty(new WorkspaceDocumentIndex(saved.CreateEditDocument()).FindProject(source.Definition.id).Document.Runs);
        page.Selected = page.Roots[0].Children[0].Children[0];
        Assert.True(page.CreateProjectVersionCommand.CanExecute(null));
        page.CreateProjectVersionCommand.Execute(null);
        var version = page.Selected!;
        Assert.False(version.IsSealed); Assert.NotEqual(source.Definition.id, version.Id); Assert.Empty(version.Children);
        version.Parent!.Editor!.ResultsFunctionName = "sorter-eval-inputs.standard.mssi.simple";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var final = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.Equal("sorter-eval-inputs.standard.mssi.simple", WorkspaceDefinitionResolution.projectSnapshot(new WorkspaceDocumentIndex(final.CreateEditDocument()).FindProject(source.Definition.id).Document.Definition).Configuration.ResultsFunctionName);
        Assert.Equal("sorter-eval-inputs.standard.mssi.simple", WorkspaceDefinitionResolution.projectSnapshot(new WorkspaceDocumentIndex(final.CreateEditDocument()).FindProject(version.Id).Document.Definition).Configuration.ResultsFunctionName);
    }

    [Fact]
    public void Saving_changed_sealed_settings_is_rejected_without_writing()
    {
        var source = Source(); var original = Workspace(source);
        var saved = WorkspaceFileStore.CreateDocument(Root, original, Token).Workspace;
        var before = File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace"));
        var dto = WorkspaceDocumentModule.clone(original);
        dto.Modules[0].Projects[0].Definition.configuration.resultsDatabaseName = "Changed";
        var changed = dto;
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(changed, saved, Token)).Message);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace")));
    }

    [Fact]
    public void Copying_a_module_remaps_internal_source_projects_and_run_parents_together()
    {
        var source = Source(); var original = Workspace(source, Consumer(source));
        var sourceRoot = Path.Combine(temporary.FullName, "Original");
        WorkspaceFileStore.CreateDocument(sourceRoot, original, Token);
        var module = original.Modules[0];
        var sourceFolder = Path.Combine(sourceRoot, "Library");
        var copy = WorkspaceFolderCopy.Prepare(Root, sourceFolder, Path.Combine(Root, "Library"), Token)!;
        var saved = WorkspaceFileStore.CreateDocument(Root, original, Token, [copy]).Workspace;
        Assert.NotEqual(module.Definition.id, saved.CreateEditDocument().Modules[0].Definition.id);
        var copiedSource = new WorkspaceDocumentIndex(saved.CreateEditDocument()).TryFindRun(new WorkspaceDocumentIndex(saved.CreateEditDocument()).Projects.Single(p => p.Document.Definition.projectName == "Source").Document.Runs[0].id).Value.Parent.Document;
        var copiedConsumer = new WorkspaceDocumentIndex(saved.CreateEditDocument()).TryFindRun(new WorkspaceDocumentIndex(saved.CreateEditDocument()).Projects.Single(p => p.Document.Definition.projectName == "Consumer").Document.Runs[0].id).Value.Parent.Document;
        Assert.NotEqual(source.Definition.id, copiedSource.Definition.id);
        var branch = copiedConsumer.ResolvedRuns()[0];
        Assert.All(branch.SourceProjectIds.Values.Where(id => id != Guid.Empty), id => Assert.Equal(copiedSource.Definition.id, id));
        Assert.Equal(copiedConsumer.Definition.id, branch.ParentProjectId);
        Assert.True(copiedSource.Definition.isSealed && copiedConsumer.Definition.isSealed);
        Assert.Equal(2, new WorkspaceDocumentIndex(WorkspaceDocumentModule.clone(saved.CreateEditDocument())).Projects.Length);
        Assert.Equal(source.Definition.id, WorkspaceFileStore.LoadWorkspace(sourceRoot).CreateEditDocument().Modules[0].Projects[0].Definition.id);
    }

    [Fact]
    public void Preparing_an_import_remaps_snapshots_captured_sources_and_retained_editing_choices()
    {
        var source = Source(); var consumer = Consumer(source);
        var document = Workspace(source, consumer);
        var module = document.Modules[0]; var external = Guid.NewGuid();
        setupSourceConfigDto[] choices = [new() { dataType = new() { kind = "SortableTests", value = "" }, sourceProjectId = source.Definition.id },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = external }];
        module.Definition.inheritedDefaults.setupSources = choices;
        module.Definition.localOverrides.setupSources = choices;
        var editing = ProjectLocalSettingsDto.fromDomain(consumer.LocalSettings());
        editing.setupSources = choices;
        editing.resultsParameterDefaults = [Binding("ChannelCount", "unfinished")];
        editing.excludedResultsDefaults = ["ChannelCount"];
        var definition = module.Projects.Single(value => value.Definition.id == consumer.Definition.id).Definition;
        definition.state = projectStateDto.NewSealed(definition.configuration, Microsoft.FSharp.Core.FSharpOption<projectLocalSettingsDto>.Some(editing));
        var library = Path.Combine(temporary.FullName, "Document library");
        var saved = WorkspaceFileStore.CreateDocument(library, document, Token).Workspace;
        var import = WorkspaceImport.PrepareModule(Root, Path.Combine(library, "Library"), Path.Combine(Root, "Library"), Token);
        var imported = import.Definition;
        var copiedSource = imported.Projects.Single(value => value.Definition.projectName == "Source");
        var copied = imported.Projects.Single(value => value.Definition.projectName == "Consumer");
        Assert.All(copied.Definition.configuration.setupDataTypes, value => Assert.Equal(copiedSource.Definition.id, value.sourceProjectId));
        foreach (var sources in new[] { imported.Definition.inheritedDefaults.setupSources, imported.Definition.localOverrides.setupSources, copied.Definition.localSettings.setupSources })
        {
            Assert.Equal(copiedSource.Definition.id, sources.Single(value => value.dataType.kind == "SortableTests").sourceProjectId);
            Assert.Equal(external, sources.Single(value => value.dataType.kind == "SorterModelSet").sourceProjectId);
        }
        Assert.Equal("unfinished", copied.Definition.localSettings.resultsParameterDefaults.Single().value);
        Assert.Equal(new[] { "ChannelCount" }, copied.Definition.localSettings.excludedResultsDefaults);
        var destination = WorkspaceDocumentModule.withModules(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Copy", "Workspace", []), [imported]);
        WorkspaceFileStore.CreateDocument(Root, destination, Token, [import.Copy]);
        Assert.Equal(copied.Definition.id, new WorkspaceDocumentIndex(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument()).FindProject(copied.Definition.id).Document.Runs[0].parentProjectId);
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), WorkspaceFileStore.LoadWorkspace(library).CreateEditDocument()));
    }

    [Fact]
    public void Setup_editor_explains_incompatible_type_and_text_and_links_to_the_defining_source()
    {
        var source = Source(); var world = Workspace(source, Consumer(source));
        var tree = WorkspaceTreeNode.FromDocument(world);
        var consumer = tree.Children[0].Children.Single(p => p.Name == "Consumer");
        var sourceNode = tree.Children[0].Children.Single(p => p.Name == "Source");
        var row = consumer.Editor!.InheritedSetupDataTypes.Single(row => row.DataType.Kind == "SortableTests");
        Assert.Equal(source.Definition.id, Assert.Single(row.SourceProjects).Id);
        Assert.Same(sourceNode.Editor, row.SourceEditor);
        row = new WorkspaceSetupDataTypeEntry(new() { dataType = new() { kind = "SortableTests", value = "wrong label" }, sourceProjectId = source.Definition.id, queryParameterKeys = [] }, () => [new(source.Definition.id, "Source", true, sourceNode.Editor!.ToProjectSettings(), sourceNode.Editor)]);
        Assert.False(Assert.Single(row.SourceProjects).IsCompatible); Assert.Contains("exact data type", row.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Importing_into_the_same_workspace_remaps_internal_references_and_preserves_external_ones(bool wholeModule)
    {
        var source = Source(); var original = Workspace(source, Consumer(source));
        WorkspaceFileStore.CreateDocument(Root, original, Token);
        var folder = Path.Combine(Root, "Library");
        if (!wholeModule) folder = Path.Combine(folder, "Consumer");
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings, pickExistingFolder: _ => folder);
        page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Name = "Destination";
        await page.AddExistingAsync(wholeModule ? WorkspaceEntityKind.Module : WorkspaceEntityKind.Project);
        Assert.Empty(page.Error);
        var imported = page.Selected!;
        var importedConsumer = wholeModule ? imported.Children.Single(p => p.Name == "Consumer") : imported;
        var expectedSourceId = wholeModule ? imported.Children.Single(p => p.Name == "Source").Id : source.Definition.id;
        Assert.All(importedConsumer.Editor!.InheritedSetupDataTypes.Where(row => row.RequiresSource), entry => Assert.Equal(expectedSourceId, entry.SourceProjectId));
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        var copiedRun = saved.ResolveRun(new WorkspaceDocumentIndex(saved.CreateEditDocument()).FindProject(importedConsumer.Id).Document.Runs[0].id).Run;
        Assert.All(copiedRun.SourceProjectIds.Values.Where(id => id != Guid.Empty), id => Assert.Equal(expectedSourceId, id));
        Assert.Equal(importedConsumer.Id, copiedRun.ParentProjectId);
        Assert.True(new WorkspaceDocumentIndex(saved.CreateEditDocument()).FindProject(source.Definition.id).Document.Definition.isSealed);
    }

    [Fact]
    public async Task Failed_and_successful_first_run_saves_both_leave_the_project_editable()
    {
        WorkspaceFileStore.CreateDocument(Root, Workspace(), Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        page.Selected = page.Roots[0].Children[0];
        page.AddProjectCommand.Execute(null);
        var parent = page.Selected!;
        parent.Editor!.LoadFixtureSettings(Settings());
        page.AddRunCommand.Execute(null);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Path.Combine(Root, page.Roots[0].Children[0].Name), "module"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.False(parent.IsSealed);
        parent.Editor!.Description = "Retry description";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceTestFiles.LoadRun(Root, document => new WorkspaceDocumentIndex(document).FindProject(parent.Id).Document.Runs[0]);
        Assert.False(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects.Single(p => p.Definition.id == parent.Id).Definition.isSealed);
        Assert.Equal("Retry description", saved.Description);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
