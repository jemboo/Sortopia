using Sortopia.Core.Serialization;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class ShortFolderLayoutTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-short-layout-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static workspace Mixed(bool branch = false, bool containerNames = false)
    {
        var sample = WorkspacePersistenceTests.Sample(branch);
        var dto = WorkspaceDto.fromDomain(sample);
        var child = workspaceModule.createInherited(Guid.NewGuid(), "Nested", containerNames ? "projects" : "nested",
            ModuleDefaults.configuration(branch), ModuleOverridesDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, new()), [], []);
        dto.modules[0].modules = [ModuleDto.fromDomain(child)];
        if (containerNames) dto.modules[0].projects[0].folderName = "modules";
        return WorkspaceDto.toDomain(dto);
    }
    private static void Write<T>(string folder, Guid id, T definition)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(id)), MessagePackSetup.serialize(definition));
    }
    private void WritePreviousLayout(workspace value)
    {
        var root = WorkspaceDefinitionCodec.workspace(value); root.schemaVersion = 15;
        Write(Root, value.Id, root);
        void Module(string parent, workspaceModule module, inheritedFromDto? origin)
        {
            var folder = Path.Combine(parent, "modules", module.FolderName);
            var dto = WorkspaceDefinitionCodec.moduleWithOrigin(origin!, module); dto.schemaVersion = 15;
            Write(folder, module.Id, dto);
            var inherited = new inheritedFromDto { moduleId = module.Id, moduleName = module.ModuleName };
            foreach (var child in module.Modules) Module(folder, child, inherited);
            foreach (var project in module.Projects)
            {
                var path = Path.Combine(folder, "projects", project.FolderName);
                var p = WorkspaceDefinitionCodec.projectWithOrigin(inherited, project); p.schemaVersion = 15; Write(path, project.Id, p);
                foreach (var run in project.Runs) {
                    Directory.CreateDirectory(Path.Combine(path, "runs"));
                    File.WriteAllBytes(Path.Combine(path, "runs", WorkspaceDefinitionFiles.Name(run.Id)), WorkspaceCodec.encodeRun(run));
                }
            }
        }
        foreach (var module in value.Modules) Module(Root, module, null);
    }
    private static Dictionary<string, byte[]> Files(string root) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);
    private static void Unchanged(Dictionary<string, byte[]> expected, string root)
    {
        var actual = Files(root); Assert.Equal(expected.Keys.Order(), actual.Keys.Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_children_are_direct_folders_and_names_are_unique_across_both_types(bool branch)
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Mixed(branch), Token).Definition;
        var module = Path.Combine(Root, "sorting");
        Assert.True(Directory.Exists(Path.Combine(module, "nested")));
        Assert.True(Directory.Exists(Path.Combine(module, "search")));
        Assert.False(Directory.Exists(Path.Combine(Root, "modules")));
        Assert.False(Directory.Exists(Path.Combine(module, "projects")));
        var opened = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(saved, opened);
        Assert.Equal(saved.Modules[0].Projects[0].Configuration, WorkspaceFileStore.LoadProjectFolder(Path.Combine(module, "search"), Token).Configuration);
        var duplicate = WorkspaceDto.fromDomain(saved); duplicate.modules[0].modules[0].folderName = "SEARCH";
        Assert.Contains("module and project children", Assert.Throws<ArgumentException>(() => WorkspaceDto.toDomain(duplicate)).Message);
        var header = WorkspaceDefinitionCodec.moduleWithOrigin(null!, saved.Modules[0]); header.modules[0].folderName = "SEARCH";
        Assert.Contains("Duplicate folder", Assert.Throws<ArgumentException>(() => WorkspaceDefinitionCodec.decodeModule(MessagePackSetup.serialize(header))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conversion_preserves_ids_runs_and_all_project_payloads_including_container_named_children(bool containerNames)
    {
        var value = Mixed(containerNames: containerNames); WritePreviousLayout(value);
        var project = value.Modules[0].Projects[0];
        var beforeProject = Path.Combine(Root, "modules", "sorting", "projects", project.FolderName);
        foreach (var relative in new[] { Path.Combine("data", "setup", "SorterModelSet", Guid.NewGuid() + ".msgpack"),
            Path.Combine("data", "results", "SortableTests", Guid.NewGuid() + ".msgpack"), Path.Combine("runParameters", Guid.NewGuid() + ".msgpack"), "notes.txt" }) {
            var path = Path.Combine(beforeProject, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, [1, 4, 9, 16]);
        }
        var payloads = Files(beforeProject).Where(file => file.Key != WorkspaceDefinitionFiles.Name(project.Id)).ToDictionary();
        Assert.Contains("Convert", Assert.Throws<ArgumentException>(() => WorkspaceFileStore.LoadDefinition(Root)).Message);
        var result = WorkspaceFileStore.ConvertFolderLayout(Root, Token);
        var opened = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(value.Id, opened.Id); Assert.Equal(value.Modules[0].Id, opened.Modules[0].Id);
        Assert.Equal(project, opened.FindProject(project.Id).Value);
        Assert.Equal(3, result.MovedFolders);
        Assert.False(Directory.Exists(Path.Combine(Root, "modules")));
        var afterProject = Path.Combine(Root, "sorting", project.FolderName);
        foreach (var (path, bytes) in payloads) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(afterProject, path)));
        Assert.Equal(16, WorkspaceDefinitionFiles.Version(Root, "workspace"));
        Assert.Equal(16, WorkspaceDefinitionFiles.Version(afterProject, "project"));
        Assert.DoesNotContain(Directory.GetDirectories(Root), path => Path.GetFileName(path).StartsWith(".sortopia-"));
        WorkspaceFileStore.SaveWorkspace(Root, opened, opened, Token);
    }

    [Fact]
    public void Conversion_rejects_cross_type_collisions_and_existing_destinations_without_changing_files()
    {
        var value = Mixed(); WritePreviousLayout(value);
        var parent = Path.Combine(Root, "modules", "sorting");
        var path = WorkspaceDefinitionFiles.Require(parent, "module");
        var header = MessagePackSetup.deserialize<moduleDefinitionDto>(File.ReadAllBytes(path));
        header.modules[0].folderName = "SEARCH"; File.WriteAllBytes(path, MessagePackSetup.serialize(header));
        var before = Files(Root);
        Assert.Contains("Duplicate folder", Assert.Throws<ArgumentException>(() => WorkspaceFileStore.ConvertFolderLayout(Root, Token)).Message);
        Unchanged(before, Root);
        header.modules[0].folderName = "nested"; File.WriteAllBytes(path, MessagePackSetup.serialize(header));
        Directory.CreateDirectory(Path.Combine(Root, "sorting")); File.WriteAllText(Path.Combine(Root, "sorting", "notes.txt"), "Preserve me");
        before = Files(Root);
        Assert.Contains("destination already exists", Assert.Throws<IOException>(() => WorkspaceFileStore.ConvertFolderLayout(Root, Token)).Message);
        Unchanged(before, Root);
    }

    [Fact]
    public void Failed_conversion_rolls_back_folders_and_definition_versions()
    {
        if (!OperatingSystem.IsWindows()) return;
        WritePreviousLayout(Mixed(containerNames: true)); var before = Files(Root);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => WorkspaceFileStore.ConvertFolderLayout(Root, Token)) is IOException or UnauthorizedAccessException);
        Unchanged(before, Root);
        Assert.Equal(15, WorkspaceDefinitionFiles.Version(Root, "workspace"));
        Assert.DoesNotContain(Directory.GetDirectories(Root), path => Path.GetFileName(path).StartsWith(".sortopia-"));
    }
    public void Dispose() => temporary.Delete(true);
}
