using Sortopia.FileDb;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Parallel_execution_shares_validated_common_inputs_without_rereading_them(bool varyingModels, bool queued)
    {
        var paths = ParallelFiles();
        var first = RunParameterFiles.loadFile(paths[0]);
        if (!varyingModels)
            paths = paths.Where(path => RunParameterFiles.loadFile(path).SetupInputs.SequenceEqual(first.SetupInputs)).ToArray();
        var items = paths.Select(RunParameterFiles.loadFile).ToArray();
        var common = items.Select(item => item.SetupInputs.AsEnumerable()).Aggregate((left, right) => left.Intersect(right)).ToArray();
        Assert.Equal(varyingModels ? 1 : 2, common.Length);
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(paths[0]))!;
        var store = new SortopiaFileDb(folder, first.ProjectId);
        var locks = new List<FileStream>();
        using var barrier = new Barrier(2);
        var progress = new ItemProgress(value => {
            if (value.State != workItemExecutionState.Running) return;
            lock (locks)
                if (locks.Count == 0)
                    foreach (var input in common)
                        locks.Add(new FileStream(store.GetPath(input), FileMode.Open, FileAccess.Read, FileShare.None));
            Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "Jobs must use shared inputs concurrently.");
        });
        try
        {
            // Locks are acquired after preflight and held for the whole batch.
            // Noncommon model inputs remain readable in the mixed-source case.
            var queue = new Queue<string>(paths);
            var report = queued
                ? RunParameterExecution.executeQueuedFilesWithOptions([paths[0]], () => queue.TryDequeue(out var path) ? path : null!, new(2), null!, progress, Token)
                : RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, progress, Token);
            Completed(report);
            Assert.Equal(common.Length, locks.Count);
            Assert.Equal(paths.Length, report.Results.Length);
        }
        finally
        {
            foreach (var stream in locks) stream.Dispose();
        }
    }

    [Fact]
    public void Single_file_execution_uses_the_validated_input_snapshot_without_reading_it_again()
    {
        var (world, project) = Create(true);
        Completed(Prepare(world, project).Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var parameters = Path.Combine(folder, "runParameters");
        var item = RunParameterFiles.listWorkItems(parameters, Token).First();
        var store = new SortopiaFileDb(folder, project.Id);
        var locks = new List<FileStream>();
        var progress = new ItemProgress(value => {
            if (value.State == workItemExecutionState.Running)
                foreach (var input in item.SetupInputs)
                    locks.Add(new FileStream(store.GetPath(input), FileMode.Open, FileAccess.Read, FileShare.None));
        });
        try
        {
            // Running is reported after complete preflight. Holding exclusive read
            // handles makes any redundant input read fail, without changing a byte.
            var report = RunParameterExecution.executeFilesWithStatus(
                [Path.Combine(parameters, item.Id + ".msgpack")], null!, progress, Token);
            Completed(report);
            Assert.NotEmpty(locks);
            Assert.Single(report.Results);
        }
        finally
        {
            foreach (var stream in locks) stream.Dispose();
        }
    }
}
