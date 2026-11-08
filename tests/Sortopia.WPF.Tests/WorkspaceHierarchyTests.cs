using System.IO;
using System.Text.Json;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.@module;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceHierarchyTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-hierarchy-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static run MakeRun(string projectName, string name, bool branch = false) => RunDto.toDomain(new runDto
    {
        schemaVersion = 4, id = WorkspaceTestFiles.Id(projectName + "/" + name), runKind = branch ? "branch" : "root", projectName = projectName, runName = name,
        description = "Keep every setting: α", resultsDatabaseName = "Results", executeFunctionName = "Evaluate",
        outputParameterSpans = [new parameterSpanDto { keyName = "Seed", values = ["7", "11"] }],
        outputDataTypes = [4], outputQueryParameterKeys = ["Seed"],
        setupDatabaseName = branch ? "Setup" : "", setupFunctionName = branch ? "Generate" : "",
        setupParameterSpans = branch ? [new parameterSpanDto { keyName = "Width", values = ["8", "12"] }] : [],
        setupOutputDataTypes = branch ? [3] : [],
        setupQueryParameterKeys = branch ? [new queryKeysDto { outputDataType = 3, keys = ["Width"] }] : []
    });
    private workspace Sample() => workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(hierarchyKind.Branch, Guid.NewGuid(), "Sorting", "Sorting",
        [WorkspaceModule.create(hierarchyKind.Branch, Guid.NewGuid(), "Nested module", "Nested module", [], [])],
        [project.create(hierarchyKind.Branch, Guid.NewGuid(), "Search", "Search", [MakeRun("Search", "First", true), MakeRun("Search", "With setup", true)])])]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_ids_survive_renames_and_round_trips(bool branch)
    {
        var original = MakeRun("Search", "Trial", branch);
        var renamed = WorkspaceRun.withNames("Search", "Trial: renamed", original);
        var bytes = WorkspaceCodec.encodeRun(renamed);
        Assert.Equal(4, MessagePackSetup.deserialize<runDto>(bytes).schemaVersion);
        Assert.Equal("Trial: renamed", WorkspaceCodec.decodeRun(bytes).RunName);
        Assert.Equal(original.Id, WorkspaceCodec.decodeRun(bytes).Id);
        Assert.Null(typeof(run).GetProperty("FolderName"));
        var malformed = RunDto.fromDomain(renamed);
        malformed.id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeRun(MessagePackSetup.serialize(malformed)));
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
        Assert.Equal(new[] { "8", "12" }, runs[1].SetupParameterSpans.Value.Single().Values);
        Assert.Equal("Width", runs[1].SetupQueryParameterKeys.Value.Single().Value.Single());
        var exposed = restored.Modules;
        exposed[0] = WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Replaced", "Replaced", [], []);
        Assert.Equal("Sorting", restored.Modules[0].ModuleName);
        var invalid = WorkspaceDto.fromDomain(original);
        invalid.schemaVersion = 99;
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
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Display name", name, [], []));
    }

    [Theory]
    [InlineData("workspace.modules", false)]
    [InlineData("workspace.modules", true)]
    [InlineData("module.modules", false)]
    [InlineData("module.modules", true)]
    [InlineData("module.projects", false)]
    [InlineData("module.projects", true)]
    [InlineData("project.runs", false)]
    public void Every_sibling_collection_requires_distinct_names_and_folders(string collection, bool duplicateFolders)
    {
        var secondName = duplicateFolders ? "Second" : "FIRST";
        var secondFolder = duplicateFolders ? "FOLDER" : "another-folder";
        var modules = new[] { WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "First", "folder", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), secondName, secondFolder, [], []) };
        var projects = new[] { project.create(hierarchyKind.Root, Guid.NewGuid(), "First", "folder", []), project.create(hierarchyKind.Root, Guid.NewGuid(), secondName, secondFolder, []) };
        var runs = new[] { MakeRun("Parent", "First", false), MakeRun("Parent", secondName, false) };
        var error = Assert.Throws<ArgumentException>(() =>
        {
            switch (collection)
            {
                case "workspace.modules": workspace.create(Guid.NewGuid(), "Parent", "parent", modules); break;
                case "module.modules": WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Parent", "parent", modules, []); break;
                case "module.projects": WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Parent", "parent", [], projects); break;
                case "project.runs": project.create(hierarchyKind.Root, Guid.NewGuid(), "Parent", "parent", runs); break;
            }
        });
        Assert.Contains(duplicateFolders ? "Duplicate folder name" : "Duplicate name", error.Message);
    }

    [Fact]
    public void Names_and_folders_can_be_reused_in_separate_typed_collections()
    {
        var childProject = project.create(hierarchyKind.Root, Guid.NewGuid(), "Shared", "shared", []);
        var parentModule = WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Parent", "parent", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Shared", "shared", [], [])], [childProject]);
        Assert.Equal(parentModule.Modules.Single().FolderName, parentModule.Projects.Single().FolderName);
    }

    [Fact]
    public void Duplicate_siblings_and_mismatched_run_project_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sorting", "Sorting", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "sorting", "sorting", [], [])]));
        Assert.Throws<ArgumentException>(() => project.create(hierarchyKind.Root, Guid.NewGuid(), "Search", "Search", [MakeRun("Different", "First")]));
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "First", "same", [], []), WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Second", "SAME", [], [])]));
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "M", "m", [],
            [project.create(hierarchyKind.Root, Guid.NewGuid(), "First", "same", []), project.create(hierarchyKind.Root, Guid.NewGuid(), "Second", "SAME", [])]));
    }

    [Fact]
    public void Names_and_folder_names_round_trip_independently_and_only_folder_names_determine_paths()
    {
        var value = workspace.create(Guid.NewGuid(), "Research: 2026", "Workspace", [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Sorting / networks", "sorters",
            [WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Nested: module", "nested-m", [], [])],
            [project.create(hierarchyKind.Root, Guid.NewGuid(), "Search: prefixes", "prefix-search", [MakeRun("Search: prefixes", "Trial: first", false), MakeRun("Search: prefixes", "Trial: root", false)])])]);
        WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(loaded));
        Assert.Equal("sorters", loaded.Modules.Single().FolderName);
        Assert.Equal("nested-m", loaded.Modules.Single().Modules.Single().FolderName);
        var projectFolder = Path.Combine(Root, "modules", "sorters", "projects", "prefix-search");
        Assert.True(File.Exists(WorkspaceTestFiles.DefinitionPath(projectFolder, "project")));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[1].Id))));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[0].Id))));
        var imported = WorkspaceFileStore.LoadModuleFolder(Path.Combine(Root, "modules", "sorters"), Token);
        Assert.Equal(WorkspaceCodec.encodeModule(value.Modules.Single()), WorkspaceCodec.encodeModule(imported));
    }





    [Fact]
    public void Importing_folders_with_duplicate_saved_run_names_fails_without_writes()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var runs = Path.Combine(Root, "modules", "Sorting", "projects", "Search", "runs");
        var duplicate = RunDto.fromDomain(MakeRun("Search", "First", true));
        duplicate.id = Guid.NewGuid();
        File.WriteAllBytes(Path.Combine(runs, WorkspaceDefinitionFiles.Name(duplicate.id)), WorkspaceCodec.encodeRun(RunDto.toDomain(duplicate)));
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
        Assert.Equal("Sorting", WorkspaceCodec.decodeModule(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "modules", "Sorting", "module"))).ModuleName);
        Assert.Equal("Search", WorkspaceCodec.decodeProject(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "modules", "Sorting", "projects", "Search", "project"))).ProjectName);
        var runFile = Path.Combine(Root, "modules", "Sorting", "projects", "Search", "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs.Single(run => run.RunName == "With setup").Id));
        Assert.Equal(WorkspaceCodec.encodeRun(MakeRun("Search", "With setup", true)), File.ReadAllBytes(runFile));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(runFile)!, "results.txt"), "Keep these results");
        WorkspaceFileStore.SaveWorkspace(Root, value, value, Token);
        Assert.Equal("Keep these results", File.ReadAllText(Path.Combine(Path.GetDirectoryName(runFile)!, "results.txt")));
    }

    [Fact]
    public void Import_reads_actual_entity_hierarchy_and_preserves_run_identity_without_writing()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var moduleFolder = Path.Combine(Root, "modules", "Sorting");
        Directory.Move(Path.Combine(moduleFolder, "projects", "Search"), Path.Combine(moduleFolder, "projects", "Renamed"));
        var projectFolder = Path.Combine(moduleFolder, "projects", "Renamed");
        WorkspaceImportTests.WriteModule(Path.Combine(moduleFolder, "modules", "Added on disk"), "Added on disk", hierarchyKind.Branch);
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var imported = WorkspaceFileStore.LoadModuleFolder(moduleFolder, Token);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        var renamed = imported.Projects.Single();
        Assert.Equal("Search", renamed.ProjectName);
        Assert.Equal("Renamed", renamed.FolderName);
        var run = renamed.Runs.Single(r => r.RunName == "With setup");
        Assert.Equal("With setup", run.RunName);
        Assert.Equal(MakeRun("Search", "With setup", true).Id, run.Id);
        Assert.Equal("Search", run.ProjectName);
        Assert.Equal("Generate", run.SetupFunctionName.Value);
        Assert.Contains(imported.Modules, m => m.ModuleName == "Added on disk");
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
    }

    [Fact]
    public void Extra_folders_are_reported_and_preserved_and_can_be_imported_later()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var empty = workspace.create(Guid.NewGuid(), "Research", "Workspace", []);
        var result = WorkspaceFileStore.SaveWorkspace(Root, empty, WorkspaceFileStore.LoadDefinition(Root), Token);
        Assert.Contains(Path.Combine("modules", "Sorting"), result.RetainedFolders);
        Assert.Empty(WorkspaceFileStore.LoadDefinition(Root).Modules);
        Assert.True(Directory.Exists(Path.Combine(Root, "modules", "Sorting")));
        Assert.Equal("Sorting", WorkspaceFileStore.LoadModuleFolder(Path.Combine(Root, "modules", "Sorting"), Token).ModuleName);
    }

    [Fact]
    public void Cancelled_save_leaves_definitions_and_folders_unchanged()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var updated = workspace.create(Guid.NewGuid(), "Updated", "Workspace", []);
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.SaveWorkspace(Root, updated, Sample(), new CancellationToken(true)));
        Assert.Equal("Research", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
        Assert.True(Directory.Exists(Path.Combine(Root, "modules", "Sorting")));
    }

    [Fact]
    public void Failed_commit_rolls_back_earlier_writes_and_new_folders()
    {
        var original = Sample();
        WorkspaceFileStore.CreateWorkspace(Root, original, Token);
        var updated = workspace.create(original.Id, "Updated", "Workspace", original.Modules.Append(WorkspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "New module", "New module", [], [])).ToArray());
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => WorkspaceFileStore.SaveWorkspace(Root, updated, original, Token));
            Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        }
        Assert.Equal(WorkspaceCodec.encodeWorkspace(original), WorkspaceCodec.encodeWorkspace(WorkspaceFileStore.LoadDefinition(Root)));
        Assert.False(Directory.Exists(Path.Combine(Root, "modules", "New module")));
        Assert.Empty(Directory.GetFiles(Root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void Corrupt_run_and_workspace_definitions_are_reported_without_writes()
    {
        var runs = Directory.CreateDirectory(Path.Combine(Root, "modules", "M", "projects", "P", "runs")).FullName;
        File.WriteAllBytes(Path.Combine(runs, WorkspaceDefinitionFiles.Name(Guid.NewGuid())), [0xc1]);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadProjectFolder(Path.Combine(Root, "modules", "M", "projects", "P"), Token));
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
        viewModel.Selected!.Name = "Search";
        viewModel.AddRunCommand.Execute(null);
        viewModel.Selected!.Name = "First";
        viewModel.Selected.RunConfiguration = JsonSerializer.Serialize(RunDto.fromDomain(MakeRun("Search", "First")));
        Assert.True(viewModel.SaveWorkspaceCommand.CanExecute(null));
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        viewModel.Selected.Name = "Edited";
        await viewModel.SaveWorkspaceAsync();
        Assert.Empty(viewModel.Error);
        Assert.True(viewModel.HasSavedWorkspace);
        Assert.False(viewModel.ShowNewWorkspace);
        Assert.Equal("Edited", WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0].RunName);
        Assert.False(viewModel.DiscardChangesCommand.CanExecute(null));
        Assert.Single(viewModel.Roots[0].Children);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
