using Sortopia.Core.Serialization;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class ShortFolderLayoutTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-short-layout-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static WorkspaceDocument Mixed(bool branch = false)
    {
        var sample = WorkspacePersistenceTests.Sample(branch);
        var dto = WorkspaceDocumentModule.clone(sample);
        var child = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "nested",
            ModuleDefaults.configuration(branch), ModuleOverridesDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, new()), [], []);
        dto.Modules[0] = WorkspaceDocumentModule.withModuleChildren(dto.Modules[0], [child], dto.Modules[0].Projects);
        return dto;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mixed_children_are_direct_folders_and_names_are_unique_across_both_types(bool branch)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Mixed(branch), Token).Workspace;
        var module = Path.Combine(Root, "sorting");
        Assert.True(Directory.Exists(Path.Combine(module, "nested")));
        Assert.True(Directory.Exists(Path.Combine(module, "search")));
        Assert.False(Directory.Exists(Path.Combine(Root, "modules")));
        Assert.False(Directory.Exists(Path.Combine(module, "projects")));
        var opened = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), opened.CreateEditDocument()));
        Assert.True(WorkspaceDocumentModule.sameProjectContent(saved.CreateEditDocument().Modules[0].Projects[0], WorkspaceFileStore.LoadProjectDocument(Path.Combine(module, "search"), Token)));
        var duplicate = WorkspaceDocumentModule.clone(saved.CreateEditDocument()); duplicate.Modules[0].Modules[0].Definition.moduleName = "SEARCH";

        Assert.Contains("Duplicate folder", Assert.Throws<ArgumentException>(() => WorkspaceDocumentModule.validate(duplicate)).Message);
        var header = WorkspaceDocumentModule.moduleFile(WorkspaceDocumentModule.clone(saved.CreateEditDocument()).Modules[0]); header.modules[0].name = "SEARCH";
        Assert.Contains("Duplicate folder", Assert.Throws<ArgumentException>(() => WorkspaceDefinitionCodec.decodeModule(MessagePackSetup.serialize(header))).Message);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(16)]
    public void Old_definitions_are_rejected_without_modifying_saved_files(int version)
    {
        var value = WorkspaceFileStore.CreateDocument(Root, Mixed(), Token).Workspace;
        var path = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var definition = WorkspaceDocumentModule.workspaceFile(value.CreateEditDocument()); definition.schemaVersion = version;
        var bytes = MessagePackSetup.serialize(definition); File.WriteAllBytes(path, bytes);
        Assert.Contains("Unsupported entity definition version", Assert.Throws<ArgumentException>(() => WorkspaceFileStore.LoadWorkspace(Root)).Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Referenced_names_must_match_the_child_definition(bool project)
    {
        WorkspaceFileStore.CreateDocument(Root, Mixed(), Token);
        var parent = project ? Path.Combine(Root, "sorting") : Root;
        var name = project ? "search" : "sorting";
        var folder = Path.Combine(parent, name);
        var kind = project ? "project" : "module";
        var file = WorkspaceDefinitionFiles.Require(folder, kind);
        if (project) {
            var dto = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(file)); dto.projectName = "Different";
            File.WriteAllBytes(file, MessagePackSetup.serialize(dto));
        } else {
            var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(file)); dto.moduleName = "Different";
            File.WriteAllBytes(file, MessagePackSetup.serialize(dto));
        }
        var before = File.ReadAllBytes(file);
        Assert.Contains("does not match its folder", Assert.Throws<InvalidDataException>(() => WorkspaceFileStore.LoadWorkspace(Root)).Message);
        Assert.Throws<InvalidDataException>(() => WorkspaceFileStore.LoadWorkspace(Root));
        Assert.Throws<InvalidDataException>(() => { if (project) WorkspaceFileStore.LoadProjectDocument(folder, Token); else WorkspaceFileStore.LoadModuleDocument(folder, Token); });
        Assert.Equal(before, File.ReadAllBytes(file));
    }
    public void Dispose() => temporary.Delete(true);
}
