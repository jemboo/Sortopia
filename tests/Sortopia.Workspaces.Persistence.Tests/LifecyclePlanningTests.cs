using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class LifecyclePlanningTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-lifecycle-planning-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Dictionary<string, byte[]> Files() => Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
    private void AssertFiles(Dictionary<string, byte[]> expected)
    {
        Assert.Equal(expected.Keys.Order(), Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("remove")]
    [InlineData("reset")]
    public void Lifecycle_changes_keep_the_baseline_and_cache_retryable_after_failure(string operation)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspacePersistenceTests.Sample(), Token).Workspace;
        var original = saved.CreateEditDocument();
        var project = original.Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        var data = Directory.CreateDirectory(Path.Combine(folder, "data", "nested"));
        var cache = Path.Combine(data.FullName, "cache.bin");
        File.WriteAllText(cache, "keep until committed");
        Directory.CreateDirectory(Path.Combine(folder, "runParameters", "empty"));
        var notes = Path.Combine(folder, "notes.txt");
        File.WriteAllText(notes, "keep outside generated folders");
        var before = Files();
        SavedWorkspace Apply(CancellationToken token) => operation switch
        {
            "resume" => WorkspaceFileStore.ResumeProjectEditing(saved, project.Definition.id, token).Workspace,
            "remove" => WorkspaceFileStore.RemoveRunParameterFiles(saved, project.Definition.id, project.Runs[0].id, token),
            _ => WorkspaceFileStore.ResetGeneratedFiles(saved, "project", project.Definition.id, token).Workspace
        };

        using (var locked = new FileStream(cache, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => Apply(Token));
        AssertFiles(before);
        Assert.ThrowsAny<OperationCanceledException>(() => Apply(new CancellationToken(true)));
        AssertFiles(before);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));

        var updated = Apply(Token);
        Assert.False(updated.TryReadProjectState(project.Definition.id).Value.IsSealed);
        Assert.True(saved.TryReadProjectState(project.Definition.id).Value.IsSealed);
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        Assert.Equal(operation != "reset", Directory.Exists(Path.Combine(folder, "runParameters")));
        Assert.True(File.Exists(notes));
        foreach (var (path, bytes) in before.Where(entry => Path.GetDirectoryName(entry.Key) == Path.Combine(folder, "runs")))
            Assert.Equal(bytes, File.ReadAllBytes(path));
        var after = Files();
        Assert.Throws<IOException>(() => Apply(Token)); // The old saved revision is now stale.
        AssertFiles(after);
    }

    [Fact]
    public void A_later_project_planning_failure_does_not_apply_earlier_resumes_or_deletions()
    {
        var document = WorkspacePersistenceTests.Sample();
        var later = WorkspacePersistenceTests.Sample().Modules[0];
        later.Definition.moduleName = "later";
        later.Definition.localOverrides.resultsParameterDefaults =
            [new() { keyName = "ChannelCount", valueType = (int)parameterValueType.Integer, value = "unfinished" }];
        document = WorkspaceDocumentModule.withModules(document, [document.Modules[0], later]);
        var saved = WorkspaceFileStore.CreateDocument(Root, document, Token).Workspace;
        var original = saved.CreateEditDocument();
        foreach (var module in original.Modules)
        {
            var data = Directory.CreateDirectory(Path.Combine(Root, module.Definition.moduleName, "search", "data"));
            File.WriteAllText(Path.Combine(data.FullName, "cache.bin"), "keep on planning failure");
        }
        var before = Files();
        Assert.Contains("invalid inherited settings", Assert.Throws<InvalidOperationException>(() =>
            WorkspaceFileStore.ResetGeneratedFiles(saved, "workspace", document.Definition.id, Token)).Message);
        AssertFiles(before);
        Assert.True(WorkspaceDocumentModule.sameContent(original, saved.CreateEditDocument()));
        Assert.All(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules, module => Assert.True(module.Projects[0].Definition.isSealed));

        var edit = saved.CreateEditDocument();
        edit.Modules[1].Definition.localOverrides.resultsParameterDefaults[0].value = "4";
        saved = WorkspaceFileStore.SaveDocument(edit, saved, Token).Workspace;
        var updated = WorkspaceFileStore.ResetGeneratedFiles(saved, "workspace", document.Definition.id, Token).Workspace;
        Assert.All(updated.CreateEditDocument().Modules, module =>
        {
            Assert.False(module.Projects[0].Definition.isSealed);
            Assert.False(Directory.Exists(Path.Combine(Root, module.Definition.moduleName, "search", "data")));
        });
    }

    public void Dispose() => temporary.Delete(true);
}
