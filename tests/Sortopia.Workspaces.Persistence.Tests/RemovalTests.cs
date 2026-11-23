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
    private SavedWorkspace Create(bool branch = false, bool secondRun = false)
    {
        var document = WorkspacePersistenceTests.Sample(branch);
        if (secondRun)
        {
            var p = document.Modules[0].Projects[0];
            var other = Clone(p.Runs[0]); other.id = Guid.NewGuid(); other.runName = "Keep six";
            other.resultsParameterSpans.Single(span => span.keyName == "ChannelCount").values = ["6"];
            if (branch) other.setupParameterSpans.Single(span => span.keyName == "ChannelCount").values = ["6"];
            document.Modules[0].Projects[0] = new(p.Definition, [p.Runs[0], other]);
        }
        return WorkspaceFileStore.CreateDocument(Root, document, Token).Workspace;
    }
    private SavedWorkspace Save(WorkspaceDocument edit, SavedWorkspace before, WorkspaceFolderMove[]? moves = null)
    {
        var result = WorkspaceFileStore.SaveDocument(edit, before, Token, moves: moves);
        Assert.Empty(result.CleanupWarnings);
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
        return result.Workspace;
    }
    private static void RemoveFirstRun(WorkspaceDocument document)
    {
        var p = document.Modules[0].Projects[0]; var runs = p.Runs.Skip(1).ToArray();
        document.Modules[0].Projects[0] = new(p.Definition, runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Module_and_project_removal_delete_all_contents_but_keep_unlisted_siblings(bool module)
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument());
        var folder = Path.Combine(Root, "sorting", "search");
        Directory.CreateDirectory(Path.Combine(folder, "data", "nested"));
        File.WriteAllBytes(Path.Combine(folder, "data", "nested", "payload.bin"), new byte[1024]);
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "owned contents");
        var unrelated = Directory.CreateDirectory(Path.Combine(Root, "unlisted"));
        File.WriteAllText(Path.Combine(unrelated.FullName, "notes.txt"), "keep");
        if (module) edit = WorkspaceDocumentModule.withModules(edit, []);
        else
        {
            edit.Modules[0] = new(edit.Modules[0].Definition, [], []);
        }
        Save(edit, saved);
        Assert.False(Directory.Exists(module ? Path.GetDirectoryName(folder) : folder));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated.FullName, "notes.txt")));
        Assert.Empty(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules.SelectMany(m => m.Projects));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Run_removal_deletes_generated_files_and_only_unshared_data_and_locked_modules_cannot_be_renamed(bool branch)
    {
        var saved = Create(branch, secondRun: true);
        var p = saved.CreateEditDocument().Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        foreach (var run in p.Runs)
        {
            var report = RunParameterExecution.generateInScope(saved.ResolveRun(run.id), Root, null!, Token);
            Assert.Equal(runExecutionStatus.Completed, report.Execution.Status);
            foreach (var id in report.WorkItemIds)
                Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", id + ".msgpack"), null!, Token).Status);
        }
        Assert.Equal(branch ? 4 : 2, Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        var edit = Clone(saved.CreateEditDocument()); RemoveFirstRun(edit);
        edit.Modules[0].Definition.moduleName = "moved";
        Assert.Contains("generated run parameter files", Assert.Throws<InvalidOperationException>(() => Save(edit, saved, [new(Path.Combine(Root, "sorting"), Path.Combine(Root, "moved"))])).Message);
        edit = Clone(saved.CreateEditDocument()); RemoveFirstRun(edit); saved = Save(edit, saved);
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runs"), "*.msgpack"));
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        Assert.Equal(branch ? 2 : 1, Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        var remaining = RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).Single();
        Assert.Equal(p.Runs[1].id, remaining.RunId);
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token).Single().Outputs.State);
        edit = Clone(saved.CreateEditDocument()); RemoveFirstRun(edit); Save(edit, saved);
        Assert.DoesNotContain(Directory.GetFiles(folder, "*.msgpack", SearchOption.AllDirectories), path => Path.GetDirectoryName(path) != folder);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Parameter_file_removal_cleans_data_transactionally_and_retains_shared_outputs(bool branch, bool shared)
    {
        var saved = Create(branch, secondRun: true);
        if (shared)
        {
            var edit = Clone(saved.CreateEditDocument()); var p = edit.Modules[0].Projects[0];
            p.Runs[1].resultsParameterSpans = Clone(p.Runs[0].resultsParameterSpans);
            p.Runs[1].setupParameterSpans = Clone(p.Runs[0].setupParameterSpans);
            saved = Save(edit, saved);
        }
        var project = saved.CreateEditDocument().Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        foreach (var run in project.Runs)
        {
            var report = RunParameterExecution.generateInScope(saved.ResolveRun(run.id), Root, null!, Token);
            Assert.Equal(runExecutionStatus.Completed, report.Execution.Status);
            foreach (var id in report.WorkItemIds)
                Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", id + ".msgpack"), null!, Token).Status);
        }
        var reports = Directory.CreateDirectory(Path.Combine(folder, "data", "results", "reports"));
        var reportPath = Path.Combine(reports.FullName, "summary.csv");
        File.WriteAllText(reportPath, "retained until reset or last-run removal");
        var before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var firstFile = RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).First(item => item.RunId == project.Runs[0].id);
        using (var locked = new FileStream(Path.Combine(folder, "runParameters", firstFile.Id + ".msgpack"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => WorkspaceFileStore.RemoveRunParameterFiles(saved, project.Definition.id, project.Runs[0].id, Token));
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        saved = WorkspaceFileStore.RemoveRunParameterFiles(saved, project.Definition.id, project.Runs[0].id, Token);
        Assert.True(File.Exists(reportPath));
        Assert.True(saved.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal((branch ? 2 : 1) * (shared ? 2 : 1), Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.All(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token), item => Assert.Equal(outputState.Complete, item.Outputs.State));
        before = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(folder, "project"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => WorkspaceFileStore.RemoveRunParameterFiles(saved, project.Definition.id, project.Runs[1].id, Token)) is IOException or UnauthorizedAccessException);
        foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
        saved = WorkspaceFileStore.RemoveRunParameterFiles(saved, project.Definition.id, project.Runs[1].id, Token);
        Assert.False(saved.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal(2, saved.CreateEditDocument().Modules[0].Projects[0].Runs.Length);
        Assert.True(Directory.Exists(Path.Combine(folder, "runParameters")));
        Assert.Empty(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
    }

    [Fact]
    public void Failed_metadata_commit_restores_removed_folder_and_its_contents()
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument());
        edit.Modules[0] = new(edit.Modules[0].Definition, [], []);
        edit.Definition.workspaceName = "Changed title";
        var folder = Path.Combine(Root, "sorting", "search");
        File.WriteAllText(Path.Combine(folder, "payload.bin"), "restore me");
        var files = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => WorkspaceFileStore.SaveDocument(edit, saved, Token)) is IOException or UnauthorizedAccessException);
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Single(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects);
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
        Save(edit, saved); Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Locked_owned_file_stops_removal_before_any_changes()
    {
        var saved = Create(); var edit = Clone(saved.CreateEditDocument()); RemoveFirstRun(edit);
        var path = Path.Combine(Root, "sorting", "search", "runs", WorkspaceDefinitionFiles.Name(saved.CreateEditDocument().Modules[0].Projects[0].Runs[0].id));
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => WorkspaceFileStore.SaveDocument(edit, saved, Token));
        Assert.Single(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Runs);
        Assert.True(File.Exists(path));
        Save(edit, saved); Assert.False(File.Exists(path));
    }
    public void Dispose() => temporary.Delete(true);

    [Theory]
    [InlineData("workspace")]
    [InlineData("module")]
    [InlineData("project")]
    public void Reset_containers_preserves_definitions_and_unlisted_files_and_releases_projects(string kind)
    {
        var saved = Create();
        var module = saved.CreateEditDocument().Modules[0]; var project = module.Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        Directory.CreateDirectory(Path.Combine(folder, "runParameters"));
        Directory.CreateDirectory(Path.Combine(folder, "data", "results", "reports"));
        File.WriteAllText(Path.Combine(folder, "runParameters", "bad.msgpack"), "unreadable");
        File.WriteAllText(Path.Combine(folder, "data", "results", "reports", "summary.csv"), "old report");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "keep");
        var outside = Directory.CreateDirectory(Path.Combine(Root, "unlisted", "data"));
        File.WriteAllText(Path.Combine(outside.FullName, "notes.txt"), "keep outside");
        var id = kind == "workspace" ? saved.CreateEditDocument().Definition.id : kind == "module" ? module.Definition.id : project.Definition.id;
        var result = WorkspaceFileStore.ResetGeneratedFiles(saved, kind, id, Token);
        Assert.Empty(result.CleanupWarnings);
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        Assert.False(Directory.Exists(Path.Combine(folder, "runParameters")));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(folder, "notes.txt")));
        Assert.Equal("keep outside", File.ReadAllText(Path.Combine(outside.FullName, "notes.txt")));
        var loaded = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.Equal(saved.CreateEditDocument().Definition.id, loaded.CreateEditDocument().Definition.id);
        Assert.Equal(project.Definition.id, loaded.CreateEditDocument().Modules[0].Projects[0].Definition.id);
        Assert.Equal(project.Runs.Select(r => r.id), loaded.CreateEditDocument().Modules[0].Projects[0].Runs.Select(r => r.id));
        Assert.False(loaded.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.Empty(WorkspaceFileStore.InspectGeneratedFiles(loaded, Token).LockedProjectIds);
        WorkspaceFileStore.ResetGeneratedFiles(loaded, kind, id, Token); // Idempotent.
        Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_run_preserves_shared_outputs_until_last_run_and_invalidates_reports(bool branch)
    {
        var saved = Create(branch, secondRun: true);
        var edit = Clone(saved.CreateEditDocument()); var p = edit.Modules[0].Projects[0];
        p.Runs[1].resultsParameterSpans = Clone(p.Runs[0].resultsParameterSpans);
        p.Runs[1].setupParameterSpans = Clone(p.Runs[0].setupParameterSpans);
        saved = Save(edit, saved); p = saved.CreateEditDocument().Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        foreach (var run in p.Runs)
        {
            var generated = RunParameterExecution.generateInScope(saved.ResolveRun(run.id), Root, null!, Token);
            Assert.Equal(runExecutionStatus.Completed, generated.Execution.Status);
            foreach (var id in generated.WorkItemIds)
                Assert.Equal(runExecutionStatus.Completed, RunParameterExecution.executeFile(Path.Combine(folder, "runParameters", id + ".msgpack"), null!, Token).Status);
        }
        var reports = Directory.CreateDirectory(Path.Combine(folder, "data", "results", "reports"));
        File.WriteAllText(Path.Combine(reports.FullName, "summary.csv"), "stale report");
        saved = WorkspaceFileStore.ResetGeneratedFiles(saved, "run", p.Runs[0].id, Token).Workspace;
        Assert.True(saved.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.False(Directory.Exists(reports.FullName));
        var work = RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token);
        Assert.NotEmpty(work);
        Assert.All(work, item => Assert.Equal(outputState.Complete, item.Outputs.State));
        Assert.All(RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token), item => Assert.Equal(p.Runs[1].id, item.RunId));
        saved = WorkspaceFileStore.ResetGeneratedFiles(saved, "run", p.Runs[1].id, Token).Workspace;
        Assert.False(saved.CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
        Assert.False(Directory.Exists(Path.Combine(folder, "data")));
        Assert.False(Directory.Exists(Path.Combine(folder, "runParameters")));
        Assert.Equal(2, saved.CreateEditDocument().Modules[0].Projects[0].Runs.Length);
    }

    [Fact]
    public void Reset_failure_cancellation_and_stale_baseline_leave_files_and_locks_unchanged()
    {
        var saved = Create(); var project = saved.CreateEditDocument().Modules[0].Projects[0];
        var folder = Path.Combine(Root, "sorting", "search");
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        Directory.CreateDirectory(Path.Combine(folder, "runParameters"));
        var payload = Path.Combine(folder, "data", "payload.bin");
        File.WriteAllText(payload, "keep on failure");
        File.WriteAllText(Path.Combine(folder, "runParameters", "bad.msgpack"), "unreadable");
        var before = Directory.GetFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllBytes);
        void Unchanged()
        {
            Assert.Equal(before.Keys.Order(), Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Order());
            foreach (var (path, bytes) in before) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.True(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Definition.isSealed);
            Assert.Empty(Directory.GetFileSystemEntries(Root, ".sortopia-*"));
        }
        using (var locked = new FileStream(payload, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => WorkspaceFileStore.ResetGeneratedFiles(saved, "project", project.Definition.id, Token));
        Unchanged();
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(folder, "project"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(Record.Exception(() => WorkspaceFileStore.ResetGeneratedFiles(saved, "project", project.Definition.id, Token)) is IOException or UnauthorizedAccessException);
        Unchanged();
        Assert.ThrowsAny<OperationCanceledException>(() => WorkspaceFileStore.ResetGeneratedFiles(saved, "workspace", saved.CreateEditDocument().Definition.id, new CancellationToken(true)));
        Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.ResetGeneratedFiles(saved, "module", Guid.NewGuid(), Token));
        Unchanged();
        var edit = Clone(saved.CreateEditDocument()); edit.Definition.workspaceName = "Changed elsewhere";
        Save(edit, saved);
        Assert.Throws<IOException>(() => WorkspaceFileStore.ResetGeneratedFiles(saved, "project", project.Definition.id, Token));
        Assert.True(File.Exists(payload));
    }
}
