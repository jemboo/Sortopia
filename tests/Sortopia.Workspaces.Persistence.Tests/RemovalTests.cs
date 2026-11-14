using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class RemovalTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-removal-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static T Clone<T>(T value) => MessagePackSetup.deserialize<T>(MessagePackSetup.serialize(value));
    private WorkspaceDocument Create(bool branch = false, bool secondRun = false)
    {
        var document = WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample(branch));
        if (secondRun)
        {
            var p = document.Modules[0].Projects[0];
            var other = Clone(p.Runs[0]); other.id = Guid.NewGuid(); other.runName = "Keep six";
            other.resultsParameterSpans.Single(span => span.keyName == "ChannelCount").values = ["6"];
            if (branch) other.setupParameterSpans.Single(span => span.keyName == "ChannelCount").values = ["6"];
            p.Definition.runs = [p.Runs[0].id, other.id];
            document.Modules[0].Projects[0] = new(p.Definition, [p.Runs[0], other]);
        }
        return WorkspaceFileStore.CreateDocument(Root, document, Token).Definition;
    }
    private WorkspaceDocument Save(WorkspaceDocument edit, WorkspaceDocument before, WorkspaceFolderMove[]? moves = null)
    {
        var result = WorkspaceFileStore.SaveDocument(Root, edit, before, Token, moves: moves);
        Assert.Empty(result.CleanupWarnings);
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
        return result.Definition;
    }
    private static void RemoveFirstRun(WorkspaceDocument document)
    {
        var p = document.Modules[0].Projects[0]; var runs = p.Runs.Skip(1).ToArray();
        p.Definition.runs = runs.Select(run => run.id).ToArray();
        document.Modules[0].Projects[0] = new(p.Definition, runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_and_project_removal_delete_all_contents_but_keep_unlisted_siblings(bool module)
    {
        var saved = Create(); var edit = Clone(saved);
        var folder = Path.Combine(Root, "sorting", "search");
        Directory.CreateDirectory(Path.Combine(folder, "data", "nested"));
        File.WriteAllBytes(Path.Combine(folder, "data", "nested", "payload.bin"), new byte[1024]);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "owned contents");
        var unrelated = Directory.CreateDirectory(Path.Combine(Root, "unlisted"));
        File.WriteAllText(Path.Combine(unrelated.FullName, "notes.txt"), "keep");
        if (module) edit = new(new workspaceDefinitionDto { schemaVersion = saved.Definition.schemaVersion, id = saved.Definition.id,
            workspaceName = saved.Definition.workspaceName, folderName = saved.Definition.folderName, modules = [] }, []);
        else
        {
            edit.Modules[0].Definition.projects = [];
            edit.Modules[0] = new(edit.Modules[0].Definition, [], []);
        }
        Save(edit, saved);
        Assert.False(Directory.Exists(module ? Path.GetDirectoryName(folder) : folder));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated.FullName, "notes.txt")));
        Assert.Empty(WorkspaceFileStore.LoadDocument(Root).Modules.SelectMany(m => m.Projects));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_removal_deletes_generated_files_and_only_unshared_data_even_during_parent_move(bool branch)
    {
        var saved = Create(branch, secondRun: true);
        var p = saved.Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        foreach (var run in p.Runs)
        {
            var report = RunParameterExecution.generateInScope(WorkspaceRunDocument.resolveByRunId(saved, run.id), Root, null!, Token);
            Assert.Equal(runExecutionStatus.Completed, report.Execution.Status);
            foreach (var id in report.WorkItemIds)
                Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", id + ".msgpack"), null!, Token).Status);
        }
        Assert.Equal(branch ? 4 : 2, Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        var edit = Clone(saved); RemoveFirstRun(edit);
        edit.Definition.modules[0].folderName = "moved"; edit.Modules[0].Definition.folderName = "moved";
        saved = Save(edit, saved, [new(Path.Combine(Root, "sorting"), Path.Combine(Root, "moved"))]);
        folder = Path.Combine(Root, "moved", "search");
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runs"), "*.msgpack"));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        Assert.Equal(branch ? 2 : 1, Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        var remaining = RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).Single();
        Assert.Equal(p.Runs[1].id, remaining.RunId);
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token).Single().Outputs.State);
        edit = Clone(saved); RemoveFirstRun(edit); Save(edit, saved);
        Assert.DoesNotContain(Directory.GetFiles(folder, "*.msgpack", SearchOption.AllDirectories), path => Path.GetDirectoryName(path) != folder);
    }

    [Fact]
    public void Failed_metadata_commit_restores_removed_folder_and_its_contents()
    {
        var saved = Create(); var edit = Clone(saved);
        edit.Modules[0].Definition.projects = []; edit.Modules[0] = new(edit.Modules[0].Definition, [], []);
        edit.Definition.workspaceName = "Changed title";
        var folder = Path.Combine(Root, "sorting", "search");
        File.WriteAllText(Path.Combine(folder, "payload.bin"), "restore me");
        var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => WorkspaceFileStore.SaveDocument(Root, edit, saved, Token)) is IOException or UnauthorizedAccessException);
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Single(WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects);
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
        Save(edit, saved); Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Locked_owned_file_stops_removal_before_any_changes()
    {
        var saved = Create(); var edit = Clone(saved); RemoveFirstRun(edit);
        var path = Path.Combine(Root, "sorting", "search", "runs", WorkspaceDefinitionFiles.Name(saved.Modules[0].Projects[0].Runs[0].id));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => WorkspaceFileStore.SaveDocument(Root, edit, saved, Token));
        Assert.Single(WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Runs);
        Assert.True(File.Exists(path));
        Save(edit, saved); Assert.False(File.Exists(path));
    }
    public void Dispose() => temporary.Delete(true);
}
