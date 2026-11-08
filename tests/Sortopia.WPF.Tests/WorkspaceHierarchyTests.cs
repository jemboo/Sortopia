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
        schemaVersion = 3, id = WorkspaceTestFiles.Id(projectName + "/" + name), runKind = branch ? "branch" : "root", projectName = projectName, runName = name,
        description = "Keep every setting: α", resultsDatabaseName = "Results", executeFunctionName = "Evaluate",
        outputParameterSpans = [new parameterSpanDto { keyName = "Seed", values = ["7", "11"] }],
        outputDataTypes = [4], outputQueryParameterKeys = ["Seed"],
        setupDatabaseName = branch ? "Setup" : "", setupFunctionName = branch ? "Generate" : "",
        setupParameterSpans = branch ? [new parameterSpanDto { keyName = "Width", values = ["8", "12"] }] : [],
        setupOutputDataTypes = branch ? [3] : [],
        setupQueryParameterKeys = branch ? [new queryKeysDto { outputDataType = 3, keys = ["Width"] }] : []
    });
    private workspace Sample() => workspace.create(Guid.NewGuid(), "Research", "Workspace", [WorkspaceModule.create(Guid.NewGuid(), "Sorting", "Sorting",
        [WorkspaceModule.create(Guid.NewGuid(), "Nested module", "Nested module", [], [])],
        [project.create(Guid.NewGuid(), "Search", "Search", [MakeRun("Search", "First"), MakeRun("Search", "With setup", true)],
            [project.create(Guid.NewGuid(), "Nested project", "Nested project", [], [])])])]);

    private static Dictionary<string, object?> LegacyRun(run value)
    {
        var dto = RunDto.fromDomain(value);
        var fields = typeof(runDto).GetProperties().Where(property => property.Name != "id")
            .ToDictionary(property => property.Name, property => property.GetValue(dto));
        fields["schemaVersion"] = 1;
        return fields;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Old_run_files_receive_stable_ids_and_new_run_files_preserve_them_when_renamed(bool branch)
    {
        var original = MakeRun("Search", "Trial", branch);
        var legacy = WorkspaceCodec.decodeRun(MessagePackSetup.serialize(LegacyRun(original)));
        Assert.NotEqual(Guid.Empty, legacy.Id);
        Assert.Equal(legacy.Id, WorkspaceCodec.decodeRun(MessagePackSetup.serialize(LegacyRun(original))).Id);
        var renamed = WorkspaceRun.withNames("Search", "Trial: renamed", legacy);
        var bytes = WorkspaceCodec.encodeRun(renamed);
        Assert.Equal(3, MessagePackSetup.deserialize<runDto>(bytes).schemaVersion);
        Assert.Equal("Trial: renamed", WorkspaceCodec.decodeRun(bytes).RunName);
        Assert.Equal(legacy.Id, WorkspaceCodec.decodeRun(bytes).Id);
        Assert.Null(typeof(run).GetProperty("FolderName"));
        var malformed = RunDto.fromDomain(renamed);
        malformed.id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeRun(MessagePackSetup.serialize(malformed)));
    }

    [Fact]
    public void MessagePack_round_trip_preserves_recursive_hierarchy_and_both_complete_run_types()
    {
        var original = Sample();
        var restored = WorkspaceCodec.decodeWorkspace(WorkspaceCodec.encodeWorkspace(original));
        Assert.Equal(WorkspaceCodec.encodeWorkspace(original), WorkspaceCodec.encodeWorkspace(restored));
        var runs = restored.Modules[0].Projects[0].Runs;
        Assert.False(runs[0].HasSetup);
        Assert.True(runs[1].HasSetup);
        Assert.Equal(new[] { "8", "12" }, runs[1].SetupParameterSpans.Value.Single().Values);
        Assert.Equal("Width", runs[1].SetupQueryParameterKeys.Value.Single().Value.Single());
        var exposed = restored.Modules;
        exposed[0] = WorkspaceModule.create(Guid.NewGuid(), "Replaced", "Replaced", [], []);
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
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(Guid.NewGuid(), "Display name", name, [], []));
    }

    [Theory]
    [InlineData("workspace.modules", false)]
    [InlineData("workspace.modules", true)]
    [InlineData("module.modules", false)]
    [InlineData("module.modules", true)]
    [InlineData("module.projects", false)]
    [InlineData("module.projects", true)]
    [InlineData("project.projects", false)]
    [InlineData("project.projects", true)]
    [InlineData("project.runs", false)]
    public void Every_sibling_collection_requires_distinct_names_and_folders(string collection, bool duplicateFolders)
    {
        var secondName = duplicateFolders ? "Second" : "FIRST";
        var secondFolder = duplicateFolders ? "FOLDER" : "another-folder";
        var modules = new[] { WorkspaceModule.create(Guid.NewGuid(), "First", "folder", [], []), WorkspaceModule.create(Guid.NewGuid(), secondName, secondFolder, [], []) };
        var projects = new[] { project.create(Guid.NewGuid(), "First", "folder", [], []), project.create(Guid.NewGuid(), secondName, secondFolder, [], []) };
        var runs = new[] { MakeRun("Parent", "First", false), MakeRun("Parent", secondName, true) };
        var error = Assert.Throws<ArgumentException>(() =>
        {
            switch (collection)
            {
                case "workspace.modules": workspace.create(Guid.NewGuid(), "Parent", "parent", modules); break;
                case "module.modules": WorkspaceModule.create(Guid.NewGuid(), "Parent", "parent", modules, []); break;
                case "module.projects": WorkspaceModule.create(Guid.NewGuid(), "Parent", "parent", [], projects); break;
                case "project.projects": project.create(Guid.NewGuid(), "Parent", "parent", [], projects); break;
                case "project.runs": project.create(Guid.NewGuid(), "Parent", "parent", runs, []); break;
            }
        });
        Assert.Contains(duplicateFolders ? "Duplicate folder name" : "Duplicate name", error.Message);
    }

    [Fact]
    public void Names_and_folders_can_be_reused_in_separate_typed_collections()
    {
        var childProject = project.create(Guid.NewGuid(), "Shared", "shared", [], []);
        var parentProject = project.create(Guid.NewGuid(), "Parent", "parent", [MakeRun("Parent", "Shared")], [childProject]);
        var parentModule = WorkspaceModule.create(Guid.NewGuid(), "Parent", "parent", [WorkspaceModule.create(Guid.NewGuid(), "Shared", "shared", [], [])], [childProject]);
        Assert.Equal(parentProject.Runs.Single().RunName, parentProject.Projects.Single().ProjectName);
        Assert.Equal(parentModule.Modules.Single().FolderName, parentModule.Projects.Single().FolderName);
    }

    [Fact]
    public void Duplicate_siblings_and_mismatched_run_project_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceModule.create(Guid.NewGuid(), "Sorting", "Sorting", [], []), WorkspaceModule.create(Guid.NewGuid(), "sorting", "sorting", [], [])]));
        Assert.Throws<ArgumentException>(() => project.create(Guid.NewGuid(), "Search", "Search", [MakeRun("Different", "First")], []));
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.NewGuid(), "Research", "Workspace",
            [WorkspaceModule.create(Guid.NewGuid(), "First", "same", [], []), WorkspaceModule.create(Guid.NewGuid(), "Second", "SAME", [], [])]));
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(Guid.NewGuid(), "M", "m", [],
            [project.create(Guid.NewGuid(), "First", "same", [], []), project.create(Guid.NewGuid(), "Second", "SAME", [], [])]));
        Assert.Throws<ArgumentException>(() => project.create(Guid.NewGuid(), "P", "p", [],
            [project.create(Guid.NewGuid(), "First", "same", [], []), project.create(Guid.NewGuid(), "Second", "SAME", [], [])]));
    }

    [Fact]
    public void Names_and_folder_names_round_trip_independently_and_only_folder_names_determine_paths()
    {
        var value = workspace.create(Guid.NewGuid(), "Research: 2026", "Workspace", [WorkspaceModule.create(Guid.NewGuid(), "Sorting / networks", "sorters",
            [WorkspaceModule.create(Guid.NewGuid(), "Nested: module", "nested-m", [], [])],
            [project.create(Guid.NewGuid(), "Search: prefixes", "prefix-search", [MakeRun("Search: prefixes", "Trial: branch", true), MakeRun("Search: prefixes", "Trial: root", false)],
                [project.create(Guid.NewGuid(), "Nested: project", "nested-p", [], [])])])]);
        WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(loaded));
        Assert.Equal("sorters", loaded.Modules.Single().FolderName);
        Assert.Equal("nested-m", loaded.Modules.Single().Modules.Single().FolderName);
        Assert.Equal("nested-p", loaded.Modules.Single().Projects.Single().Projects.Single().FolderName);
        var projectFolder = Path.Combine(Root, "modules", "sorters", "projects", "prefix-search");
        Assert.True(File.Exists(WorkspaceTestFiles.DefinitionPath(projectFolder, "project")));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[1].Id))));
        Assert.True(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs[0].Id))));
        var imported = WorkspaceFileStore.LoadModuleFolder(Path.Combine(Root, "modules", "sorters"), Token);
        Assert.Equal(WorkspaceCodec.encodeModule(value.Modules.Single()), WorkspaceCodec.encodeModule(imported));
    }

    [Fact]
    public void Version_one_files_load_with_inferred_folder_names_and_save_as_version_two_without_root_paths()
    {
        var oldProject = new { schemaVersion = 1, projectName = "Search", runs = new[] { LegacyRun(MakeRun("Search", "Trial")) }, projects = Array.Empty<object>() };
        var oldModule = new { schemaVersion = 1, moduleName = "Sorting", modules = Array.Empty<object>(), projects = new[] { oldProject } };
        var oldWorkspace = new { schemaVersion = 1, workspaceName = "Research", rootFolder = Root, modules = new[] { oldModule } };
        var decoded = WorkspaceCodec.decodeWorkspace(MessagePackSetup.serialize(oldWorkspace));
        Assert.Equal("Workspace", decoded.FolderName);
        Assert.Equal("Sorting", decoded.Modules.Single().FolderName);
        Assert.Equal("Search", decoded.Modules.Single().Projects.Single().FolderName);
        Assert.NotEqual(Guid.Empty, decoded.Modules.Single().Projects.Single().Runs.Single().Id);
        Assert.Equal("Sorting", WorkspaceCodec.decodeModule(MessagePackSetup.serialize(oldModule)).FolderName);
        Assert.Equal("Search", WorkspaceCodec.decodeProject(MessagePackSetup.serialize(oldProject)).FolderName);
        var migrated = WorkspaceCodec.encodeWorkspace(decoded);
        Assert.Equal(3, MessagePackSetup.deserialize<workspaceDto>(migrated).schemaVersion);
        Assert.Null(MessagePackSetup.deserialize<workspaceDtoV1>(migrated).rootFolder);
        Assert.Equal(migrated, WorkspaceCodec.encodeWorkspace(WorkspaceCodec.decodeWorkspace(migrated)));
        Directory.CreateDirectory(Root);
        File.WriteAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), MessagePackSetup.serialize(oldWorkspace));
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        WorkspaceFileStore.SaveWorkspace(Root, workspace.create(loaded.Id, "Renamed", loaded.FolderName, loaded.Modules), loaded, Token);
        Assert.Equal("Renamed", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
        Assert.Equal(3, MessagePackSetup.deserialize<workspaceDto>(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"))).schemaVersion);
    }

    [Fact]
    public void Version_two_workspace_snapshots_with_old_run_definitions_still_load()
    {
        var dto = WorkspaceDto.fromDomain(Sample());
        foreach (var run in dto.modules.Single().projects.Single().runs)
        {
            run.schemaVersion = 1;
            run.id = Guid.Empty;
        }
        var loaded = WorkspaceCodec.decodeWorkspace(MessagePackSetup.serialize(dto));
        Assert.All(loaded.Modules.Single().Projects.Single().Runs, run => Assert.NotEqual(Guid.Empty, run.Id));
    }

    [Fact]
    public void Importing_folders_with_duplicate_saved_run_names_fails_without_writes()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var runs = Path.Combine(Root, "modules", "Sorting", "projects", "Search", "runs");
        var copy = Directory.CreateDirectory(Path.Combine(runs, "Copy of first"));
        File.Copy(Path.Combine(runs, WorkspaceDefinitionFiles.Name(MakeRun("Search", "First").Id)), WorkspaceTestFiles.DefinitionPath(copy.FullName, "run"));
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
        Assert.Equal(7, Directory.GetFiles(Root, "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(WorkspaceFileStore.LoadDefinition(Root)));
        Assert.Equal("Sorting", WorkspaceCodec.decodeModule(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "modules", "Sorting", "module"))).ModuleName);
        Assert.Equal("Search", WorkspaceCodec.decodeProject(File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "modules", "Sorting", "projects", "Search", "project"))).ProjectName);
        var runFile = Path.Combine(Root, "modules", "Sorting", "projects", "Search", "runs", WorkspaceDefinitionFiles.Name(value.Modules.Single().Projects.Single().Runs.Single(run => run.HasSetup).Id));
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
        Directory.Move(Path.Combine(projectFolder, "projects", "Nested project"), Path.Combine(temporary.FullName, "Removed project"));
        Directory.CreateDirectory(Path.Combine(moduleFolder, "modules", "Added on disk"));
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var imported = WorkspaceFileStore.LoadModuleFolder(moduleFolder, Token);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        var renamed = imported.Projects.Single();
        Assert.Equal("Search", renamed.ProjectName);
        Assert.Equal("Renamed", renamed.FolderName);
        Assert.Empty(renamed.Projects);
        var run = renamed.Runs.Single(r => r.HasSetup);
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
        var updated = workspace.create(original.Id, "Updated", "Workspace", original.Modules.Append(WorkspaceModule.create(Guid.NewGuid(), "New module", "New module", [], [])).ToArray());
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
        File.WriteAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), [0xc1]);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadDefinition(Root));
        Assert.Equal(new byte[] { 0xc1 }, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
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
