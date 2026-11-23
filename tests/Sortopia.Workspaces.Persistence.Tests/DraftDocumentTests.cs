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
    private SavedWorkspace Create() => WorkspaceFileStore.CreateDocument(Root,
        WorkspacePersistenceTests.Sample(), Token).Workspace;

    [Fact]
    public void Invalid_run_text_is_editable_but_selected_run_resolution_is_strict()
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument());
        edit.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values = ["unfinished"];
        WorkspaceFileStore.SaveDocument(edit, saved, Token);
        var reopened = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.Equal("unfinished", reopened.CreateEditDocument().Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values[0]);
        Assert.ThrowsAny<Exception>(() => reopened.ResolveRun(reopened.CreateEditDocument().Modules[0].Projects[0].Runs[0].id));
    }

    [Fact]
    public void Sealed_configuration_is_protected_and_editing_a_copy_cannot_change_the_saved_baseline()
    {
        var saved = Create(); var original = saved.CreateEditDocument().Modules[0].Projects[0].Definition.configuration.description;
        var edit = saved.CreateEditDocument();
        edit.Modules[0].Projects[0].Definition.configuration.description = "Changed";
        Assert.Equal(original, saved.CreateEditDocument().Modules[0].Projects[0].Definition.configuration.description);
        Assert.Contains("sealed", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(edit, saved, Token)).Message);
        Assert.Equal(original, WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Definition.configuration.description);
    }

    [Fact]
    public void Saved_run_parent_cannot_change_in_an_editable_document()
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument());
        var p = edit.Modules[0].Projects[0];
        p.Definition.id = Guid.NewGuid();

        p.Runs[0].parentProjectId = p.Definition.id;
        Assert.Contains("parent project", Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.SaveDocument(edit, saved, Token)).Message);
        Assert.Equal(saved.CreateEditDocument().Modules[0].Projects[0].Definition.id, WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Definition.id);
    }

    [Fact]
    public void Raw_documents_still_reject_invalid_identities_and_external_edits()
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument());
        edit.Modules[0].Definition.id = Guid.Empty;
        Assert.Throws<ArgumentException>(() => WorkspaceFileStore.SaveDocument(edit, saved, Token));
        var path = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var changed = Clone(saved.CreateEditDocument()); changed.Definition.workspaceName = "External change";
        File.WriteAllBytes(path, MessagePackSetup.serialize(WorkspaceDocumentModule.workspaceFile(changed)));
        Assert.Contains("changed since", Assert.Throws<IOException>(() => WorkspaceFileStore.SaveDocument(Clone(saved.CreateEditDocument()), saved, Token)).Message);
        Assert.Equal("External change", WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Definition.workspaceName);
    }

    [Fact]
    public void Missing_files_can_be_restored_from_the_saved_document()
    {
        var saved = Create(); var path = WorkspaceDefinitionFiles.Require(Path.Combine(Root, "sorting", "search"), "project");
        File.Delete(path);
        WorkspaceFileStore.SaveDocument(Clone(saved.CreateEditDocument()), saved, Token);
        Assert.True(File.Exists(path)); Assert.Single(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Runs);
    }
    public void Dispose() => temporary.Delete(true);
}
