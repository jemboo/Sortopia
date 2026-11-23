using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceHierarchyTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-hierarchy-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static projectSettingsDto ProjectSettings(bool branch = false) => new()
    {
        description = "Keep every setting: α", resultsDatabaseName = "Results", resultsFunctionName = "sortable-tests.standard", resultsFunctionVersion = "1",
        resultsDataTypes = [new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = [] }],
        resultsParameterDefaults = [RunExecutionTests.Binding("ChannelCount", "8", parameterValueType.Integer), RunExecutionTests.Binding("SortableDataFormat", "Bools")],
        setupParameterDefaults = branch ? [RunExecutionTests.Binding("ChannelCount", "8", parameterValueType.Integer), RunExecutionTests.Binding("SortableDataFormat", "Bools")] : [],
        setupDatabaseName = branch ? "Setup" : "", setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : "", setupFunctionVersion = branch ? "1" : "",
        setupDataTypes = branch ? [new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = [] }] : []
    };
    internal static projectSettingsDto EditorSettings(bool branch) => ProjectSettings(branch);
    internal static project MakeProject(hierarchyKind kind, Guid id, string name, run[] runs)
    {
        var settings = ProjectSettings(kind == hierarchyKind.Branch);
        var resultKeys = runs.SelectMany(run => run.ResultsParameterSpans).Select(span => span.KeyName).ToHashSet();
        var setupKeys = runs.SelectMany(run => run.SetupParameterSpans?.Value ?? Microsoft.FSharp.Collections.ListModule.Empty<parameterSpan>()).Select(span => span.KeyName).ToHashSet();
        settings.resultsParameterDefaults = settings.resultsParameterDefaults.Where(binding => !resultKeys.Contains(binding.keyName)).ToArray();
        settings.setupParameterDefaults = settings.setupParameterDefaults.Where(binding => !setupKeys.Contains(binding.keyName)).ToArray();
        foreach (var output in settings.resultsDataTypes) output.queryParameterKeys = resultKeys.ToArray();
        foreach (var output in settings.setupDataTypes) output.queryParameterKeys = setupKeys.ToArray();
        var parent = project.create(id, name, ProjectDto.settingsToDomain(kind, settings), []);
        return project.withRuns(parent, runs.Select(value => { var dto = RunDto.fromDomain(value); dto.parentProjectId = id; return RunDto.toDomain(parent, dto); }).ToArray());
    }
    internal static run FromRunDto(runDto value, string projectName = "Project")
    {
        var settings = ProjectSettings(value.runKind == "branch");
        foreach (var output in settings.resultsDataTypes) output.queryParameterKeys = value.resultsParameterSpans.Select(span => span.keyName).ToArray();
        foreach (var output in settings.setupDataTypes) output.queryParameterKeys = value.setupParameterSpans.Select(span => span.keyName).ToArray();
        settings.resultsParameterDefaults = settings.resultsParameterDefaults.Where(binding => !value.resultsParameterSpans.Any(span => span.keyName == binding.keyName)).ToArray();
        settings.setupParameterDefaults = settings.setupParameterDefaults.Where(binding => !value.setupParameterSpans.Any(span => span.keyName == binding.keyName)).ToArray();
        return RunDto.toDomain(project.create(value.parentProjectId, projectName, SettingsDto.toDomain(value.runKind == "branch" ? hierarchyKind.Branch : hierarchyKind.Root, settings), []), value);
    }
    internal static setupDataTypeConfigDto SetupType(string kind, string text = "", params string[] keys) => new()
    { dataType = new() { kind = kind, value = text }, sourceProjectId = WorkspaceTestFiles.Id("source"), queryParameterKeys = keys };
    internal static run DecodeRun(byte[] bytes) => FromRunDto(MessagePackSetup.deserialize<runDto>(bytes));
    internal static run ReadRun(string file)
    {
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(file))!;
        var parent = WorkspaceFileStore.LoadProjectFolder(folder, Token);
        return WorkspaceCodec.decodeRun(parent, File.ReadAllBytes(file));
    }
    internal static void ConfigureProject(WorkspaceTreeNode node)
    {
        var module = node.Kind == WorkspaceEntityKind.Module ? node : node.Parent!;
        module.Editor!.ResultsFunctionName = "sortable-tests.standard";
        if (node.IsBranch) module.Editor.SetupFunctionName = "sortable-tests-with-prefix.standard";
        var settings = ProjectSettings(node.IsBranch);
        settings.resultsParameterDefaults = []; settings.setupParameterDefaults = [];
        foreach (var output in settings.resultsDataTypes) output.queryParameterKeys = ["ChannelCount", "SortableDataFormat"];
        foreach (var output in settings.setupDataTypes) output.queryParameterKeys = ["ChannelCount", "SortableDataFormat"];
        node.Editor!.LoadProject(settings);
    }
    // Hierarchy fixtures use registered functions and explicit query spans.
    internal static WorkspaceModule MakeModule(hierarchyKind kind, Guid id, string name, WorkspaceModule[] modules, project[] projects)
    {
        var local = new moduleOverridesDto { resultsFunctionName = "sortable-tests.standard", setupFunctionName = kind == hierarchyKind.Branch ? "sortable-tests-with-prefix.standard" : null };
        return WorkspaceModule.createInherited(id, name, ModuleDefaults.configuration(kind == hierarchyKind.Branch), ModuleOverridesDto.toDomain(kind, local), modules, projects);
    }
    internal static WorkspaceTreeNode AddSetupSource(WorkspaceTreeNode owner, string text = "")
    {
        var container = owner.Kind == WorkspaceEntityKind.Module ? owner : owner.Parent!;
        container.Editor!.SetupFunctionName = Sortopia.Functions.SetupFunctionNames.evaluationSource;
        container.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        var sourceModule = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Source module", family: container.Family);
        var root = container;
        while (root.Parent is not null) root = root.Parent;
        root.Children.Add(sourceModule); // Its functions belong on a separate module path from the consumer.
        var sourceSettings = ProjectSettings(container.IsBranch);
        sourceSettings.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiSimpleEvaluationInputs;
        sourceSettings.resultsParameterDefaults = [.. RunExecutionTests.UniformModelDefaults(8), RunExecutionTests.Binding("SortableDataFormat", "Bools")];
        sourceSettings.resultsDataTypes = [];
        sourceModule.Editor!.LoadProject(sourceSettings);
        var source = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Setup source", family: container.Family);
        sourceModule.Children.Add(source);
        foreach (var sourceRow in owner.Editor!.SetupDataTypes) sourceRow.SourceOverride!.IsOverridden = true;
        foreach (var entry in owner.Editor.SetupDataTypes) entry.SourceProjectId = source.Id;
        return source;
    }
    internal static run MakeRun(string projectName, string name, bool branch = false) => FromRunDto(new runDto
    {
        schemaVersion = WorkspaceDtoSchema.version, id = WorkspaceTestFiles.Id(projectName + "/" + name), runKind = branch ? "branch" : "root", parentProjectId = WorkspaceTestFiles.Id(projectName + "/project"), runName = name,
        descriptionOverride = null,
        resultsParameterSpans = [new parameterSpanDto { keyName = "ChannelCount", values = ["7", "11"] }, new() { keyName = "SortableDataFormat", values = ["Bools"] }],

        setupParameterSpans = branch ? [new parameterSpanDto { keyName = "ChannelCount", values = ["8", "12"] }, new() { keyName = "SortableDataFormat", values = ["Bools"] }] : []
    }, projectName);
    private workspace Sample() => workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Sorting",
        [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Nested module", [], [])],
        [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Branch, Guid.NewGuid(), "Search", [MakeRun("Search", "First", true), MakeRun("Search", "With setup", true)])])]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ids_survive_renames_and_round_trips(bool branch)
    {
        var original = MakeRun("Search", "Trial", branch);
        var renamed = WorkspaceRun.withNames("Search", "Trial: renamed", original);
        var bytes = WorkspaceCodec.encodeRun(renamed);
        Assert.Equal(WorkspaceDtoSchema.version, MessagePackSetup.deserialize<runDto>(bytes).schemaVersion);
        Assert.Equal("Trial: renamed", DecodeRun(bytes).RunName);
        Assert.Equal(original.Id, DecodeRun(bytes).Id);
        Assert.Null(typeof(run).GetProperty("FolderName"));
        var malformed = RunDto.fromDomain(renamed);
        malformed.id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => DecodeRun(MessagePackSetup.serialize(malformed)));
    }

    [Fact]
    public void MessagePack_round_trip_preserves_recursive_modules_and_complete_branch_runs()
    {
        var original = Sample();
        var restored = WorkspaceCodec.decodeWorkspace(WorkspaceCodec.encodeWorkspace(original));
        Assert.Equal(WorkspaceCodec.encodeWorkspace(original), WorkspaceCodec.encodeWorkspace(restored));
        var runs = restored.Modules[0].Projects[0].Runs;
        Assert.True(runs[0].HasSetup);
        Assert.True(runs[1].HasSetup);
        Assert.Equal(new[] { "8", "12" }, runs[1].SetupParameterSpans.Value.Single(row => row.KeyName == "ChannelCount").Values);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, Assert.Single(runs[1].SetupQueryParameterKeys.Value).Value);
        var exposed = restored.Modules;
        exposed[0] = WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Replaced", [], []);
        Assert.Equal("Sorting", restored.Modules[0].ModuleName);
        var invalid = WorkspaceDto.fromDomain(original);
        invalid.schemaVersion = 109;
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeWorkspace(MessagePackSetup.serialize(invalid)));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("CON.txt")]
    [InlineData("name.")]
    [InlineData(" ")]
    public void Folder_names_cannot_escape_or_alias_folder_paths(string name)
    {
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), name, [], []));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), name, []));
    }

    [Theory]
    [InlineData("workspace.modules")]
    [InlineData("module.modules")]
    [InlineData("module.projects")]
    [InlineData("project.runs")]
    public void Every_sibling_collection_requires_distinct_names(string collection)
    {
        var secondName = "FIRST";
        var modules = new[] { WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "First", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), secondName, [], []) };
        var projects = new[] { WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "First", []), WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), secondName, []) };
        var runs = new[] { MakeRun("Parent", "First", false), MakeRun("Parent", secondName, false) };
        var error = Assert.Throws<ArgumentException>(() =>
        {
            switch (collection)
            {
                case "workspace.modules": workspace.create(Guid.NewGuid(), "Parent", "parent", modules); break;
                case "module.modules": WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "parent", modules, []); break;
                case "module.projects": WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "parent", [], projects); break;
                case "project.runs": WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "parent", runs); break;
            }
        });
        Assert.Contains("Duplicate name", error.Message);
    }

    [Fact]
    public void Module_and_project_names_share_the_same_folder_namespace()
    {
        var childProject = WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "shared", []);
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "parent",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "SHARED", [], [])], [childProject]));
    }

    [Fact]
    public void Duplicate_siblings_and_mismatched_run_project_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Sorting", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "sorting", [], [])]));
        Assert.Throws<ArgumentException>(() => project.create(Guid.NewGuid(), "Search", SettingsDto.toDomain(hierarchyKind.Root, ProjectSettings()), [MakeRun("Different", "First")]));
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "same", [], []), WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "SAME", [], [])]));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "m", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "same", []), WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "SAME", [])]));
    }

    [Fact]
    public void Entity_names_determine_paths_while_workspace_and_run_names_remain_display_names()
    {
        var value = workspace.create(Guid.NewGuid(), "Research: 2026", "Workspace", [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "sorters",
            [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "nested-m", [], [])],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "prefix-search", [MakeRun("prefix-search", "Trial: first", false), MakeRun("prefix-search", "Trial: root", false)])])]);
        WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(loaded));
        Assert.Equal("sorters", loaded.Modules.Single().ModuleName);
        Assert.Equal("nested-m", loaded.Modules.Single().Modules.Single().ModuleName);
        var projectFolder = Path.Combine(Root, "sorters", "prefix-search");
        Assert.True(File.Exists(WorkspaceTestFiles.DefinitionPath(projectFolder, "project")));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[1].Id))));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[0].Id))));
        var imported = WorkspaceFileStore.LoadModuleFolder(Path.Combine(Root, "sorters"), Token);
        Assert.Equal(WorkspaceCodec.encodeModule(value.Modules.Single()), WorkspaceCodec.encodeModule(imported));
    }





    [Fact]
    public void Importing_folders_with_duplicate_saved_run_names_fails_without_writes()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var runs = Path.Combine(Root, "Sorting", "Search", "runs");
        var duplicate = RunDto.fromDomain(MakeRun("Search", "First", true));
        duplicate.id = Guid.NewGuid();
        duplicate.parentProjectId = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Id;
        File.WriteAllBytes(Path.Combine(runs, WorkspaceDefinitionFiles.Name(duplicate.id)), WorkspaceCodec.encodeRun(WorkspaceHierarchyTests.FromRunDto(duplicate)));
        var projectFile = WorkspaceDefinitionFiles.Require(Path.GetDirectoryName(runs)!, "project");
        var definition = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(projectFile));
        definition.runs = [.. definition.runs, duplicate.id];
        File.WriteAllBytes(projectFile, MessagePackSetup.serialize(definition));
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var error = Assert.Throws<ArgumentException>(() => WorkspaceFileStore.LoadProjectFolder(Path.GetDirectoryName(runs)!, Token));
        Assert.Contains("Duplicate name in runs", error.Message);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
    }

    [Fact]
    public void Saving_writes_every_entity_in_its_typed_folder_and_preserves_output_files()
    {
        var value = Sample();
        WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        Assert.Equal(6, Directory.GetFiles(Root, "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(WorkspaceFileStore.LoadDefinition(Root)));
        Assert.Equal("Sorting", WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "Sorting", "module"))).moduleName);
        Assert.Equal("Search", WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "Sorting", "Search", "project"))).projectName);
        var runFile = Path.Combine(Root, "Sorting", "Search", "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs.Single(run => run.RunName == "With setup").Id));
        Assert.Equal(WorkspaceCodec.encodeRun(value.Modules[0].Projects[0].Runs.Single(r => r.RunName == "With setup")), File.ReadAllBytes(runFile));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runFile)!, "results.txt"), "Keep these results");
        WorkspaceFileStore.SaveWorkspace(Root, value, value, Token);
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Path.GetDirectoryName(runFile)!, "results.txt")));
    }

    [Fact]
    public void Import_reads_referenced_hierarchy_and_preserves_run_identity_without_writing()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var moduleFolder = Path.Combine(Root, "Sorting");
        Directory.Move(Path.Combine(moduleFolder, "Search"), Path.Combine(moduleFolder, "Renamed"));
        var projectFolder = Path.Combine(moduleFolder, "Renamed");
        WorkspaceImportTests.WriteModule(Path.Combine(moduleFolder, "Added on disk"), hierarchyKind.Branch);
        var moduleFile = WorkspaceDefinitionFiles.Require(moduleFolder, "module");
        var definition = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(moduleFile));
        definition.projects[0].name = "Renamed";
        File.WriteAllBytes(moduleFile, MessagePackSetup.serialize(definition));
        var projectFile = WorkspaceDefinitionFiles.Require(projectFolder, "project");
        var projectDefinition = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(projectFile));
        projectDefinition.projectName = "Renamed";
        File.WriteAllBytes(projectFile, MessagePackSetup.serialize(projectDefinition));
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var imported = WorkspaceFileStore.LoadModuleFolder(moduleFolder, Token);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        var renamed = imported.Projects.Single();
        Assert.Equal("Renamed", renamed.ProjectName);
        var run = renamed.Runs.Single(r => r.RunName == "With setup");
        Assert.Equal("With setup", run.RunName);
        Assert.Equal(MakeRun("Search", "With setup", true).Id, run.Id);
        Assert.Equal("Renamed", run.ProjectName);
        Assert.Equal("sortable-tests-with-prefix.standard", run.SetupFunctionName.Value);
        Assert.DoesNotContain(imported.Modules, m => m.ModuleName == "Added on disk");
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
    }

    [Fact]
    public void Removed_modules_are_deleted_instead_of_becoming_unlisted_folders()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var empty = workspace.create(Guid.NewGuid(), "Research", "Workspace", []);
        var result = WorkspaceFileStore.SaveWorkspace(Root, empty, WorkspaceFileStore.LoadDefinition(Root), Token);
        Assert.Empty(result.RetainedFolders);
        Assert.Empty(WorkspaceFileStore.LoadDefinition(Root).Modules);
        Assert.False(Directory.Exists(Path.Combine(Root, "Sorting")));
    }

    [Fact]
    public void Cancelled_save_leaves_definitions_and_folders_unchanged()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var updated = workspace.create(Guid.NewGuid(), "Updated", "Workspace", []);
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.SaveWorkspace(Root, updated, Sample(), new CancellationToken(true)));
        Assert.Equal("Research", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
        Assert.True(Directory.Exists(Path.Combine(Root, "Sorting")));
    }

    [Fact]
    public void Failed_commit_rolls_back_earlier_writes_and_new_folders()
    {
        var original = Sample();
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var updated = workspace.create(original.Id, "Updated", "Workspace", original.Modules.Append(WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "New module", [], [])).ToArray());
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => WorkspaceFileStore.SaveWorkspace(Root, updated, original, Token));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(WorkspaceCodec.encodeWorkspace(original), WorkspaceCodec.encodeWorkspace(WorkspaceFileStore.LoadDefinition(Root)));
        Assert.False(Directory.Exists(Path.Combine(Root, "New module")));
        Assert.Empty(Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Corrupt_run_and_workspace_definitions_are_reported_without_writes()
    {
        var runs = Directory.CreateDirectory(Path.Combine(Root, "M", "P", "runs")).FullName;
        File.WriteAllBytes(Path.Combine(runs, WorkspaceDefinitionFiles.Name(Guid.NewGuid())), [0xc1]);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadProjectFolder(Path.Combine(Root, "M", "P"), Token));
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        var file = WorkspaceTestFiles.DefinitionPath(Root, "workspace");
        File.WriteAllBytes(file, [0xc1]);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadDefinition(Root));
        Assert.Equal(new byte[] { 0xc1 }, File.ReadAllBytes(file));
    }

    [Fact]
    public void Copied_workspace_uses_selected_root_instead_of_saved_absolute_path()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var moved = Path.Combine(temporary.FullName, "Moved workspace");
        Directory.Move(Root, moved);
        var loaded = WorkspaceFileStore.LoadDefinition(moved);
        Assert.Equal("Moved workspace", loaded.FolderName);
        WorkspaceFileStore.SaveWorkspace(moved, loaded, loaded, Token);
        Assert.True(File.Exists(WorkspaceTestFiles.DefinitionPath(moved, "workspace")));
    }

    [Fact]
    public async Task Workspace_editor_saves_the_current_hierarchy_in_one_action()
    {
        Directory.CreateDirectory(Root);
        var viewModel = new WorkspacePageViewModel(() => { }, _ => Root, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "setting.txt")));
        viewModel.BrowseWorkspaceFolderCommand.Execute(null);
        viewModel.AddModuleCommand.Execute(null);
        viewModel.Selected!.Name = "Sorting";
        viewModel.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(viewModel.Selected!);
        viewModel.Selected!.Name = "Search";
        viewModel.AddRunCommand.Execute(null);
        viewModel.Selected!.Name = "First";
        viewModel.Selected.Editor!.LoadRun(RunDto.fromDomain(MakeRun("Search", "First")));
        Assert.True(viewModel.SaveWorkspaceCommand.CanExecute(null));
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        viewModel.Selected.Name = "Edited";
        await viewModel.SaveWorkspaceAsync();
        Assert.Empty(viewModel.Error);
        Assert.True(viewModel.HasSavedWorkspace);
        Assert.False(viewModel.ShowNewWorkspace);
        Assert.Equal("Edited", WorkspaceTestFiles.LoadRuntimeSnapshot(Root).Modules[0].Projects[0].Runs[0].RunName);
        Assert.False(viewModel.DiscardChangesCommand.CanExecute(null));
        Assert.Single(viewModel.Roots[0].Children);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
