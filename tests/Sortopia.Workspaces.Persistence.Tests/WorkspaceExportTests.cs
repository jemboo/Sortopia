using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class WorkspaceExportTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-export-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private string Destination => Path.Combine(temporary.FullName, "Export");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private void CreateWorkspace()
    {
        var root = WorkspacePersistenceTests.Sample();
        var branch = WorkspacePersistenceTests.Sample(true).Modules[0];
        branch.Definition.moduleName = "branch";
        var nested = WorkspacePersistenceTests.Sample().Modules[0];
        // Include nested modules and saved, incomplete project drafts.
        nested.Projects[0].Definition.state = projectStateDto.NewDraft(new(), nested.Projects[0].Definition.configuration, null!);
        root.Modules[0] = new(root.Modules[0].Definition, [nested], root.Modules[0].Projects);
        root = new(root.Definition, [root.Modules[0], branch]);
        WorkspaceFileStore.CreateDocument(Root, root, Token);
        var project = Path.Combine(Root, "sorting", "search");
        Directory.CreateDirectory(Path.Combine(project, "data", "results", "empty"));
        File.WriteAllBytes(Path.Combine(project, "data", "results", "output.bin"), [0, 1, 2, 255]);
        Directory.CreateDirectory(Path.Combine(project, "runParameters"));
        File.WriteAllText(Path.Combine(project, "runParameters", "parameters.msgpack"), "parameter file");
        File.WriteAllText(Path.Combine(Root, "notes.txt"), "Workspace notes");
        Directory.CreateDirectory(Destination);
    }

    private static Dictionary<string, byte[]> Files(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(folder, path), File.ReadAllBytes);

    [Fact]
    public void Export_copies_the_complete_hierarchy_and_data_without_changing_source_or_ids()
    {
        CreateWorkspace();
        var before = Files(Root);
        WorkspaceFileStore.ExportWorkspace(Root, Destination, Token);
        var exported = Files(Destination);
        Assert.Equal(before.Keys.Order(), exported.Keys.Order());
        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, exported[path]);
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Root, path)));
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(Root, path)), File.GetLastWriteTimeUtc(Path.Combine(Destination, path)));
        }
        Assert.True(Directory.Exists(Path.Combine(Destination, "sorting", "search", "data", "results", "empty")));
        var source = WorkspaceFileStore.LoadWorkspace(Root);
        var copy = WorkspaceFileStore.LoadWorkspace(Destination);
        Assert.Equal(source.CreateEditDocument().Definition.id, copy.CreateEditDocument().Definition.id);
        Assert.Equal(source.CreateEditDocument().Modules[0].Projects[0].Runs[0].id, copy.CreateEditDocument().Modules[0].Projects[0].Runs[0].id);
        Assert.Single(copy.CreateEditDocument().Modules[0].Modules[0].Projects[0].Runs);
        Assert.Empty(Directory.GetDirectories(Destination, ".sortopia-*"));
    }

    [Theory]
    [InlineData("same")]
    [InlineData("child")]
    [InlineData("parent")]
    [InlineData("occupied")]
    public void Invalid_destinations_leave_existing_files_untouched(string kind)
    {
        CreateWorkspace();
        var destination = kind switch
        {
            "same" => Root,
            "child" => Directory.CreateDirectory(Path.Combine(Root, "Export")).FullName,
            "parent" => temporary.FullName,
            _ => Destination
        };
        if (kind == "occupied") File.WriteAllText(Path.Combine(destination, "keep.txt"), "Keep this");
        var before = Files(temporary.FullName);
        Assert.Throws<IOException>(() => WorkspaceFileStore.ExportWorkspace(Root, destination, Token));
        var after = Files(temporary.FullName);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
    }

    [Fact]
    public void Cancellation_and_unreadable_source_leave_the_destination_empty()
    {
        CreateWorkspace();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => WorkspaceFileStore.ExportWorkspace(Root, Destination, cancelled.Token));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
        using var locked = new FileStream(Path.Combine(Root, "notes.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Throws<IOException>(() => WorkspaceFileStore.ExportWorkspace(Root, Destination, Token));
        Assert.Empty(Directory.GetFileSystemEntries(Destination));
    }

    public void Dispose() => temporary.Delete(true);
}
