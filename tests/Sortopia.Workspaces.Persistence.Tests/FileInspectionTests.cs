using System.Text.Json;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class FileInspectionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-inspection-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspector_reads_saved_fields_and_child_references_without_loading_the_hierarchy(bool branch)
    {
        var root = Path.Combine(temporary.FullName, "Workspace");
        var saved = WorkspaceFileStore.CreateWorkspace(root, WorkspacePersistenceTests.Sample(branch), Token).Definition;
        var workspaceFile = WorkspaceDefinitionFiles.Require(root, "workspace");
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var top = WorkspaceFileInspection.inspectConfiguration(root, workspaceFile, Token);
        Assert.Equal("Workspace definition", top.Kind);
        var moduleRef = Assert.Single(top.References);
        Assert.True(moduleRef.Exists);
        var module = WorkspaceFileInspection.inspectConfiguration(root, moduleRef.FullPath, Token);
        using var moduleJson = JsonDocument.Parse(module.Json);
        Assert.Equal(JsonValueKind.Object, moduleJson.RootElement.GetProperty("inheritedDefaults").ValueKind);
        Assert.Equal(JsonValueKind.Object, moduleJson.RootElement.GetProperty("localOverrides").ValueKind);
        var projectRef = Assert.Single(module.References);
        var project = WorkspaceFileInspection.inspectConfiguration(root, projectRef.FullPath, Token);
        Assert.Equal("Project definition", project.Kind);
        var runRef = Assert.Single(project.References);
        var run = WorkspaceFileInspection.inspectConfiguration(root, runRef.FullPath, Token);
        Assert.Equal("Run definition", run.Kind);
        Assert.Equal(projectRef.FullPath, Assert.Single(run.References).FullPath);
        Assert.Contains("resultsParameterSpans", run.Json);
        foreach (var file in before) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        File.Delete(moduleRef.FullPath);
        // Broken workspaces are still inspectable, and missing references are visible.
        var broken = WorkspaceFileInspection.inspectConfiguration(root, workspaceFile, Token);
        Assert.False(Assert.Single(broken.References).Exists);
    }

    [Fact]
    public void Folder_listing_is_paged_filtered_and_does_not_open_file_contents()
    {
        var nested = temporary.CreateSubdirectory("nested");
        File.WriteAllText(Path.Combine(nested.FullName, "hidden.msgpack"), "not a valid definition");
        for (var i = 0; i < 205; i++) File.WriteAllText(Path.Combine(temporary.FullName, $"sample-{i:000}.msgpack"), "invalid");
        using var locked = new FileStream(Path.Combine(temporary.FullName, "sample-000.msgpack"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var first = WorkspaceFileInspection.listFolder(temporary.FullName, temporary.FullName, 0, "", Token);
        Assert.Equal(200, first.Entries.Length);
        Assert.True(first.HasMore);
        Assert.True(first.Entries[0].IsDirectory);
        var rest = WorkspaceFileInspection.listFolder(temporary.FullName, temporary.FullName, 200, "", Token);
        Assert.Equal(6, rest.Entries.Length);
        Assert.False(rest.HasMore);
        Assert.Empty(first.Entries.Select(e => e.FullPath).Intersect(rest.Entries.Select(e => e.FullPath)));
        var filtered = WorkspaceFileInspection.listFolder(temporary.FullName, temporary.FullName, 0, "SAMPLE-204", Token);
        Assert.Equal("sample-204.msgpack", Assert.Single(filtered.Entries).Name);
        Assert.Empty(WorkspaceFileInspection.listFolder(temporary.FullName, temporary.FullName, 1000, "", Token).Entries);
    }

    [Fact]
    public void Oversized_configuration_is_not_read_and_paths_cannot_escape_the_selected_root()
    {
        var path = Path.Combine(temporary.FullName, "large.msgpack");
        using (var file = File.Create(path)) file.SetLength(WorkspaceFileInspection.maxConfigurationBytes + 1);
        var report = WorkspaceFileInspection.inspectConfiguration(temporary.FullName, path, Token);
        Assert.Empty(report.Json);
        Assert.Contains("not loaded", report.Description);
        Assert.Throws<IOException>(() => WorkspaceFileInspection.checkedPath(temporary.FullName, Path.Combine(temporary.Parent!.FullName, "outside.msgpack")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceFileInspection.listFolder(temporary.FullName, temporary.FullName, 0, "", cancelled.Token));
    }

    public void Dispose() => temporary.Delete(true);
}
