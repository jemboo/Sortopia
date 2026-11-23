using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class DocumentImportTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-document-import-");
    private string Library => Path.Combine(temporary.FullName, "Library");
    private string Destination => Path.Combine(temporary.FullName, "Destination");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBindingDto Unfinished() => new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = "unfinished" };
    private static WorkspaceDocument Container(ModuleDocument module) => WorkspaceDocumentModule.withModules(
        WorkspaceFixtures.Workspace(Guid.NewGuid(), "Imported", "Destination", []), [module]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unfinished_module_project_and_run_values_survive_copying_and_reopening(bool setup)
    {
        var runtime = WorkspacePersistenceTests.Sample(setup);
        var document = WorkspaceDocumentModule.clone(runtime);
        var module = document.Modules[0]; var project = module.Projects[0];
        module.Definition.localOverrides.resultsParameterDefaults = [Unfinished()];
        project.Definition.state = projectStateDto.NewDraft(ProjectLocalSettingsDto.fromDomain(runtime.Modules[0].Projects[0].LocalSettings()),
            SettingsDto.fromDomain(runtime.Modules[0].Projects[0].InheritedDefaults()), null!);
        project.Definition.localSettings.resultsParameterDefaults = [Unfinished()];
        project.Definition.localSettings.excludedResultsDefaults = ["ChannelCount"];
        project.Runs[0].resultsParameterSpans[0].values = ["still typing"];
        var child = WorkspaceFixtures.Module(setup ? hierarchyKind.Branch : hierarchyKind.Root, Guid.NewGuid(), "Nested", [], []);
        document.Modules[0] = WorkspaceDocumentModule.withModuleChildren(module, [child], module.Projects);
        var saved = WorkspaceFileStore.CreateDocument(Library, document, Token).Workspace;
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadWorkspace(Library).ResolveRun(project.Runs[0].id));

        var import = WorkspaceImport.PrepareModule(Destination, Path.Combine(Library, "sorting"), Path.Combine(Destination, "sorting"), Token);
        var imported = import.Definition; var copiedProject = imported.Projects[0];
        Assert.NotEqual(module.Definition.id, imported.Definition.id);
        Assert.NotEqual(project.Definition.id, copiedProject.Definition.id);
        Assert.Equal(copiedProject.Definition.id, copiedProject.Runs[0].parentProjectId);
        Assert.Equal(copiedProject.Runs[0].id, WorkspaceDocumentModule.projectFile(copiedProject).runs[0]);
        Assert.Equal(imported.Definition.id, imported.Modules[0].Definition.inheritedFrom.moduleId);
        Assert.Equal(imported.Definition.id, copiedProject.Definition.inheritedFrom.moduleId);
        Assert.Equal(imported.Modules[0].Definition.id, WorkspaceDocumentModule.moduleFile(imported).modules[0].id);
        Assert.Equal("unfinished", imported.Definition.localOverrides.resultsParameterDefaults[0].value);
        Assert.Equal("unfinished", copiedProject.Definition.localSettings.resultsParameterDefaults[0].value);
        Assert.Equal("still typing", copiedProject.Runs[0].resultsParameterSpans[0].values[0]);
        var copied = WorkspaceFileStore.CreateDocument(Destination, Container(imported), Token, [import.Copy]).Workspace;
        var reopened = WorkspaceFileStore.LoadWorkspace(Destination);
        Assert.True(WorkspaceDocumentModule.sameContent(copied.CreateEditDocument(), reopened.CreateEditDocument()));
        Assert.False(reopened.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), WorkspaceFileStore.LoadWorkspace(Library).CreateEditDocument()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sealed_project_import_retains_raw_editing_settings_and_can_resume_them(bool setup)
    {
        var runtime = WorkspacePersistenceTests.Sample(setup);
        var document = WorkspaceDocumentModule.clone(runtime);
        var original = document.Modules[0].Projects[0];
        var local = ProjectLocalSettingsDto.fromDomain(runtime.Modules[0].Projects[0].LocalSettings());
        local.description = null; local.resultsParameterDefaults = [Unfinished()]; local.excludedResultsDefaults = ["ChannelCount"];
        original.Definition.state = projectStateDto.NewSealed(original.Definition.configuration, Microsoft.FSharp.Core.FSharpOption<projectLocalSettingsDto>.Some(local));
        WorkspaceFileStore.CreateDocument(Library, document, Token);

        var source = Path.Combine(Library, "sorting", "search");
        var attached = WorkspaceImport.PrepareProject(Library, source, source, Token);
        Assert.Null(attached.Copy);
        Assert.Equal(original.Definition.id, attached.Definition.Definition.id);
        var import = WorkspaceImport.PrepareProject(Destination, source, Path.Combine(Destination, "sorting", "search"), Token);
        var imported = import.Definition;
        Assert.True(imported.Definition.isSealed);
        Assert.Equal(MessagePackSetup.serialize(original.Definition.configuration), MessagePackSetup.serialize(imported.Definition.configuration));
        Assert.Equal(MessagePackSetup.serialize(local), MessagePackSetup.serialize(imported.Definition.localSettings));
        var module = WorkspaceDocumentModule.withModuleChildren(document.Modules[0], [], [imported]);
        var saved = WorkspaceFileStore.CreateDocument(Destination, Container(module), Token, [import.Copy]).Workspace;
        var resumed = WorkspaceFileStore.ResumeProjectEditing(saved, imported.Definition.id, Token).Workspace;
        var editable = resumed.CreateEditDocument().Modules[0].Projects[0];
        Assert.False(editable.Definition.isSealed);
        Assert.Null(editable.Definition.configuration);
        Assert.Equal(MessagePackSetup.serialize(local), MessagePackSetup.serialize(editable.Definition.localSettings));
        Assert.True(WorkspaceDocumentModule.sameContent(resumed.CreateEditDocument(), WorkspaceFileStore.LoadWorkspace(Destination).CreateEditDocument()));
    }

    [Fact]
    public void An_independent_edit_copy_still_detects_external_edits_to_retained_settings()
    {
        var runtime = WorkspacePersistenceTests.Sample();
        var document = WorkspaceDocumentModule.clone(runtime);
        var definition = document.Modules[0].Projects[0].Definition;
        definition.state = projectStateDto.NewSealed(definition.configuration, Microsoft.FSharp.Core.FSharpOption<projectLocalSettingsDto>.Some(ProjectLocalSettingsDto.fromDomain(runtime.Modules[0].Projects[0].LocalSettings())));
        var saved = WorkspaceFileStore.CreateDocument(Destination, document, Token).Workspace;
        var baseline = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), baseline));
        var changed = WorkspaceDocumentModule.clone(saved.CreateEditDocument());
        changed.Modules[0].Projects[0].Definition.localSettings.resultsParameterDefaults = [Unfinished()];
        Assert.False(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), changed));
        var file = WorkspaceDefinitionFiles.Require(Path.Combine(Destination, "sorting", "search"), "project");
        File.WriteAllBytes(file, MessagePackSetup.serialize(WorkspaceDocumentModule.projectFile(changed.Modules[0].Projects[0])));
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() =>
            WorkspaceFileStore.SaveDocument(WorkspaceDocumentModule.clone(baseline), saved, Token)).Message);
        Assert.Equal("unfinished", WorkspaceFileStore.LoadWorkspace(Destination).CreateEditDocument().Modules[0].Projects[0].Definition.localSettings.resultsParameterDefaults[0].value);
    }
    public void Dispose() => temporary.Delete(true);
}
