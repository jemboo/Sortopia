using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class SavedWorkspaceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-saved-workspace-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Created_and_loaded_handles_protect_the_baseline_and_advance_only_after_saving(bool reload)
    {
        var input = WorkspacePersistenceTests.Sample();
        var created = WorkspaceFileStore.CreateDocument(Root, input, Token).Workspace;
        var saved = reload ? WorkspaceFileStore.LoadWorkspace(Root) : created;
        var original = saved.CreateEditDocument();
        input.Definition.workspaceName = "Changed input object";
        Assert.Equal(Path.GetFullPath(Root), saved.RootFolder);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));
        var edit = saved.CreateEditDocument();
        edit.Definition.workspaceName = "First revision";
        edit.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values = ["still typing"];
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));

        var updated = WorkspaceFileStore.SaveDocument(WorkspaceDocumentModule.clone(edit), saved, Token).Workspace;
        Assert.NotSame(saved, updated);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));
        Assert.Equal("First revision", updated.CreateEditDocument().Definition.workspaceName);
        Assert.Equal("still typing", updated.CreateEditDocument().Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values[0]);
        edit.Definition.workspaceName = "Changed after saving";
        Assert.Equal("First revision", updated.CreateEditDocument().Definition.workspaceName);
        Assert.Contains("changed since it was loaded", Assert.Throws<IOException>(() =>
            WorkspaceFileStore.SaveDocument(original, saved, Token)).Message);

        var next = updated.CreateEditDocument();
        next.Definition.workspaceName = "Second revision";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.SaveDocument(next, updated, cancellation.Token));
        Assert.Equal("First revision", WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Definition.workspaceName);
        var final = WorkspaceFileStore.SaveDocument(next, updated, Token).Workspace;
        Assert.Equal("Second revision", final.CreateEditDocument().Definition.workspaceName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generation_uses_the_saved_revision_and_returns_a_new_handle_with_retained_editing_settings(bool branch)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(branch), Token).Workspace;
        var projectId = saved.CreateEditDocument().Modules[0].Projects[0].Definition.id;
        saved = WorkspaceFileStore.ResumeProjectEditing(saved, projectId, Token).Workspace;
        var original = saved.CreateEditDocument();
        var editing = MessagePackSetup.serialize(original.Modules[0].Projects[0].Definition.localSettings);
        var discarded = saved.CreateEditDocument();
        discarded.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values = ["unfinished edit"];

        var generated = WorkspaceRunGeneration.generate(saved, original.Modules[0].Projects[0].Runs[0].id, null!, Token);

        Assert.Equal(runExecutionStatus.Completed, generated.Report.Execution.Status);
        Assert.NotSame(saved, generated.Workspace);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));
        var sealedProject = generated.Workspace.CreateEditDocument().Modules[0].Projects[0];
        Assert.True(sealedProject.Definition.isSealed);
        Assert.Equal(editing, MessagePackSetup.serialize(sealedProject.Definition.localSettings));
        Assert.Equal(original.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values, sealedProject.Runs[0].resultsParameterSpans[0].values);
        Assert.Contains("generated", WorkspaceFileStore.ResumeProjectEditingReason(generated.Workspace, projectId));

        var resumed = WorkspaceFileStore.RemoveRunParameterFiles(generated.Workspace, projectId, sealedProject.Runs[0].id, Token);
        Assert.False(resumed.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.True(generated.Workspace.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal(editing, MessagePackSetup.serialize(resumed.CreateEditDocument().Modules[0].Projects[0].Definition.localSettings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Focused_reads_are_independent_and_leave_unfinished_siblings_unresolved(bool setup)
    {
        var input = WorkspacePersistenceTests.Sample(setup);
        var project = input.Modules[0].Projects[0];
        var selectedId = project.Runs[0].id;
        var unfinished = WorkspaceDocumentModule.cloneProject(project).Runs[0];
        unfinished.id = Guid.NewGuid(); unfinished.runName = "Unfinished";
        unfinished.resultsParameterSpans[0].values = ["still typing"];
        unfinished.sgd = Microsoft.FSharp.Core.FSharpOption<sgdRunConfig>.Some(new(2, 3));
        input = WorkspaceNavigation.replaceProject(WorkspaceDocumentModule.withRuns(project, [.. project.Runs, unfinished]), input);
        var saved = WorkspaceFileStore.CreateDocument(Root, input, Token).Workspace;
        var original = saved.CreateEditDocument();

        var state = saved.TryReadProjectState(project.Definition.id).Value;
        Assert.True(state.IsSealed);
        ProjectLifecycle.configuration(state).resultsDataTypes[0].queryParameterKeys[0] = "Changed copy";
        var controls = saved.TryReadRunSnapshotControls(unfinished.id).Value;
        controls.SnapshotInterval = 999;
        Assert.Equal(2, saved.TryReadRunSnapshotControls(unfinished.id).Value.SnapshotInterval);
        Assert.Equal(selectedId, saved.ResolveRun(selectedId).Run.Id);
        Assert.Equal(selectedId, saved.ResolveRun(project.Definition.id, selectedId).Run.Id);
        Assert.ThrowsAny<ArgumentException>(() => saved.ResolveRun(unfinished.id));
        Assert.Throws<InvalidOperationException>(() => saved.ResolveRun(Guid.NewGuid(), selectedId));
        Assert.Null(saved.TryReadProjectState(Guid.NewGuid()));
        Assert.Null(saved.TryReadRunSnapshotControls(Guid.NewGuid()));
        Assert.Null(saved.TryReadRunSnapshotControls(selectedId));
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));

        var draft = WorkspaceFileStore.ResumeProjectEditing(saved, project.Definition.id, Token).Workspace;
        var edit = draft.CreateEditDocument();
        edit.Modules[0].Projects[0].Definition.localSettings.resultsParameterDefaults =
            [new() { keyName = "Unfinished", valueType = (int)parameterValueType.Integer, value = "still typing" }];
        draft = WorkspaceFileStore.SaveDocument(edit, draft, Token).Workspace;
        var draftState = draft.TryReadProjectState(project.Definition.id).Value;
        Assert.True(draftState.IsDraft);
        ProjectLifecycle.localSettings(draftState).resultsParameterDefaults[0].value = "Changed copy";
        Assert.Equal("still typing", ProjectLifecycle.localSettings(draft.TryReadProjectState(project.Definition.id).Value).resultsParameterDefaults[0].value);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));
    }

    public void Dispose() => temporary.Delete(true);
}
