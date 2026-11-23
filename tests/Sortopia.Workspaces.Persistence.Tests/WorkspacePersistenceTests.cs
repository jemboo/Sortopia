using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.Serialization.ModuleDocument;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class WorkspacePersistenceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-persistence-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string Library => Path.Combine(temporary.FullName, "Library");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static WorkspaceDocument Sample(bool branch = false)
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
            resultsParameterDefaults = [],
            setupDatabaseName = branch ? "Setup" : "", setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : "",
            setupFunctionVersion = branch ? "1" : "", setupParameterDefaults = [],
            setupDataTypes = branch ? [new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = ["ChannelCount", "SortableDataFormat"] }] : []
        };
        var parent = WorkspaceFixtures.Project(Guid.NewGuid(), "search", SettingsDto.toDomain(kind, settings), []);
        parameterSpanDto[] spans = [new() { keyName = "ChannelCount", values = ["4", "6"] }, new() { keyName = "SortableDataFormat", values = ["Bools"] }];
        var run = RunDto.toDomain(parent.Snapshot(), new runDto
        {
            schemaVersion = RunDtoSchema.version, id = Guid.NewGuid(), parentProjectId = parent.Definition.id, runName = "Trial",
            runKind = branch ? "branch" : "root", resultsParameterSpans = spans, setupParameterSpans = branch ? spans : []
        });
        parent = WorkspaceFixtures.WithRuns(parent, [run]);
        var module = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "sorting", ModuleDefaults.configuration(branch),
            ModuleOverridesDto.toDomain(kind, new moduleOverridesDto { resultsFunctionName = settings.resultsFunctionName, setupFunctionName = branch ? settings.setupFunctionName : null }), [], [parent]);
        return WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Workspace", [module]);
    }

    private static WorkspaceDocument Rename(WorkspaceDocument value, string name)
    {
        var edited = WorkspaceDocumentModule.clone(value);
        edited.Definition.workspaceName = name;
        return edited;
    }
    private static WorkspaceDocument Add(WorkspaceDocument value, ModuleDocument module) =>
        WorkspaceDocumentModule.withModules(value, [.. value.Modules, module]);
    private static WorkspaceDocument Imported(ModuleDocument module) =>
        WorkspaceDocumentModule.withModules(WorkspaceFixtures.Workspace(Guid.NewGuid(), "Imported", "Workspace", []), [module]);
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
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(branch), Token).Workspace;
        var loaded = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), loaded.CreateEditDocument()));
        var module = loaded.CreateEditDocument().Modules.Single();
        var project = module.Projects.Single();
        var run = project.Runs.Single();
        var moduleFolder = Path.Combine(Root, module.Definition.moduleName);
        var projectFolder = Path.Combine(moduleFolder, project.Definition.projectName);
        var runFile = Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(run.id));
        Assert.True(File.Exists(runFile));
        Assert.Empty(Directory.GetDirectories(Path.Combine(projectFolder, "runs")));
        Assert.Equal(run.id, RunCodec.decodeRun(WorkspaceDefinitionResolution.projectSnapshot(project.Definition), File.ReadAllBytes(runFile)).Id);
        Assert.Equal(project.Definition.id, WorkspaceFileStore.LoadProjectDocument(projectFolder, Token).Definition.id);
        Assert.Equal(module.Definition.id, WorkspaceFileStore.LoadModuleDocument(moduleFolder, Token).Definition.id);
        var paths = Files(Root).Keys.Order().ToArray();
        Assert.Equal(4, paths.Length);
        Assert.Equal(new[] { saved.CreateEditDocument().Definition.id, module.Definition.id, project.Definition.id, run.id }.Order(),
            paths.Select(path => Guid.Parse(Path.GetFileNameWithoutExtension(path))).Order());
        WorkspaceFileStore.SaveDocument(Rename(loaded.CreateEditDocument(), "Renamed"), loaded, Token);
        Assert.Equal(paths, Files(Root).Keys.Order());
        Assert.Equal("Renamed", WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Definition.workspaceName);
    }

    [Fact]
    public void Stale_saves_and_duplicate_creation_do_not_overwrite_the_workspace()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(), Token).Workspace;
        WorkspaceFileStore.SaveDocument(Rename(saved.CreateEditDocument(), "External edit"), saved, Token);
        var before = Files(Root);
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() =>
            WorkspaceFileStore.SaveDocument(Rename(saved.CreateEditDocument(), "Stale edit"), saved, Token)).Message);
        Assert.Throws<IOException>(() => WorkspaceFileStore.CreateDocument(Root, saved.CreateEditDocument(), Token));
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public void Ambiguous_definitions_are_rejected_without_modifying_files()
    {
        WorkspaceFileStore.CreateDocument(Root, Sample(), Token);
        File.Copy(WorkspaceDefinitionFiles.Require(Root, "workspace"), Path.Combine(Root, WorkspaceDefinitionFiles.Name(Guid.NewGuid())));
        var before = Files(Root);
        Assert.Contains("Multiple workspace definition files", Assert.Throws<IOException>(() => WorkspaceFileStore.LoadWorkspace(Root)).Message);
        AssertFilesEqual(before, Root);
    }

    [Fact]
    public void Sealed_project_settings_cannot_be_changed_by_a_non_ui_caller()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(), Token).Workspace;
        var dto = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        dto.Modules[0].Projects[0].Definition.configuration.resultsDatabaseName = "Changed";
        var changed = dto;
        var before = Files(Root);
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(changed, saved, Token)).Message);
        AssertFilesEqual(before, Root);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sealed_project_names_and_folders_can_change_with_stable_ids_and_settings(bool branch)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(branch), Token).Workspace;
        var original = saved.CreateEditDocument().Modules[0].Projects[0];
        var source = Path.Combine(Root, "sorting", "search");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "Keep contents");
        var dto = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        dto.Modules[0].Projects[0].Definition.projectName = "Renamed";


        var destination = Path.Combine(Root, "sorting", "Renamed");
        var result = WorkspaceFileStore.SaveDocument(dto, saved, Token, moves: [new(source, destination)]);
        var renamed = new WorkspaceDocumentIndex(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument()).TryFindRun(original.Runs[0].id).Value.Parent.Document;
        Assert.Equal("Renamed", renamed.Definition.projectName); Assert.Equal("Renamed", renamed.Definition.projectName);
        Assert.True(renamed.Definition.isSealed); Assert.Equal(SettingsDto.toDomain(renamed.Family(), original.Definition.configuration), renamed.Configuration());
        Assert.Equal(original.Runs[0].id, renamed.Runs[0].id); Assert.Equal(original.Definition.id, renamed.ResolvedRuns()[0].ParentProjectId);
        Assert.Equal("Renamed", renamed.ResolvedRuns()[0].ProjectName);
        Assert.Equal("Keep contents", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.False(Directory.Exists(source)); Assert.Equal(1, result.MovedFolders);
    }

    [Fact]
    public void Module_import_is_deferred_preserves_new_ids_and_copies_user_data()
    {
        var original = WorkspaceFileStore.CreateDocument(Library, Sample(true), Token).Workspace;
        var source = Path.Combine(Library, "sorting");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "Keep this data");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        var before = Files(Library);
        var destination = Path.Combine(Root, "sorting");
        var import = WorkspaceImport.PrepareModule(Root, source, destination, Token);
        Assert.False(Directory.Exists(Root));
        Assert.NotNull(import.Copy);
        Assert.NotEqual(original.CreateEditDocument().Modules[0].Definition.id, import.Definition.Definition.id);
        var project = import.Definition.Projects.Single();
        Assert.Equal(project.Definition.id, project.Runs.Single().parentProjectId);
        Assert.Equal(original.CreateEditDocument().Modules[0].Projects[0].Definition.id, import.Copy.OriginalIds[project.Definition.id]);
        var saved = WorkspaceFileStore.CreateDocument(Root, Imported(import.Definition), Token, [import.Copy]);
        Assert.Equal(import.Definition.Definition.id, saved.Workspace.CreateEditDocument().Modules[0].Definition.id);
        Assert.Equal(project.Runs[0].id, saved.Workspace.CreateEditDocument().Modules[0].Projects[0].Runs[0].id);
        Assert.Equal("Keep this data", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(destination, "empty")));
        Assert.Equal(1, saved.CopiedFolders);
        AssertFilesEqual(before, Library);
    }

    [Fact]
    public void Project_import_assigns_new_ids_but_attaching_in_place_keeps_ids()
    {
        WorkspaceFileStore.CreateDocument(Root, Sample(), Token);
        var source = Path.Combine(Root, "sorting", "search");
        var attached = WorkspaceImport.PrepareProject(Root, source, source, Token);
        Assert.Null(attached.Copy);
        var destination = Path.Combine(Root, "sorting", "copy");
        var copied = WorkspaceImport.PrepareProject(Root, source, destination, Token);
        Assert.NotNull(copied.Copy);
        Assert.NotEqual(attached.Definition.Definition.id, copied.Definition.Definition.id);
        Assert.NotEqual(attached.Definition.Runs[0].id, copied.Definition.Runs[0].id);
        Assert.Equal(copied.Definition.Definition.id, copied.Definition.Runs[0].parentProjectId);
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
        WorkspaceFileStore.CreateDocument(Library, Sample(), Token);
        var source = Path.Combine(Library, "sorting");
        var note = Path.Combine(source, "notes.txt");
        File.WriteAllText(note, "Before");
        var timestamp = File.GetLastWriteTimeUtc(note);
        var emptyFolder = Path.Combine(source, "empty");
        Directory.CreateDirectory(emptyFolder);
        var import = WorkspaceImport.PrepareModule(Root, source, Path.Combine(Root, "sorting"), Token);
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
        Assert.Contains("source changed", Assert.Throws<IOException>(() => WorkspaceFileStore.CreateDocument(Root,
            Imported(import.Definition), Token, [import.Copy!])).Message);
        Assert.False(Directory.Exists(Path.Combine(Root, "sorting")));
        Assert.Null(WorkspaceDefinitionFiles.Find(Root, "workspace"));
    }

    [Fact]
    public void Failed_copy_commit_rolls_back_and_can_be_retried()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspaceFixtures.Workspace(Guid.NewGuid(), "Empty", "Workspace", []), Token).Workspace;
        WorkspaceFileStore.CreateDocument(Library, Sample(), Token);
        var destination = Path.Combine(Root, "sorting");
        var import = WorkspaceImport.PrepareModule(Root, Path.Combine(Library, "sorting"), destination, Token);
        var updated = Add(saved.CreateEditDocument(), import.Definition);
        var before = Files(Root);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => WorkspaceFileStore.SaveDocument(updated, saved, Token, [import.Copy!]));
            Assert.True(error is IOException or UnauthorizedAccessException, $"Expected a file-lock failure, got {error}");
        }
        Assert.False(Directory.Exists(destination));
        AssertFilesEqual(before, Root);
        Assert.Empty(Directory.GetDirectories(Root, ".sortopia-*"));
        WorkspaceFileStore.SaveDocument(updated, saved, Token, [import.Copy!]);
        Assert.True(Directory.Exists(destination));
    }

    [Fact]
    public void Moves_preserve_entity_ids_and_user_files()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(), Token).Workspace;
        var source = Path.Combine(Root, "sorting");
        File.WriteAllText(Path.Combine(source, "notes.txt"), "Keep on move");
        var dto = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        dto.Modules[0].Definition.moduleName = "moved";

        var destination = Path.Combine(Root, "moved");
        var result = WorkspaceFileStore.SaveDocument(dto, saved, Token, moves: [new WorkspaceFolderMove(source, destination)]);
        Assert.Equal(1, result.MovedFolders);
        Assert.False(Directory.Exists(source));
        Assert.Equal("Keep on move", File.ReadAllText(Path.Combine(destination, "notes.txt")));
        Assert.Equal(saved.CreateEditDocument().Modules[0].Definition.id, WorkspaceFileStore.LoadModuleDocument(destination, Token).Definition.id);
        Assert.Equal(saved.CreateEditDocument().Modules[0].Projects[0].Runs[0].id, result.Workspace.CreateEditDocument().Modules[0].Projects[0].Runs[0].id);
    }

    [Fact]
    public void Cancelled_saves_leave_all_files_unchanged()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Sample(), Token).Workspace;
        var before = Files(Root);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.SaveDocument(Rename(saved.CreateEditDocument(), "Cancelled"), saved, cancellation.Token));
        AssertFilesEqual(before, Root);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
