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
    public void Document_children_alone_determine_saved_references_names_and_order(bool branch)
    {
        var document = WorkspacePersistenceTests.Sample(branch);
        var module = document.Modules[0];
        var project = module.Projects[0];
        module.Definition.moduleName = "Renamed module";
        project.Definition.projectName = "Renamed project";
        var unfinished = WorkspaceDocumentModule.cloneProject(project).Runs[0];
        unfinished.id = Guid.NewGuid(); unfinished.runName = "Unfinished";
        unfinished.resultsParameterSpans[0].values = ["still typing"];
        project = new(project.Definition, [unfinished, project.Runs[0]]);
        var otherProject = WorkspaceDocumentModule.cloneProject(project);
        otherProject.Definition.id = Guid.NewGuid(); otherProject.Definition.projectName = "Other project";
        otherProject = new(otherProject.Definition, []);
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var nested = WorkspaceFixtures.Module(family, Guid.NewGuid(), "Nested", [], []);
        var otherModule = WorkspaceFixtures.Module(family, Guid.NewGuid(), "Other module", [], []);
        module = new(module.Definition, [nested], [otherProject, project]);
        document = new(document.Definition, [otherModule, module]);

        var saved = WorkspaceFileStore.CreateDocument(Root, document, Token).Workspace;
        var moduleFolder = Path.Combine(Root, "Renamed module");
        var projectFolder = Path.Combine(moduleFolder, "Renamed project");
        var workspaceFile = WorkspaceDefinitionCodec.decodeWorkspace(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace")));
        var moduleFile = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(moduleFolder, "module")));
        var projectFile = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(projectFolder, "project")));
        Assert.Equal(new[] { "Other module", "Renamed module" }, workspaceFile.modules.Select(child => child.name));
        Assert.Equal(nested.Definition.id, Assert.Single(moduleFile.modules).id);
        Assert.Equal(new[] { "Other project", "Renamed project" }, moduleFile.projects.Select(child => child.name));
        Assert.Equal(project.Runs.Select(run => run.id), projectFile.runs);
        var reopened = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), reopened.CreateEditDocument()));
        Assert.Equal("still typing", reopened.CreateEditDocument().Modules[1].Projects[1].Runs[0].resultsParameterSpans[0].values[0]);

        var edit = WorkspaceDocumentModule.clone(reopened.CreateEditDocument());
        var editedProject = edit.Modules[1].Projects[1];
        editedProject = new(editedProject.Definition, [editedProject.Runs[1]]);
        var editedModule = new ModuleDocument(edit.Modules[1].Definition, [], [editedProject, edit.Modules[1].Projects[0]]);
        edit = new(edit.Definition, [editedModule, edit.Modules[0]]);
        var result = WorkspaceFileStore.SaveDocument(edit, reopened, Token);
        Assert.Empty(result.CleanupWarnings);
        reopened = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.Equal(new[] { "Renamed module", "Other module" }, reopened.CreateEditDocument().Modules.Select(child => child.Definition.moduleName));
        Assert.Equal(new[] { "Renamed project", "Other project" }, reopened.CreateEditDocument().Modules[0].Projects.Select(child => child.Definition.projectName));
        Assert.Equal(project.Runs[1].id, Assert.Single(reopened.CreateEditDocument().Modules[0].Projects[0].Runs).id);
        Assert.Empty(reopened.CreateEditDocument().Modules[0].Modules);
        Assert.False(Directory.Exists(Path.Combine(moduleFolder, "Nested")));
        Assert.False(File.Exists(Path.Combine(projectFolder, "runs", WorkspaceDefinitionFiles.Name(unfinished.id))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Each_file_owns_its_fields_and_only_references_children(bool branch)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(branch), Token).Workspace;
        var w = WorkspaceDefinitionCodec.decodeWorkspace(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(Root, "workspace")));
        var m = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ModuleFolder, "module")));
        var p = WorkspaceDefinitionCodec.decodeProject(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ProjectFolder, "project")));
        Assert.Equal(WorkspaceDefinitionCodec.schemaVersion, w.schemaVersion);
        Assert.Equal(saved.CreateEditDocument().Modules[0].Definition.id, Assert.Single(w.modules).id);
        Assert.Equal(saved.CreateEditDocument().Modules[0].Projects[0].Definition.id, Assert.Single(m.projects).id);
        Assert.Equal(saved.CreateEditDocument().Modules[0].Projects[0].Runs[0].id, Assert.Single(p.runs));
        Assert.Equal(new[] { "id", "name" }, typeof(childReferenceDto).GetProperties().Select(property => property.Name));
        foreach (var type in new[] { typeof(ModuleDocument), typeof(ProjectDocument) })
            Assert.Null(type.GetProperty("FolderName"));
        foreach (var type in new[] { typeof(moduleDefinitionDto), typeof(projectDefinitionDto) })
            Assert.Null(type.GetProperty("folderName"));
        Assert.DoesNotContain("folderName", MessagePack.MessagePackSerializer.ConvertToJson(MessagePackSetup.serialize(m), MessagePackSetup.options, Token));
        Assert.DoesNotContain("folderName", MessagePack.MessagePackSerializer.ConvertToJson(MessagePackSetup.serialize(p), MessagePackSetup.options, Token));
        Assert.Null(typeof(moduleDefinitionDto).GetProperty("configuration"));
        Assert.NotNull(p.configuration);
        Assert.Null(p.inheritedDefaults);
        Assert.Null(p.localSettings);

        var before = Files(Root);
        var edited = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        edited.Modules[0].Projects[0].Runs[0].descriptionOverride = "Run edit only";
        WorkspaceFileStore.SaveDocument(edited, saved, Token);
        var changed = before.Keys.Where(path => !before[path].SequenceEqual(File.ReadAllBytes(path))).ToArray();
        Assert.Single(changed);
        Assert.Equal(Path.Combine(ProjectFolder, "runs", p.runs[0] + ".msgpack"), changed[0]);
        Assert.Equal("Run edit only", WorkspaceFileStore.LoadWorkspace(Root).ResolveRun(p.runs[0]).Run.Description);
        var portable = Path.Combine(temporary.FullName, "Portable", "search");
        Copy(ProjectFolder, portable);
        Directory.Delete(Root, true);
        var detached = WorkspaceFileStore.LoadProjectDocument(portable, Token);
        Assert.Equal(SettingsDto.toDomain(detached.Family(), saved.CreateEditDocument().Modules[0].Projects[0].Definition.configuration), detached.Configuration());
        Assert.Equal("Run edit only", detached.ResolvedRuns()[0].Description);
    }

    private WorkspaceDocument Inherited(bool branch)
    {
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var top = ModuleOverridesDto.toDomain(family, new moduleOverridesDto {
            resultsFunctionName = "sortable-tests.standard", resultsParameterDefaults = [Binding(16), new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }],
            setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : null,
            setupParameterDefaults = branch ? [Binding(16), new() { keyName = "SortableDataFormat", valueType = (int)parameterValueType.Text, value = "Bools" }] : null });
        var parent = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "parent", ModuleDefaults.configuration(branch), top, [], []);
        var child = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "child", parent.Configuration(),
            ModuleOverridesDto.toDomain(family, new moduleOverridesDto { resultsParameterDefaults = [Binding(32)], setupParameterDefaults = branch ? [Binding(32)] : null }), [], []);
        var p = WorkspaceFixtures.DraftProject(Guid.NewGuid(), "draft", ModuleDefaults.toProject(child.Configuration()),
            ProjectLocalSettingsDto.toDomain(ModuleDefaults.toProject(child.Configuration()), new projectLocalSettingsDto { resultsDatabaseName = "Own results", setupDatabaseName = branch ? "Own setup" : null,
                resultsParameterDefaults = [Binding(64)], setupParameterDefaults = branch ? [Binding(64)] : null }), []);
        child = WorkspaceFixtures.InheritedModule(child.Definition.id, child.Definition.moduleName, child.InheritedDefaults(), child.LocalOverrides(), [], [p]);
        parent = WorkspaceFixtures.InheritedModule(parent.Definition.id, parent.Definition.moduleName, parent.InheritedDefaults(), parent.LocalOverrides(), [child], []);
        return WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [parent]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Detached_folders_keep_parent_defaults_separate_from_local_overrides(bool branch)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Inherited(branch), Token).Workspace;
        var source = Path.Combine(Root, "parent", "child");
        var moduleCopy = Path.Combine(temporary.FullName, "Detached", "child");
        var projectCopy = Path.Combine(temporary.FullName, "Detached", "draft");
        Copy(source, moduleCopy);
        Copy(Path.Combine(source, "draft"), projectCopy);
        Directory.Delete(Root, true); // Only this test's generated workspace.
        var m = WorkspaceFileStore.LoadModuleDocument(moduleCopy, Token);
        var p = WorkspaceFileStore.LoadProjectDocument(projectCopy, Token);
        Assert.Equal(32, Channel(m.Configuration().ResultsParameterDefaults));
        Assert.Equal(64, Channel(p.Configuration().ResultsParameterDefaults));
        Assert.Equal("Own results", p.Configuration().ResultsDatabaseName);
        var family = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var withoutLocal = WorkspaceFixtures.InheritedModule(m.Definition.id, m.Definition.moduleName, m.InheritedDefaults(),
            ModuleOverridesDto.toDomain(family, new moduleOverridesDto()), m.Modules, m.Projects);
        Assert.Equal(16, Channel(withoutLocal.Configuration().ResultsParameterDefaults));
        var draft = WorkspaceFixtures.DraftProject(p.Definition.id, p.Definition.projectName, p.InheritedDefaults(),
            ProjectLocalSettingsDto.toDomain(p.InheritedDefaults(), new projectLocalSettingsDto { resultsDatabaseName = "Own results", setupDatabaseName = branch ? "Own setup" : null }), []);
        Assert.Equal(32, Channel(draft.Configuration().ResultsParameterDefaults));
        Assert.Equal("sortable-tests.standard", draft.Configuration().ResultsFunctionName);
        if (branch)
        {
            Assert.Equal(16, Channel(withoutLocal.Configuration().Setup.Value.ParameterDefaults));
            Assert.Equal(32, Channel(draft.Configuration().Setup.Value.ParameterDefaults));
        }
        var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(WorkspaceDefinitionFiles.Require(moduleCopy, "module")));
        Assert.Equal(saved.CreateEditDocument().Modules[0].Definition.id, dto.inheritedFrom.moduleId);
    }

    [Fact]
    public void Captured_parent_values_refresh_even_when_a_local_override_hides_the_change()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Inherited(false), Token).Workspace;
        var dto = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        dto.Modules[0].Definition.localOverrides.resultsParameterDefaults.Single(binding => binding.keyName == "ChannelCount").value = "8";
        // Document editors save captured parent context along with the local edit.
        dto.Modules[0].Modules[0].Definition.inheritedDefaults = ModuleSettingsDto.fromDomain(
            WorkspaceDefinitionResolution.moduleConfiguration(null!, dto.Modules[0].Definition));
        WorkspaceFileStore.SaveDocument(dto, saved, Token);
        var folder = Path.Combine(Root, "parent", "child");
        var detached = WorkspaceFileStore.LoadModuleDocument(folder, Token);
        Assert.Equal(32, Channel(detached.Configuration().ResultsParameterDefaults));
        Assert.Equal(8, Channel(detached.InheritedDefaults().ResultsParameterDefaults));
    }

    [Fact]
    public void Unlisted_runs_and_folders_stay_on_disk_without_rejoining_the_hierarchy()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(), Token).Workspace;
        var oldRun = saved.CreateEditDocument().Modules[0].Projects[0].Runs[0].id;
        var unlisted = Guid.NewGuid();
        var unlistedDto = WorkspaceDocumentModule.clone(saved.CreateEditDocument()).Modules[0].Projects[0].Runs[0];
        unlistedDto.id = unlisted;
        File.WriteAllBytes(Path.Combine(ProjectFolder, "runs", unlisted + ".msgpack"), MessagePackSetup.serialize(unlistedDto));
        var dto = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        dto.Modules[0].Projects[0] = WorkspaceDocumentModule.withRuns(dto.Modules[0].Projects[0], []);
        WorkspaceFileStore.SaveDocument(dto, saved, Token);
        var extra = Path.Combine(ModuleFolder, "unlisted");
        Directory.CreateDirectory(extra);
        File.WriteAllText(Path.Combine(extra, Guid.NewGuid() + ".msgpack"), "not a definition");
        Assert.Empty(WorkspaceFileStore.LoadProjectDocument(ProjectFolder, Token).Runs);
        Assert.Single(WorkspaceFileStore.LoadModuleDocument(ModuleFolder, Token).Projects);
        Assert.Empty(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Runs);
        Assert.False(File.Exists(Path.Combine(ProjectFolder, "runs", oldRun + ".msgpack")));
        Assert.True(File.Exists(Path.Combine(ProjectFolder, "runs", unlisted + ".msgpack")));
        var loaded = WorkspaceFileStore.LoadWorkspace(Root);
        WorkspaceFileStore.SaveDocument(loaded.CreateEditDocument(), loaded, Token);
        Assert.Single(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects);
    }

    [Theory]
    [InlineData("missing-child")]
    [InlineData("wrong-id")]
    [InlineData("escape")]
    [InlineData("duplicate")]
    [InlineData("missing-run")]
    public void Broken_references_fail_explicitly_without_modifying_files(string problem)
    {
        WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(), Token);
        var path = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var dto = WorkspaceDefinitionCodec.decodeWorkspace(File.ReadAllBytes(path));
        switch (problem)
        {
            case "missing-child": dto.modules[0].name = "missing"; break;
            case "wrong-id": dto.modules[0].id = Guid.NewGuid(); break;
            case "escape": dto.modules[0].name = "../sorting"; break;
            case "duplicate": dto.modules = [dto.modules[0], dto.modules[0]]; break;
            case "missing-run": File.Delete(Directory.GetFiles(Path.Combine(ProjectFolder, "runs")).Single()); break;
        }
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
        var before = Files(Root);
        Assert.NotNull(Record.Exception(() => WorkspaceFileStore.LoadWorkspace(Root)));
        foreach (var (file, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void Edits_to_child_context_are_detected_even_when_live_parent_values_hide_them()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, Inherited(false), Token).Workspace;
        var baseline = WorkspaceFileStore.LoadWorkspace(Root);
        var path = WorkspaceDefinitionFiles.Require(Path.Combine(Root, "parent", "child"), "module");
        var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(path));
        dto.inheritedDefaults.description = "External change";
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveDocument(WorkspaceDocumentModule.clone(baseline.CreateEditDocument()), baseline, Token)).Message);
    }

    [Theory]
    [InlineData("module")]
    [InlineData("project")]
    public void Replaced_child_definitions_are_not_mistaken_for_missing_files_to_restore(string kind)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(), Token).Workspace;
        var baseline = WorkspaceFileStore.LoadWorkspace(Root);
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
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveDocument(WorkspaceDocumentModule.clone(baseline.CreateEditDocument()), baseline, Token)).Message);
        Assert.False(File.Exists(original));
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    public void Dispose() => temporary.Delete(true);
}
