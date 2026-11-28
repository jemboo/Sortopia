using Sortopia.FileDb;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData("Random", false)]
    [InlineData("Random", true)]
    [InlineData("Best", false)]
    [InlineData("Best", true)]
    public void Prepared_seeds_and_tests_execute_without_reloading_sources(string selection, bool msrs)
    {
        var (world, consumer) = SgdWorkspace(msrs: msrs, sourceCount: 16, width: 16, sgd: new(2, 2, null),
            resultsSpans: [new() { keyName = "InitialPoolSelection", values = [selection] }]);
        Completed(Prepare(world, consumer).Execution);
        var folder = Isolate(Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        var sources = Directory.GetFiles(Path.Combine(folder, "data", "setup"), "*.msgpack", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllBytes);
        Assert.Contains(sources.Keys, path => path.Contains("SortableTests"));
        Assert.Contains(sources.Keys, path => path.Contains("SorterModelSet"));
        var dispatched = false;
        var progress = new ItemProgress(update => {
            if (update.State != workItemExecutionState.Running) return;
            dispatched = true;
            // This test's isolated source is unavailable after preparation. Both
            // selection modes must execute using only their cached seed population.
            foreach (var file in sources.Keys) File.Delete(file);
        });
        try
        {
            Completed(RunParameterExecution.executeFilesWithOptions([path], new(1, 2, 0), null!, progress, Token));
            Assert.True(dispatched);
        }
        finally
        {
            foreach (var file in sources) File.WriteAllBytes(file.Key, file.Value);
        }
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
    }
}
