using Sortopia.Workspaces.Serialization;
using System.Collections.Concurrent;
using Sortopia.FileDb;
using Sortopia.Functions;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private sealed class GenerationUpdates(Action<workItemGenerationProgress> receive) : IProgress<workItemGenerationProgress>
    {
        public void Report(workItemGenerationProgress value) => receive(value);
    }

    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    public void Generation_progress_identifies_parallel_jobs_and_reports_snapshot_boundaries(bool reporting, int generations)
    {
        var (world, consumer) = SgdWorkspace(resultsSpans: [
            new() { keyName = "MutationMod", values = ["0", "1"] }],
            reporting: reporting ? new("Constant(2)", "Constant(1)") : null, sgd: new(2, generations / 2));
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var paths = Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack");
        var events = new ConcurrentQueue<workItemGenerationProgress>();
        runExecutionReport Execute()
        {
            var queue = new Queue<string>(paths);
            return RunParameterExecution.executeQueuedFilesWithProgress(paths,
                () => queue.TryDequeue(out var path) ? path : null!, new(2), null!, null!, new GenerationUpdates(events.Enqueue), Token);
        }
        Completed(Execute());
        Assert.Equal(2, events.Select(value => value.Id).Distinct().Count());
        foreach (var path in paths)
        {
            var id = RunParameterFiles.loadFile(path).Id;
            var observed = events.Where(value => value.Id == id).Select(value => value.Generation).ToArray();
            Assert.Equal(0, observed[0]); Assert.Equal(generations, observed[^1]);
            Assert.Equal(observed.Distinct().Order(), observed);
            Assert.Equal(generations == 0 ? [0] : new[] { 0, 2, 4, 6 }, observed);
        }
        events.Clear();
        var reused = Execute(); Completed(reused);
        Assert.All(reused.Results.SelectMany(result => result.StoredOutputs), output => Assert.True(output.Reused));
        Assert.Empty(events); // Completed outputs do not rerun evolution to obtain progress.
    }

    [Fact]
    public void Cancellation_keeps_last_generation_and_retry_resumes_from_checkpoint()
    {
        var (world, consumer) = SgdWorkspace(sgd: new(2, 3),
            reporting: new("Constant(2)", "Constant(1)"));
        Completed(Prepare(world, consumer).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var path = Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
        using var cancellation = new CancellationTokenSource();
        var events = new List<int>();
        runExecutionReport Execute(CancellationToken token)
        {
            var queued = true;
            return RunParameterExecution.executeQueuedFilesWithProgress([path], () => { if (!queued) return null!; queued = false; return path; },
                new(1), null!, null!, new GenerationUpdates(value => {
                    events.Add(value.Generation);
                    if (value.Generation == 2) cancellation.Cancel();
                }), token);
        }
        Assert.Equal(runExecutionStatus.Cancelled, Execute(cancellation.Token).Status);
        Assert.Equal(new[] { 0, 2 }, events);
        events.Clear(); Completed(Execute(Token));
        Assert.Equal(new[] { 2, 4, 6 }, events);
    }
}
