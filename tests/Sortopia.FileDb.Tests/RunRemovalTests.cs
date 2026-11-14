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
        var saved = WorkspaceFileStore.LoadDocument(Root);
        var edit = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved));
        var m = edit.Modules.Single(module => module.Projects[0].Definition.id == project.Id);
        var p = m.Projects[0]; var original = p.Runs[0];
        var other = MessagePackSetup.deserialize<runDto>(MessagePackSetup.serialize(original));
        other.id = Guid.NewGuid(); other.runName = "Keep second setup";
        other.setupParameterSpans.Single(span => span.keyName == "Repl").values = ["1"];
        p.Definition.runs = [original.id, other.id]; m.Projects[0] = new(p.Definition, [original, other]);
        saved = WorkspaceFileStore.SaveDocument(Root, edit, saved, Token).Definition;
        var generated = RunParameterExecution.generateInScope(WorkspaceRunDocument.resolveByRunId(saved, original.id), Root, null!, Token);
        Completed(generated.Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        Completed(RunParameterExecution.executeFolder(Path.Combine(folder, "runParameters"), null!, null!, Token));
        if (generateSurvivor) Completed(RunParameterExecution.generateInScope(WorkspaceRunDocument.resolveByRunId(saved, other.id), Root, null!, Token).Execution);
        Assert.Equal(3, Directory.GetFiles(Path.Combine(folder, "data", "setup"), "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.Equal(4, DataFiles(folder, "results").Length);
        edit = MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(saved));
        m = edit.Modules.Single(module => module.Projects[0].Definition.id == project.Id); p = m.Projects[0];
        p.Definition.runs = [other.id]; m.Projects[0] = new(p.Definition, [other]);
        var result = WorkspaceFileStore.SaveDocument(Root, edit, saved, Token);
        Assert.Empty(result.CleanupWarnings);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "data", "setup"), "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.Equal(2, DataFiles(folder, "results").Length);
        Assert.Equal(generateSurvivor ? 2 : 0, RunParameterFiles.listWorkItems(Path.Combine(folder, "runParameters"), Token).Length);
        Completed(RunParameterExecution.generateInScope(WorkspaceRunDocument.resolveByRunId(result.Definition, other.id), Root, null!, Token).Execution);
        Assert.All(RunParameterExecution.inspectFolder(Path.Combine(folder, "runParameters"), null!, Token), item => Assert.Equal(outputState.Complete, item.Outputs.State));
    }
}
