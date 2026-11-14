using Sortopia.Core.Serialization;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class DraftDocumentTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-draft-documents-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static WorkspaceDocument Clone(WorkspaceDocument value) => MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(value));
    private WorkspaceDocument Create() => WorkspaceFileStore.CreateDocument(Root,
        WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample()), Token).Definition;

    [Fact]
    public void Invalid_run_text_is_editable_but_runtime_loading_is_strict()
    {
        var saved = Create(); var edit = Clone(saved);
        edit.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values = ["unfinished"];
        WorkspaceFileStore.SaveDocument(Root, edit, saved, Token);
        var reopened = WorkspaceFileStore.LoadDocument(Root);
        Assert.Equal("unfinished", reopened.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values[0]);
        Assert.ThrowsAny<Exception>(() => WorkspaceFileStore.LoadDefinition(Root));
        Assert.ThrowsAny<Exception>(() => WorkspaceDocumentModule.toDomain(reopened));
    }

    [Fact]
    public void Sealed_configuration_is_protected_even_if_the_caller_edits_the_baseline_DTO()
    {
        var saved = Create(); var original = saved.Modules[0].Projects[0].Definition.configuration.description;
        saved.Modules[0].Projects[0].Definition.configuration.description = "Changed";
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(Root, saved, saved, Token)).Message);
        Assert.Equal(original, WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Definition.configuration.description);
    }

    [Fact]
    public void Saved_run_parent_cannot_change_in_an_editable_document()
    {
        var saved = Create(); var edit = Clone(saved);
        var p = edit.Modules[0].Projects[0];
        p.Definition.id = Guid.NewGuid();
        edit.Modules[0].Definition.projects[0].id = p.Definition.id;
        p.Runs[0].parentProjectId = p.Definition.id;
        Assert.Contains("parent project", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(Root, edit, saved, Token)).Message);
        Assert.Equal(saved.Modules[0].Projects[0].Definition.id, WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Definition.id);
    }

    [Fact]
    public void Raw_documents_still_reject_mismatched_references_and_external_edits()
    {
        var saved = Create(); var edit = Clone(saved);
        edit.Definition.modules[0].id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => WorkspaceFileStore.SaveDocument(Root, edit, saved, Token));
        var path = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var changed = Clone(saved); changed.Definition.workspaceName = "External change";
        File.WriteAllBytes(path, MessagePackSetup.serialize(changed.Definition));
        Assert.Contains("changed since", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveDocument(Root, Clone(saved), saved, Token)).Message);
        Assert.Equal("External change", WorkspaceFileStore.LoadDocument(Root).Definition.workspaceName);
    }

    [Fact]
    public void Missing_files_can_be_restored_from_the_saved_document()
    {
        var saved = Create(); var path = WorkspaceDefinitionFiles.Require(Path.Combine(Root, "sorting", "search"), "project");
        File.Delete(path);
        WorkspaceFileStore.SaveDocument(Root, Clone(saved), saved, Token);
        Assert.True(File.Exists(path)); Assert.Single(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs);
    }
    public void Dispose() => temporary.Delete(true);
}
