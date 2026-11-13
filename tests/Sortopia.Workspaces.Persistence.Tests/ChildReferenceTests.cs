using Microsoft.FSharp.Collections;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class ChildReferenceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-child-references-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBindingDto Binding(int value) => new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = value.ToString() };
    private static int Channel(IEnumerable<parameterBinding> bindings) => int.Parse(bindings.Single(value => value.KeyName == "ChannelCount").FormattedValue);
    private static Dictionary<string, byte[]> Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private string ModuleFolder => Path.Combine(Root, "sorting");
    private string ProjectFolder => Path.Combine(ModuleFolder, "search");

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_file_owns_its_fields_and_only_references_children(bool branch)
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, WorkspacePersistenceTests.Sample(branch), Token).Definition;
        var w = WorkspaceDefinitionCodec.decodeWorkspace(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace")));
        var m = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ModuleFolder, "module")));
        var p = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ProjectFolder, "project")));
        Assert.Equal(16, w.schemaVersion);
        Assert.Equal(saved.Modules[0].Id, Assert.Single(w.modules).id);
        Assert.Equal(saved.Modules[0].Projects[0].Id, Assert.Single(m.projects).id);
        Assert.Equal(saved.Modules[0].Projects[0].Runs[0].Id, Assert.Single(p.runs));
        Assert.Equal(new[] { "id", "folderName" }, typeof(childFolderReferenceDto).GetProperties().Select(property => property.Name));
        Assert.Null(typeof(moduleDefinitionDto).GetProperty("configuration"));
        Assert.NotNull(p.configuration);
        Assert.Null(p.inheritedDefaults);
        Assert.Null(p.localOverrides);

        var before = Files(Root);
        var edited = WorkspaceDto.fromDomain(saved);
        edited.modules[0].projects[0].runs[0].descriptionOverride = "Run edit only";
        WorkspaceFileStore.SaveWorkspace(Root, WorkspaceDto.toDomain(edited), saved, Token);
        var changed = before.Keys.Where(path => !before[path].SequenceEqual(File.ReadAllBytes(path))).ToArray();
        Assert.Single(changed);
        Assert.Equal(Path.Combine(ProjectFolder, "runs", p.runs[0] + ".msgpack"), changed[0]);
        Assert.Equal("Run edit only", WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0].Description);
        var portable = Path.Combine(temporary.FullName, "Portable project");
        Copy(ProjectFolder, portable);
        Directory.Delete(Root, true);
        var detached = WorkspaceFileStore.LoadProjectFolder(portable, Token);
        Assert.Equal(saved.Modules[0].Projects[0].Configuration, detached.Configuration);
        Assert.Equal("Run edit only", detached.Runs[0].Description);
    }

    private workspace Inherited(bool branch)
    {
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var top = ModuleOverridesDto.toDomain(family, new moduleOverridesDto {
            resultsFunctionName = "sortable-tests.standard", resultsParameterDefaults = [Binding(16), new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }],
            setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : null,
            setupParameterDefaults = branch ? [Binding(16), new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }] : null });
        var parent = workspaceModule.createInherited(Guid.NewGuid(), "Parent", "parent", ModuleDefaults.configuration(branch), top, [], []);
        var child = workspaceModule.createInherited(Guid.NewGuid(), "Child", "child", parent.Configuration,
            ModuleOverridesDto.toDomain(family, new moduleOverridesDto { resultsParameterDefaults = [Binding(32)], setupParameterDefaults = branch ? [Binding(32)] : null }), [], []);
        var p = project.createInherited(Guid.NewGuid(), "Draft", "draft", ModuleDefaults.toProject(child.Configuration),
            OverridesDto.toDomain(family, new projectOverridesDto { resultsDatabaseName = "Own results", setupDatabaseName = branch ? "Own setup" : null,
                resultsParameterDefaults = [Binding(64)], setupParameterDefaults = branch ? [Binding(64)] : null }), []);
        child = workspaceModule.createInherited(child.Id, child.ModuleName, child.FolderName, child.InheritedDefaults, child.LocalOverrides, [], [p]);
        parent = workspaceModule.createInherited(parent.Id, parent.ModuleName, parent.FolderName, parent.InheritedDefaults, parent.LocalOverrides, [child], []);
        return workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [parent]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detached_folders_keep_parent_defaults_separate_from_local_overrides(bool branch)
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Inherited(branch), Token).Definition;
        var source = Path.Combine(Root, "parent", "child");
        var moduleCopy = Path.Combine(temporary.FullName, "Detached module");
        var projectCopy = Path.Combine(temporary.FullName, "Detached project");
        Copy(source, moduleCopy);
        Copy(Path.Combine(source, "draft"), projectCopy);
        Directory.Delete(Root, true); // Only this test's generated workspace.
        var m = WorkspaceFileStore.LoadModuleFolder(moduleCopy, Token);
        var p = WorkspaceFileStore.LoadProjectFolder(projectCopy, Token);
        Assert.Equal(32, Channel(m.ResultsParameterDefaults));
        Assert.Equal(64, Channel(p.ResultsParameterDefaults));
        Assert.Equal("Own results", p.ResultsDatabaseName);
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var withoutLocal = workspaceModule.createInherited(m.Id, m.ModuleName, m.FolderName, m.InheritedDefaults,
            ModuleOverridesDto.toDomain(family, new moduleOverridesDto()), m.Modules, m.Projects);
        Assert.Equal(16, Channel(withoutLocal.ResultsParameterDefaults));
        var draft = project.createInherited(p.Id, p.ProjectName, p.FolderName, p.InheritedDefaults,
            OverridesDto.toDomain(family, new projectOverridesDto { resultsDatabaseName = "Own results", setupDatabaseName = branch ? "Own setup" : null }), []);
        Assert.Equal(32, Channel(draft.ResultsParameterDefaults));
        Assert.Equal("sortable-tests.standard", draft.ResultsFunctionName);
        if (branch)
        {
            Assert.Equal(16, Channel(((workspaceModule.Branch)withoutLocal).Item.SetupParameterDefaults));
            Assert.Equal(32, Channel(((project.BranchProject)draft).Item.SetupParameterDefaults));
        }
        var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(moduleCopy, "module")));
        Assert.Equal(saved.Modules[0].Id, dto.inheritedFrom.moduleId);
    }

    [Fact]
    public void Captured_parent_values_refresh_even_when_a_local_override_hides_the_change()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Inherited(false), Token).Definition;
        var dto = WorkspaceDto.fromDomain(saved);
        dto.modules[0].localOverrides.resultsParameterDefaults.Single(binding => binding.keyName == "ChannelCount").value = "8";
        WorkspaceFileStore.SaveWorkspace(Root, WorkspaceDto.toDomain(dto), saved, Token);
        var folder = Path.Combine(Root, "parent", "child");
        var detached = WorkspaceFileStore.LoadModuleFolder(folder, Token);
        Assert.Equal(32, Channel(detached.ResultsParameterDefaults));
        Assert.Equal(8, Channel(((moduleConfiguration.RootModuleConfig)detached.InheritedDefaults).Item.ResultsParameterDefaults));
    }

    [Fact]
    public void Unlisted_runs_and_folders_stay_on_disk_without_rejoining_the_hierarchy()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, WorkspacePersistenceTests.Sample(), Token).Definition;
        var oldRun = saved.Modules[0].Projects[0].Runs[0].Id;
        var dto = WorkspaceDto.fromDomain(saved);
        dto.modules[0].projects[0].runs = [];
        WorkspaceFileStore.SaveWorkspace(Root, WorkspaceDto.toDomain(dto), saved, Token);
        var extra = Path.Combine(ModuleFolder, "unlisted");
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, Guid.NewGuid() + ".msgpack"), "not a definition");
        Assert.Empty(WorkspaceFileStore.LoadProjectFolder(ProjectFolder, Token).Runs);
        Assert.Single(WorkspaceFileStore.LoadModuleFolder(ModuleFolder, Token).Projects);
        Assert.Empty(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs);
        Assert.True(File.Exists(Path.Combine(ProjectFolder, "runs", oldRun + ".msgpack")));
        var loaded = WorkspaceFileStore.LoadDefinition(Root);
        WorkspaceFileStore.SaveWorkspace(Root, loaded, loaded, Token);
        Assert.Single(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects);
    }

    [Theory]
    [InlineData("missing-child")]
    [InlineData("wrong-id")]
    [InlineData("escape")]
    [InlineData("duplicate")]
    [InlineData("missing-run")]
    public void Broken_references_fail_explicitly_without_modifying_files(string problem)
    {
        WorkspaceFileStore.CreateWorkspace(Root, WorkspacePersistenceTests.Sample(), Token);
        var path = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var dto = WorkspaceDefinitionCodec.decodeWorkspace(File.ReadAllBytes(path));
        switch (problem)
        {
            case "missing-child": dto.modules[0].folderName = "missing"; break;
            case "wrong-id": dto.modules[0].id = Guid.NewGuid(); break;
            case "escape": dto.modules[0].folderName = "../sorting"; break;
            case "duplicate": dto.modules = [dto.modules[0], dto.modules[0]]; break;
            case "missing-run": File.Delete(Directory.GetFiles(Path.Combine(ProjectFolder, "runs")).Single()); break;
        }
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
        var before = Files(Root);
        Assert.NotNull(Record.Exception(() => WorkspaceFileStore.LoadDefinition(Root)));
        foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void Edits_to_child_context_are_detected_even_when_live_parent_values_hide_them()
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, Inherited(false), Token).Definition;
        var path = WorkspaceDefinitionFiles.Require(Path.Combine(Root, "parent", "child"), "module");
        var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(path));
        dto.inheritedDefaults.description = "External change";
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveWorkspace(Root, saved, saved, Token)).Message);
    }

    [Theory]
    [InlineData("module")]
    [InlineData("project")]
    public void Replaced_child_definitions_are_not_mistaken_for_missing_files_to_restore(string kind)
    {
        var saved = WorkspaceFileStore.CreateWorkspace(Root, WorkspacePersistenceTests.Sample(), Token).Definition;
        var folder = kind == "module" ? ModuleFolder : ProjectFolder;
        var original = WorkspaceDefinitionFiles.Require(folder, kind);
        var replacement = Guid.NewGuid();
        byte[] bytes;
        if (kind == "module")
        {
            var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(original));
            dto.id = replacement;
            bytes = MessagePackSetup.serialize(dto);
        }
        else
        {
            var dto = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(original));
            dto.id = replacement;
            bytes = MessagePackSetup.serialize(dto);
        }
        File.Delete(original);
        var path = Path.Combine(folder, replacement + ".msgpack");
        File.WriteAllBytes(path, bytes);
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveWorkspace(Root, saved, saved, Token)).Message);
        Assert.False(File.Exists(original));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    public void Dispose() => temporary.Delete(true);
}
