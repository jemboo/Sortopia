using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class WorkspacePersistenceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-persistence-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string Library => Path.Combine(temporary.FullName, "Library");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static workspace Sample(bool branch = false)
    {
        var kind = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        parameterBindingDto[] defaults = [
            new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = "4" },
            new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }];
        var settings = new projectSettingsDto
        {
            description = "Persistence fixture", resultsDatabaseName = "Results",
            resultsFunctionName = "sortable-tests.standard", resultsFunctionVersion = "1",
            resultsDataTypes = [new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = ["ChannelCount", "SortableDataFormat"] }],
            resultsParameterDefaults = defaults,
            setupDatabaseName = branch ? "Setup" : "", setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : "",
            setupFunctionVersion = branch ? "1" : "", setupParameterDefaults = branch ? defaults : [],
            setupDataTypes = branch ? [new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = ["ChannelCount", "SortableDataFormat"] }] : []
        };
        var parent = project.create(Guid.NewGuid(), "Search", "search", SettingsDto.toDomain(kind, settings), []);
        parameterSpanDto[] spans = [new() { keyName = "ChannelCount", values = ["4", "6"] }, new() { keyName = "SortableDataFormat", values = ["Bools"] }];
        var run = RunDto.toDomain(parent, new runDto
        {
            schemaVersion = 14, id = Guid.NewGuid(), parentProjectId = parent.Id, runName = "Trial",
            runKind = branch ? "branch" : "root", resultsParameterSpans = spans, setupParameterSpans = branch ? spans : []
        });
        parent = project.withRuns(parent, [run]);
        var module = WorkspaceModule.createInherited(Guid.NewGuid(), "Sorting", "sorting", ModuleDefaults.configuration(branch),
            ModuleOverridesDto.toDomain(kind, new moduleOverridesDto { resultsFunctionName = settings.resultsFunctionName, setupFunctionName = branch ? settings.setupFunctionName : null }), [], [parent]);
        return workspace.create(Guid.NewGuid(), "Research", "Workspace", [module]);
    }

    private static workspace Rename(workspace value, string name) => workspace.create(value.Id, name, value.FolderName, value.Modules);
    private static workspace Add(workspace value, WorkspaceModule module) => workspace.create(value.Id, value.WorkspaceName, value.FolderName, [.. value.Modules, module]);
    private static Dictionary<string, byte[]> Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllBytes);
    private static void AssertFilesEqual(Dictionary<string, byte[]> expected, string folder)
    {
        var actual = Files(folder);
        Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Definitions_round_trip_with_guid_filenames_and_file_only_runs(bool branch)
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Sample(branch), Token).Definition;
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(saved), WorkspaceCodec.encodeWorkspace(loaded));
        var module = loaded.Modules.Single();
        var project = module.Projects.Single();
        var run = project.Runs.Single();
        var moduleFolder = Path.Combine(Root, "modules", module.FolderName);
        var projectFolder = Path.Combine(moduleFolder, "projects", project.FolderName);
        var runFile = Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(run.Id));
        Assert.True(File.Exists(runFile));
        Assert.Empty(Directory.GetDirectories(Path.Combine(projectFolder, "runs")));
        Assert.Equal(run.Id, WorkspaceCodec.decodeRun(project, File.ReadAllBytes(runFile)).Id);
        Assert.Equal(project.Id, WorkspaceFileStore.LoadProjectFolder(projectFolder, Token).Id);
        Assert.Equal(module.Id, WorkspaceFileStore.LoadModuleFolder(moduleFolder, Token).Id);
        var paths = Files(Root).Keys.Order().ToArray();
        Assert.Equal(4, paths.Length);
        Assert.Equal(new[] { saved.Id, module.Id, project.Id, run.Id }.Order(),
            paths.Select(path => Guid.Parse(Path.GetFileNameWithoutExtension(path))).Order());
        WorkspaceFileStore.SaveWorkspace(Root, Rename(loaded, "Renamed"), loaded, Token);
        Assert.Equal(paths, Files(Root).Keys.Order());
        Assert.Equal("Renamed", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
    }

    [Fact]
    public void Stale_saves_and_duplicate_creation_do_not_overwrite_the_workspace()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token).Definition;
        WorkspaceFileStore.SaveWorkspace(Root, Rename(saved, "External edit"), saved, Token);
        var before = Files(Root);
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() =>
            WorkspaceFileStore.SaveWorkspace(Root, Rename(saved, "Stale edit"), saved, Token)).Message);
        Assert.Throws<IOException>(() => WorkspaceFileStore.CreateWorkspace(Root, saved, Token));
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public void Ambiguous_definitions_are_rejected_without_modifying_files()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        File.Copy(WorkspaceDefinitionFiles.Require(Root, "workspace"), Path.Combine(Root, WorkspaceDefinitionFiles.Name(Guid.NewGuid())));
        var before = Files(Root);
        Assert.Contains("Multiple workspace definition files", Assert.Throws<IOException>(() => WorkspaceFileStore.LoadDefinition(Root)).Message);
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public void Sealed_project_settings_cannot_be_changed_by_a_non_ui_caller()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token).Definition;
        var dto = WorkspaceDto.fromDomain(saved);
        dto.modules[0].projects[0].configuration.resultsDatabaseName = "Changed";
        var changed = WorkspaceDto.toDomain(dto);
        var before = Files(Root);
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveWorkspace(Root, changed, saved, Token)).Message);
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public void Module_import_is_deferred_preserves_new_ids_and_copies_user_data()
    {
        var original = WorkspaceFileStore.CreateWorkspace(Library, Sample(true), Token).Definition;
        var source = Path.Combine(Library, "modules", "sorting");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "Keep this data");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        var before = Files(Library);
        var destination = Path.Combine(Root, "modules", "sorting");
        var import = WorkspaceImport.PrepareModule(Root, source, destination, Token);
        Assert.False(Directory.Exists(Root));
        Assert.NotNull(import.Copy);
        Assert.NotEqual(original.Modules[0].Id, import.Definition.Id);
        var project = import.Definition.Projects.Single();
        Assert.Equal(project.Id, project.Runs.Single().ParentProjectId);
        Assert.Equal(original.Modules[0].Projects[0].Id, import.Copy.OriginalIds[project.Id]);
        var saved = WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Imported", "Workspace", [import.Definition]), Token, [import.Copy]);
        Assert.Equal(import.Definition.Id, saved.Definition.Modules[0].Id);
        Assert.Equal(project.Runs[0].Id, saved.Definition.Modules[0].Projects[0].Runs[0].Id);
        Assert.Equal("Keep this data", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.Equal(1, saved.CopiedFolders);
        AssertFilesEqual(before, Library);
    }

    [Fact]
    public void Project_import_assigns_new_ids_but_attaching_in_place_keeps_ids()
    {
        WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token);
        var source = Path.Combine(Root, "modules", "sorting", "projects", "search");
        var attached = WorkspaceImport.PrepareProject(Root, source, source, Token);
        Assert.Null(attached.Copy);
        var destination = Path.Combine(Root, "modules", "sorting", "projects", "copy");
        var copied = WorkspaceImport.PrepareProject(Root, source, destination, Token);
        Assert.NotNull(copied.Copy);
        Assert.NotEqual(attached.Definition.Id, copied.Definition.Id);
        Assert.NotEqual(attached.Definition.Runs[0].Id, copied.Definition.Runs[0].Id);
        Assert.Equal(copied.Definition.Id, copied.Definition.Runs[0].ParentProjectId);
        Assert.False(Directory.Exists(destination));
    }

    [Theory]
    [InlineData("add-file")]
    [InlineData("edit-file")]
    [InlineData("remove-file")]
    [InlineData("add-empty-folder")]
    [InlineData("remove-empty-folder")]
    public void Changed_import_sources_fail_before_writing_the_destination(string change)
    {
        WorkspaceFileStore.CreateWorkspace(Library, Sample(), Token);
        var source = Path.Combine(Library, "modules", "sorting");
        var note = Path.Combine(source, "notes.txt");
        File.WriteAllText(note, "Before");
        var timestamp = File.GetLastWriteTimeUtc(note);
        var emptyFolder = Path.Combine(source, "empty");
        Directory.CreateDirectory(emptyFolder);
        var import = WorkspaceImport.PrepareModule(Root, source, Path.Combine(Root, "modules", "sorting"), Token);
        switch (change)
        {
            case "add-file": File.WriteAllText(Path.Combine(source, "new.txt"), "Added after import"); break;
            case "edit-file":
                File.WriteAllText(note, "After!"); // Same length and timestamp: the content hash must detect this.
                File.SetLastWriteTimeUtc(note, timestamp);
                break;
            case "remove-file": File.Delete(note); break;
            case "add-empty-folder": Directory.CreateDirectory(Path.Combine(source, "new-empty")); break;
            case "remove-empty-folder": Directory.Delete(emptyFolder); break;
        }
        Assert.Contains("source changed", Assert.Throws<IOException>(() => WorkspaceFileStore.CreateWorkspace(Root,
            workspace.create(Guid.NewGuid(), "Imported", "Workspace", [import.Definition]), Token, [import.Copy!])).Message);
        Assert.False(Directory.Exists(Path.Combine(Root, "modules", "sorting")));
        Assert.Null(WorkspaceDefinitionFiles.Find(Root, "workspace"));
    }

    [Fact]
    public void Failed_copy_commit_rolls_back_and_can_be_retried()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Empty", "Workspace", []), Token).Definition;
        WorkspaceFileStore.CreateWorkspace(Library, Sample(), Token);
        var destination = Path.Combine(Root, "modules", "sorting");
        var import = WorkspaceImport.PrepareModule(Root, Path.Combine(Library, "modules", "sorting"), destination, Token);
        var updated = Add(saved, import.Definition);
        var before = Files(Root);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => WorkspaceFileStore.SaveWorkspace(Root, updated, saved, Token, [import.Copy!]));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Expected a file-lock failure, got {error}");
        }
        Assert.False(Directory.Exists(destination));
        AssertFilesEqual(before, Root);
        Assert.Empty(Directory.GetDirectories(Root, ".sortopia-*"));
        WorkspaceFileStore.SaveWorkspace(Root, updated, saved, Token, [import.Copy!]);
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public void Moves_preserve_entity_ids_and_user_files()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token).Definition;
        var source = Path.Combine(Root, "modules", "sorting");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "Keep on move");
        var dto = WorkspaceDto.fromDomain(saved);
        dto.modules[0].folderName = "moved";
        var destination = Path.Combine(Root, "modules", "moved");
        var result = WorkspaceFileStore.SaveWorkspace(Root, WorkspaceDto.toDomain(dto), saved, Token,
            moves: [new WorkspaceFolderMove(source, destination)]);
        Assert.Equal(1, result.MovedFolders);
        Assert.False(Directory.Exists(source));
        Assert.Equal("Keep on move", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.Equal(saved.Modules[0].Id, WorkspaceFileStore.LoadModuleFolder(destination, Token).Id);
        Assert.Equal(saved.Modules[0].Projects[0].Runs[0].Id, result.Definition.Modules[0].Projects[0].Runs[0].Id);
    }

    [Fact]
    public void Cancelled_saves_leave_all_files_unchanged()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Sample(), Token).Definition;
        var before = Files(Root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.SaveWorkspace(Root, Rename(saved, "Cancelled"), saved, cancellation.Token));
        AssertFilesEqual(before, Root);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
