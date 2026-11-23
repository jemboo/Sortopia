using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class GeneratedFilesInspectionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-generated-inspection-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Unreadable_work_locks_its_runs_project_and_ancestors_but_not_siblings()
    {
        var saved = WorkspaceFileStore.CreateDocument(temporary.FullName, WorkspaceNavigationTests.Nested(), Token).Workspace;
        var index = new WorkspaceDocumentIndex(saved.CreateEditDocument());
        var entry = index.Projects[0];
        var folder = Directory.CreateDirectory(Path.Combine(temporary.FullName, entry.RelativeFolder, "runParameters")).FullName;
        var file = Path.Combine(folder, Guid.NewGuid() + ".msgpack");
        File.WriteAllText(file, "unreadable");
        var inspected = WorkspaceFileStore.InspectGeneratedFiles(saved, Token);
        Assert.Equal(new[] { entry.Document.Definition.id }, inspected.LockedProjectIds);
        Assert.Equal(entry.Document.Runs.Select(run => run.id), inspected.LockedRunIds);
        Assert.Equal(entry.ModulePath.Select(module => module.id).Order(), inspected.LockedModuleIds.Order());
        Assert.NotEmpty(inspected.Projects.Single(project => project.ProjectId == entry.Document.Definition.id).ReadError);
        Assert.DoesNotContain(saved.CreateEditDocument().Modules[1].Definition.id, inspected.LockedModuleIds);
        Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.ResumeProjectEditing(saved, entry.Document.Definition.id, Token));
        File.Delete(file);
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        Assert.False(WorkspaceFileStore.HasRunParameterFiles(Path.GetDirectoryName(folder)!, Token));
        var clear = WorkspaceFileStore.InspectGeneratedFiles(saved, Token);
        Assert.Empty(clear.LockedProjectIds); Assert.Empty(clear.LockedModuleIds); Assert.Empty(clear.LockedRunIds);
        // Inspection itself is read-only, including explicit sealed snapshots without files.
        Assert.True(WorkspaceFileStore.LoadWorkspace(temporary.FullName).CreateEditDocument().Modules[0].Modules[0].Projects[0].Definition.isSealed);
        var resumed = WorkspaceFileStore.ResumeProjectEditing(saved, entry.Document.Definition.id, Token).Workspace;
        Assert.True(new WorkspaceDocumentIndex(resumed.CreateEditDocument()).FindProject(entry.Document.Definition.id).Document.Definition.state.IsDraft);
    }

    [Fact]
    public void Inspection_honors_cancellation_without_modifying_definitions()
    {
        var saved = WorkspaceFileStore.CreateDocument(temporary.FullName, WorkspaceNavigationTests.Nested(), Token).Workspace;
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.InspectGeneratedFiles(saved, cancelled.Token));
        Assert.True(WorkspaceDocumentModule.sameContent(saved.CreateEditDocument(), WorkspaceFileStore.LoadWorkspace(temporary.FullName).CreateEditDocument()));
    }

    public void Dispose() => temporary.Delete(true);
}
