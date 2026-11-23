using Sortopia.Core.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Removal_preserves_shared_setup_but_deletes_results_depending_on_removed_setup_sets(bool generateSurvivor)
    {
        var (world, project) = Create(true);
        var saved = WorkspaceFileStore.LoadWorkspace(Root);
        var edit = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved.CreateEditDocument()));
        var m = edit.Modules.Single(module => module.Projects[0].Definition.id == project.Definition.id);
        var p = m.Projects[0]; var original = p.Runs[0];
        var other = MessagePackSetup.deserialize<runDto>(MessagePackSetup.serialize(original));
        other.id = Guid.NewGuid(); other.runName = "Keep second setup";
        other.setupParameterSpans.Single(span => span.keyName == "Repl").values = ["1"];
        m.Projects[0] = new(p.Definition, [original, other]);
        saved = WorkspaceFileStore.SaveDocument(edit, saved, Token).Workspace;
        var generated = RunParameterExecution.generateInScope(saved.ResolveRun(original.id), Root, null!, Token);
        Completed(generated.Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(project.Definition.id).RelativeFolder);
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        if (generateSurvivor) Completed(RunParameterExecution.generateInScope(saved.ResolveRun(other.id), Root, null!, Token).Execution);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(folder, "data", "setup"), "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.Equal(4, DataFiles(folder, "results").Length);
        edit = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved.CreateEditDocument()));
        m = edit.Modules.Single(module => module.Projects[0].Definition.id == project.Definition.id); p = m.Projects[0];
        m.Projects[0] = new(p.Definition, [other]);
        var result = WorkspaceFileStore.SaveDocument(edit, saved, Token);
        Assert.Empty(result.CleanupWarnings);
        Assert.Equal(generateSurvivor ? 2 : 0, DataFiles(folder, "setup").Length);
        Assert.Equal(generateSurvivor ? 2 : 0, DataFiles(folder, "results").Length);
        Assert.Equal(generateSurvivor ? 2 : 0, RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).Length);
        Completed(RunParameterExecution.generateInScope(result.Workspace.ResolveRun(other.id), Root, null!, Token).Execution);
        Assert.All(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token), item =>
            Assert.Equal(generateSurvivor ? outputState.Complete : outputState.Pending, item.Outputs.State));
    }
}
