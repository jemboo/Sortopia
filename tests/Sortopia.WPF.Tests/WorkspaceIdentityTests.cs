using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.@module;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceIdentityTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-identity-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Empty_ids_and_duplicate_run_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.Empty, "W", "W", []));
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(Guid.Empty, "M", "M", [], []));
        Assert.Throws<ArgumentException>(() => project.create(Guid.Empty, "P", "P", [], []));
        foreach (var branch in new[] { false, true })
        {
            var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch));
            dto.id = Guid.Empty;
            Assert.Throws<ArgumentException>(() => RunDto.toDomain(dto));
        }
        var first = WorkspaceHierarchyTests.MakeRun("P", "A");
        var second = WorkspaceRun.withNames("P", "B", first);
        Assert.Throws<ArgumentException>(() => project.create(Guid.NewGuid(), "P", "P", [first, second], []));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Version_two_run_folders_migrate_to_id_files_without_losing_outputs(bool branch)
    {
        var root = temporary.FullName;
        var moduleFolder = Path.Combine(root, "modules", "m-folder");
        var projectFolder = Path.Combine(moduleFolder, "projects", "p-folder");
        var oldRunFolder = Path.Combine(projectFolder, "runs", "old-run-folder");
        Directory.CreateDirectory(oldRunFolder);
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "Run: descriptive", branch));
        var oldRun = typeof(runDto).GetProperties().Where(property => property.Name != "id")
            .ToDictionary(property => property.Name, property => property.GetValue(dto));
        oldRun["schemaVersion"] = 2;
        oldRun["folderName"] = "old-run-folder";
        var oldProject = new { schemaVersion = 2, projectName = "P", folderName = "p-folder", runs = new[] { oldRun }, projects = Array.Empty<object>() };
        var oldModule = new { schemaVersion = 2, moduleName = "M", folderName = "m-folder", projects = new[] { oldProject }, modules = Array.Empty<object>() };
        var oldWorkspace = new { schemaVersion = 2, workspaceName = "W", folderName = temporary.Name, modules = new[] { oldModule } };
        var legacyFile = Path.Combine(oldRunFolder, $"run-{Guid.NewGuid():D}.msgpack");
        File.WriteAllBytes(legacyFile, MessagePackSetup.serialize(oldRun));
        File.WriteAllBytes(Path.Combine(projectFolder, "project.msgpack"), MessagePackSetup.serialize(oldProject));
        File.WriteAllBytes(Path.Combine(moduleFolder, "module.msgpack"), MessagePackSetup.serialize(oldModule));
        File.WriteAllBytes(Path.Combine(root, "workspace.msgpack"), MessagePackSetup.serialize(oldWorkspace));
        File.WriteAllText(Path.Combine(oldRunFolder, "results.txt"), "Keep results");

        var loaded = WorkspaceFileStore.LoadDefinition(root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(loaded), WorkspaceCodec.encodeWorkspace(WorkspaceFileStore.LoadDefinition(root)));
        var result = WorkspaceFileStore.SaveWorkspace(root, loaded, loaded, Token).Definition;
        var module = result.Modules.Single();
        var project = module.Projects.Single();
        var run = project.Runs.Single();
        Assert.Equal(Path.Combine(root, WorkspaceDefinitionFiles.Name(result.Id)), WorkspaceDefinitionFiles.Require(root, "workspace"));
        Assert.Equal(Path.Combine(moduleFolder, WorkspaceDefinitionFiles.Name(module.Id)), WorkspaceDefinitionFiles.Require(moduleFolder, "module"));
        Assert.Equal(Path.Combine(projectFolder, WorkspaceDefinitionFiles.Name(project.Id)), WorkspaceDefinitionFiles.Require(projectFolder, "project"));
        var runFile = Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(run.Id));
        Assert.Equal(WorkspaceCodec.encodeRun(run), File.ReadAllBytes(runFile));
        Assert.False(File.Exists(legacyFile));
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(oldRunFolder, "results.txt")));
        Assert.Equal(run.Id, WorkspaceFileStore.LoadProjectFolder(projectFolder, Token).Runs.Single().Id);
        Assert.DoesNotContain("folderName", typeof(runDto).GetProperties().Select(property => property.Name));
        Assert.Null(typeof(rootRunConfig).GetProperty("FolderName"));
        Assert.Null(typeof(branchRunConfig).GetProperty("FolderName"));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
