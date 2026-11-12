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
    private static project Source(bool seal = true)
    {
        var settings = Settings();
        settings.resultsDatabaseName = "Upstream results";

        return RunExecutionTests.MakeProject("Source", true, settings, seal: seal);
    }
    private static project Consumer(project source, bool seal = true)
    {
        var settings = FunctionOutputContractTests.Combined(true);
        settings.setupDatabaseName = "Produced setup";
        settings.setupFunctionName = Sortopia.Functions.SetupFunctionNames.evaluationPrefix;
        settings.setupParameterDefaults = [.. RunExecutionTests.ModelDefaults(8), RunExecutionTests.Binding("SortableDataFormat", "Bools"),
            RunExecutionTests.Binding("PrefixLibId", Sortopia.Sorting.Catalog.PrefixLibId.toString(Sortopia.Sorting.Catalog.PrefixLib.ids().First(id => id.ChannelCount == 8 && id.StageLength == 1)), parameterValueType.PrefixLibId)];
        settings.setupDataTypes = [new() { dataType = new() { kind = "SortableTests", value = "" }, sourceProjectId = source.Id, queryParameterKeys = [] }];
        return RunExecutionTests.MakeProject("Consumer", true, settings, seal: seal);
    }
    private static workspace Workspace(params project[] projects) => workspace.create(Guid.NewGuid(), "Research", "Workspace",
        [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Library", "Library", [], projects)]);

    [Fact]
    public void Unified_entries_define_identical_map_keys_and_independent_source_and_setup_queries()
    {
        var source = Source(); var world = Workspace(source, Consumer(source));
        var parent = world.ProjectIndex.Values.Single(p => p.ProjectName == "Consumer"); var branch = ((run.BranchRun)parent.Runs[0]).Item;
        Assert.Equal(branch.SetupSortopiaDataTypes.Order(), branch.SourceProjectIds.Keys.Order());
        Assert.Equal(branch.SetupSortopiaDataTypes.Order(), branch.SetupQueryParameterKeys.Keys.Order());
        var parameters = BranchRun.makeSetupParameterSets(branch).Single(); var type = sortopiaDataType.NewSortableTests("");
        var upstream = QueryParams.forSource(branch, type, parameters); var produced = QueryParams.forSetup(branch, type, parameters);
        Assert.Equal(source.Id, upstream.ProjectId); Assert.Equal("Upstream results", upstream.DatabaseName);
        Assert.Equal("8", upstream.Properties["ChannelCount"]); Assert.DoesNotContain("PrefixLibId", upstream.Properties.Keys);
        Assert.Equal(parent.Id, produced.ProjectId); Assert.Equal("Produced setup", produced.DatabaseName);
        Assert.Contains("PrefixLibId", produced.Properties.Keys); Assert.NotEqual(upstream.Id, produced.Id);
        var standalone = ((run.BranchRun)WorkspaceCodec.decodeRunById(world, WorkspaceCodec.encodeRun(parent.Runs[0]))).Item;
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
        var dto = ProjectDto.fromDomain(consumer);
        if (issue == "wrong-case") dto.configuration.setupDataTypes.Single(entry => entry.sourceProjectId != Guid.Empty).dataType.kind = "SorterSet";
        if (issue == "wrong-text") dto.configuration.setupDataTypes.Single(entry => entry.sourceProjectId != Guid.Empty).dataType.value = "shared ";
        if (issue == "missing") dto.configuration.setupDataTypes.Single(entry => entry.sourceProjectId != Guid.Empty).sourceProjectId = Guid.NewGuid();
        if (issue == "missing-parameter") dto.runs[0].setupParameterSpans = dto.runs[0].setupParameterSpans.Where(p => p.keyName != "ChannelCount").ToArray();
        Assert.Throws<ArgumentException>(() => Workspace(source, ProjectDto.toDomain(dto)));
    }

    [Fact]
    public void Draft_dependencies_allow_draft_sources_but_duplicate_types_and_cycles_are_rejected()
    {
        var source = Source(false); var consumer = Consumer(source, false);
        var dto = ProjectDto.fromDomain(consumer);
        dto.localOverrides.setupSources = [dto.localOverrides.setupSources[0], dto.localOverrides.setupSources[0]];
        Assert.Throws<ArgumentException>(() => ProjectDto.toDomain(dto));
        var self = Consumer(source); dto = ProjectDto.fromDomain(self);
        dto.configuration.setupDataTypes.Single(row => row.sourceProjectId != Guid.Empty).sourceProjectId = self.Id;
        Assert.Throws<ArgumentException>(() => Workspace(ProjectDto.toDomain(dto)));
        Assert.Throws<ArgumentException>(() => Workspace(source, source));
    }

    [Fact]
    public void Stored_run_requires_the_exact_parent_id_and_family()
    {
        var source = Source();
        var consumer = Consumer(source);
        var inputs = RunDto.fromDomain(consumer.Runs[0]);
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(source, inputs));
        inputs.parentProjectId = consumer.Id; inputs.runKind = "root";
        Assert.Throws<ArgumentException>(() => RunDto.toDomain(consumer, inputs));
        var world = Workspace(source);
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeRunById(world, WorkspaceCodec.encodeRun(consumer.Runs[0])));
    }

    [Fact]
    public async Task Sealed_projects_remain_fixed_after_last_run_removal_and_offer_an_editable_new_version()
    {
        var source = Source(); var original = Workspace(source);
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        var node = page.Roots[0].Children[0].Children[0];
        Assert.True(node.IsSealed);
        node.Editor!.ResultsFunctionName = "Changed"; node.Name = "Changed";
        Assert.Equal("sortable-tests.standard", node.Editor.ResultsFunctionName); Assert.Equal("Source", node.Name);
        node.Children.Clear();
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root);
        Assert.True(saved.ProjectIndex[source.Id].IsSealed); Assert.Empty(saved.ProjectIndex[source.Id].Runs);
        page.Selected = page.Roots[0].Children[0].Children[0];
        Assert.True(page.CreateProjectVersionCommand.CanExecute(null));
        page.CreateProjectVersionCommand.Execute(null);
        var version = page.Selected!;
        Assert.False(version.IsSealed); Assert.NotEqual(source.Id, version.Id); Assert.Empty(version.Children);
        version.Parent!.Editor!.ResultsFunctionName = "sorter-eval-inputs.standard";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var final = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal("sortable-tests.standard", final.ProjectIndex[source.Id].ResultsFunctionName);
        Assert.Equal("sorter-eval-inputs.standard", final.ProjectIndex[version.Id].ResultsFunctionName);
    }

    [Fact]
    public void Saving_changed_sealed_settings_is_rejected_without_writing()
    {
        var source = Source(); var original = Workspace(source);
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var before = File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace"));
        var dto = WorkspaceDto.fromDomain(original);
        dto.modules[0].projects[0].configuration.resultsDatabaseName = "Changed";
        var changed = WorkspaceDto.toDomain(dto);
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveWorkspace(Root, changed, original, Token)).Message);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace")));
    }

    [Fact]
    public void Copying_a_module_remaps_internal_source_projects_and_run_parents_together()
    {
        var source = Source(); var original = Workspace(source, Consumer(source));
        var sourceRoot = Path.Combine(temporary.FullName, "Original");
        WorkspaceFileStore.CreateWorkspace(sourceRoot, original, Token);
        var module = original.Modules[0];
        var sourceFolder = Path.Combine(sourceRoot, "modules", "Library");
        var copy = WorkspaceFolderCopy.Prepare(Root, sourceFolder, Path.Combine(Root, "modules", "Library"), Token)!;
        var saved = WorkspaceFileStore.CreateWorkspace(Root, original, Token, [copy]).Definition;
        Assert.NotEqual(module.Id, saved.Modules[0].Id);
        var copiedSource = saved.ProjectIndex.Values.Single(p => p.ProjectName == "Source");
        var copiedConsumer = saved.ProjectIndex.Values.Single(p => p.ProjectName == "Consumer");
        Assert.NotEqual(source.Id, copiedSource.Id);
        var branch = ((run.BranchRun)copiedConsumer.Runs[0]).Item;
        Assert.All(branch.SourceProjectIds.Values.Where(id => id != Guid.Empty), id => Assert.Equal(copiedSource.Id, id));
        Assert.Equal(copiedConsumer.Id, branch.ParentProjectId);
        Assert.True(copiedSource.IsSealed && copiedConsumer.IsSealed);
        Assert.Equal(2, WorkspaceCodec.decodeWorkspace(WorkspaceCodec.encodeWorkspace(saved)).ProjectIndex.Count);
        Assert.Equal(source.Id, WorkspaceFileStore.LoadDefinition(sourceRoot).Modules[0].Projects[0].Id);
    }

    [Fact]
    public void Setup_editor_filters_exact_type_and_text_and_links_to_the_defining_source()
    {
        var source = Source(); var world = Workspace(source, Consumer(source));
        var tree = WorkspaceTreeNode.FromWorkspace(world);
        var consumer = tree.Children[0].Children.Single(p => p.Name == "Consumer");
        var sourceNode = tree.Children[0].Children.Single(p => p.Name == "Source");
        var row = consumer.Editor!.InheritedSetupDataTypes.Single(row => row.RequiresSource);
        Assert.Equal(source.Id, Assert.Single(row.SourceProjects).Id);
        Assert.Same(sourceNode.Editor, row.SourceEditor);
        row = new WorkspaceSetupDataTypeEntry(new() { dataType = new() { kind = "SortableTests", value = "wrong label" }, sourceProjectId = source.Id, queryParameterKeys = [] }, () => [new(source.Id, "Source", true, sourceNode.Editor!.ToProjectSettings(), sourceNode.Editor)]);
        Assert.Empty(row.SourceProjects); Assert.Contains("exact data type", row.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Importing_into_the_same_workspace_remaps_internal_references_and_preserves_external_ones(bool wholeModule)
    {
        var source = Source(); var original = Workspace(source, Consumer(source));
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var folder = Path.Combine(Root, "modules", "Library");
        if (!wholeModule) folder = Path.Combine(folder, "projects", "Consumer");
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings, pickExistingFolder: _ => folder);
        page.AddBranchModuleCommand.Execute(null);
        page.Selected!.Name = "Destination";
        await page.AddExistingAsync(wholeModule ? WorkspaceEntityKind.Module : WorkspaceEntityKind.Project);
        Assert.Empty(page.Error);
        var imported = page.Selected!;
        var importedConsumer = wholeModule ? imported.Children.Single(p => p.Name == "Consumer") : imported;
        var expectedSourceId = wholeModule ? imported.Children.Single(p => p.Name == "Source").Id : source.Id;
        Assert.All(importedConsumer.Editor!.InheritedSetupDataTypes.Where(row => row.RequiresSource), entry => Assert.Equal(expectedSourceId, entry.SourceProjectId));
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root);
        var copiedRun = ((run.BranchRun)saved.ProjectIndex[importedConsumer.Id].Runs[0]).Item;
        Assert.All(copiedRun.SourceProjectIds.Values.Where(id => id != Guid.Empty), id => Assert.Equal(expectedSourceId, id));
        Assert.Equal(importedConsumer.Id, copiedRun.ParentProjectId);
        Assert.True(saved.ProjectIndex[source.Id].IsSealed);
    }

    [Fact]
    public async Task Failed_first_run_save_leaves_the_project_draft_until_the_transaction_succeeds()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Workspace(), Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")); settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        page.Selected = page.Roots[0].Children[0];
        page.AddProjectCommand.Execute(null);
        var parent = page.Selected!;
        parent.Editor!.LoadProject(Settings());
        page.AddRunCommand.Execute(null);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Path.Combine(Root, "modules", page.Roots[0].Children[0].FolderName), "module"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await page.SaveWorkspaceAsync();
            Assert.NotEmpty(page.Error);
        }
        Assert.False(parent.IsSealed);
        parent.Editor!.Description = "Retry description";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).ProjectIndex[parent.Id];
        Assert.True(saved.IsSealed);
        Assert.Equal("Retry description", saved.Runs[0].Description);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
