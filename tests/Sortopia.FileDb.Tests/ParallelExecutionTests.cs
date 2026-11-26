using Sortopia.Workspaces.Serialization;
using System.Collections.Concurrent;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private string[] ParallelFiles()
    {
        var (world, project) = Create(true);
        var prepared = Prepare(world, project); Completed(prepared.Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(project.Definition.id).RelativeFolder);
        return prepared.WorkItemIds.Select(id => Path.Combine(folder, "runParameters", id + ".msgpack")).ToArray();
    }

    [Fact]
    public void Parallel_jobs_are_bounded_overlap_and_retain_selection_order()
    {
        var paths = ParallelFiles();
        using var barrier = new Barrier(2);
        var active = 0; var peak = 0;
        var events = new ConcurrentQueue<workItemExecutionProgress>();
        var progress = new ItemProgress(update => {
            events.Enqueue(update);
            if (update.State == workItemExecutionState.Running)
            {
                var count = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref peak, Math.Max(peak, count));
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "Two jobs must run concurrently.");
            }
            else Interlocked.Decrement(ref active);
        });
        var result = RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, progress, Token);
        Completed(result); Assert.Equal(2, peak); Assert.Equal(0, active);
        Assert.Equal(paths.Select(path => RunParameterFiles.loadFile(path).Parameters), result.Results.Select(row => row.Parameters));
        Assert.All(events.GroupBy(row => row.Id), group => Assert.Equal(
            new[] { workItemExecutionState.Running, workItemExecutionState.Complete }, group.Select(row => row.State)));
        var retry = RunParameterExecution.executeFilesWithOptions(paths, new(3), null!, null!, Token);
        Completed(retry); Assert.All(retry.Results.SelectMany(row => row.StoredOutputs), output => Assert.True(output.Reused));
    }

    [Fact]
    public void Queued_execution_validates_newly_added_files_and_keeps_parallel_jobs_bounded()
    {
        var paths = ParallelFiles();
        var queue = new ConcurrentQueue<string>(paths);
        using var barrier = new Barrier(2);
        var active = 0; var peak = 0;
        var events = new ConcurrentQueue<workItemExecutionProgress>();
        var progress = new ItemProgress(update =>
        {
            events.Enqueue(update);
            if (update.State == workItemExecutionState.Running)
            {
                var count = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref peak, Math.Max(peak, count));
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "Queued jobs must run concurrently.");
            }
            else Interlocked.Decrement(ref active);
        });
        var result = RunParameterExecution.executeQueuedFilesWithOptions([paths[0]],
            () => queue.TryDequeue(out var path) ? path : null!, new(2), null!, progress, Token);
        Completed(result); Assert.Equal(2, peak); Assert.Equal(0, active);
        Assert.Equal(paths.Length, result.Results.Length);
        Assert.All(events.GroupBy(row => row.Id), group => Assert.Equal(
            new[] { workItemExecutionState.Running, workItemExecutionState.Complete }, group.Select(row => row.State)));
    }

    [Fact]
    public void Newly_checked_missing_file_fails_without_stopping_other_queued_files()
    {
        var paths = ParallelFiles();
        var missingId = Guid.NewGuid();
        var missing = Path.Combine(Path.GetDirectoryName(paths[0])!, missingId + ".msgpack");
        var queue = new Queue<string>([paths[0], missing, paths[1]]);
        var events = new List<workItemExecutionProgress>();
        var result = RunParameterExecution.executeQueuedFilesWithOptions([paths[0]],
            () => queue.TryDequeue(out var path) ? path : null!, new(1), null!, new ItemProgress(events.Add), Token);
        Assert.Equal(runExecutionStatus.Failed, result.Status);
        Assert.Equal(2, result.Results.Length); Assert.Contains(missing, result.Error);
        Assert.Single(events, row => row.Id == missingId && row.State == workItemExecutionState.Failed);
        Assert.Equal(2, events.Count(row => row.State == workItemExecutionState.Complete));
    }

    [Fact]
    public async Task Independent_results_factories_do_not_hold_the_setup_writer_lock()
    {
        var paths = ParallelFiles();
        using var barrier = new Barrier(2);
        var tasks = paths.Take(2).Select(path => Task.Run(() => {
            var item = RunParameterFiles.loadFile(path);
            var folder = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
            var host = FileResultsHost.createWithParallelism(1, folder, item.ProjectId, item.Results, item.Parameters,
                Microsoft.FSharp.Collections.ListModule.OfSeq(item.SetupInputs.Select(reference => new storedOutput(reference, true))), Token);
            Assert.Equal(1, host.EvaluationParallelism);
            var plan = FileResultsHost.plan(item.ProjectId, item.Results, item.Parameters, item.SetupInputs);
            ISortopiaDb store = new SortopiaFileDb(folder, item.ProjectId);
            // Factories deliberately fail after meeting. They must overlap even
            // though no data is published, proving generation is outside the lock.
            return store.GetOrCreate(plan.Values.Single().Key, FuncConvert.FromFunc<Unit, FSharpResult<sortopiaStorageData, string>>(_ => {
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "Independent factories were serialized.");
                return FSharpResult<sortopiaStorageData, string>.NewError("factory reached");
            }), Token);
        }, Token)).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.All(results, result => { Assert.True(result.IsError); Assert.Equal("factory reached", result.ErrorValue); });
    }

    [Fact]
    public void Failed_parallel_row_does_not_stop_other_rows_and_can_be_retried()
    {
        var paths = ParallelFiles();
        var item = RunParameterFiles.loadFile(paths[0]);
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(paths[0]))!;
        var output = new SortopiaFileDb(folder, item.ProjectId).GetPath(
            FileResultsHost.plan(item.ProjectId, item.Results, item.Parameters, item.SetupInputs).Values.Single().Key.Reference);
        var events = new ConcurrentQueue<workItemExecutionProgress>();
        var progress = new ItemProgress(update => {
            events.Enqueue(update);
            if (update.Id == item.Id && update.State == workItemExecutionState.Running) Directory.CreateDirectory(output);
        });
        var result = RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, progress, Token);
        Assert.Equal(runExecutionStatus.Failed, result.Status); Assert.Equal(paths.Length - 1, result.Results.Length);
        Assert.Contains(output, result.Error);
        Assert.Single(events, row => row.State == workItemExecutionState.Failed);
        Assert.Equal(paths.Length - 1, events.Count(row => row.State == workItemExecutionState.Complete));
        Directory.Delete(output);
        Completed(RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, null!, Token));
    }

    [Fact]
    public void Cancellation_marks_active_jobs_and_leaves_unstarted_work_pending()
    {
        var paths = ParallelFiles();
        using var cancellation = new CancellationTokenSource();
        using var started = new CountdownEvent(2);
        var events = new ConcurrentQueue<workItemExecutionProgress>();
        var progress = new ItemProgress(update => {
            events.Enqueue(update);
            if (update.State != workItemExecutionState.Running) return;
            if (started.Signal()) cancellation.Cancel();
            Assert.True(started.Wait(TimeSpan.FromSeconds(15)));
        });
        var result = RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, progress, cancellation.Token);
        Assert.Equal(runExecutionStatus.Cancelled, result.Status); Assert.Empty(result.Results);
        Assert.Equal(2, events.Count(row => row.State == workItemExecutionState.Running));
        Assert.Equal(2, events.Count(row => row.State == workItemExecutionState.Cancelled));
        Assert.DoesNotContain(events, row => row.State == workItemExecutionState.Failed);
        Assert.All(RunParameterExecution.inspectFolder(Path.GetDirectoryName(paths[0])!, null!, Token), row => Assert.Equal(outputState.Pending, row.Outputs.State));
        Completed(RunParameterExecution.executeFilesWithOptions(paths, new(2), null!, null!, Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Duplicate_output_owners_are_rejected_before_conflicting_work_starts(bool queued)
    {
        var paths = ParallelFiles();
        var item = RunParameterFiles.loadFile(paths[0]);
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(paths[0]))!;
        var options = Sortopia.Core.Serialization.MessagePackSetup.options.WithCompression(MessagePack.MessagePackCompression.Lz4BlockArray);
        var envelope = MessagePack.MessagePackSerializer.Deserialize<runParametersEnvelopeDto>(File.ReadAllBytes(paths[0]), options, Token);
        var original = Sortopia.Core.Serialization.MessagePackSetup.deserialize<runParametersDto>(envelope.Payload);
        var copy = new runParametersDto(original.Version, original.ProjectId, original.RunId, original.FunctionName,
            original.FunctionVersion, original.DatabaseName, original.Outputs, original.Parameters, original.SetupInputs, original.SetNumber + 100, original.SetupSetNumber,
            original.SetupSuffixStages, original.ResultsSuffixStages);
        var payload = Sortopia.Core.Serialization.MessagePackSetup.serialize(copy);
        var hash = System.Security.Cryptography.SHA256.HashData(payload);
        var id = new Guid(hash[..16]);
        File.WriteAllBytes(Path.Combine(folder, "runParameters", id + ".msgpack"), MessagePack.MessagePackSerializer.Serialize(
            new runParametersEnvelopeDto(1, "runParameters", hash, payload), options, Token));
        var events = new List<workItemExecutionProgress>();
        var selected = new[] { paths[0], Path.Combine(folder, "runParameters", id + ".msgpack") };
        var queue = new Queue<string>(selected);
        var result = queued
            ? RunParameterExecution.executeQueuedFilesWithOptions([paths[0]], () => queue.TryDequeue(out var path) ? path : null!, new(1), null!, new ItemProgress(events.Add), Token)
            : RunParameterExecution.executeFilesWithOptions(selected, new(2), null!, new ItemProgress(events.Add), Token);
        Assert.Equal(runExecutionStatus.Failed, result.Status);
        Assert.Contains(item.Id + ".msgpack", result.Error); Assert.Contains(id + ".msgpack", result.Error);
        Assert.Contains("same results output", result.Error);
        if (queued)
        {
            Assert.Single(result.Results);
            Assert.Equal(new[] { workItemExecutionState.Running, workItemExecutionState.Complete }, events.Where(row => row.Id == item.Id).Select(row => row.State));
            Assert.Single(events, row => row.Id == id && row.State == workItemExecutionState.Failed);
        }
        else
        {
            Assert.Empty(result.Results);
            Assert.Equal(2, events.Count); Assert.All(events, row => Assert.Equal(workItemExecutionState.Failed, row.State));
            Assert.Empty(DataFiles(folder, "results"));
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-2")]
    [InlineData("1.5")]
    [InlineData("abc")]
    public void Cli_rejects_invalid_parallel_jobs(string jobs)
    {
        Assert.Equal(2, Sortopia.Cli.Program.main(["execute-results", "--parameters-folder", "unused", "--jobs", jobs]));
    }

    [Fact]
    public void Cli_accepts_parallel_folder_and_single_file_execution()
    {
        var paths = ParallelFiles();
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters", paths[0], "--jobs", "2"]));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters-folder", Path.GetDirectoryName(paths[0])!, "--jobs", "2"]));
    }

    [Fact]
    public void Cpu_budget_bounds_file_workers_even_when_more_jobs_are_requested()
    {
        var paths = ParallelFiles();
        var active = 0; var peak = 0;
        var events = new ItemProgress(update =>
        {
            if (update.State == workItemExecutionState.Running)
            {
                var count = Interlocked.Increment(ref active);
                Interlocked.Exchange(ref peak, Math.Max(peak, count));
            }
            else Interlocked.Decrement(ref active);
        });
        Completed(RunParameterExecution.executeFilesWithOptions(paths, new(8, 1, 0), null!, events, Token));
        Assert.Equal(1, peak); Assert.Equal(0, active);
    }

    [Fact]
    public void Cli_batch_preflights_the_entire_selection_then_executes_only_selected_files()
    {
        var paths = ParallelFiles();
        var selection = Path.Combine(temporary.FullName, "selection.json");
        File.WriteAllText(selection, System.Text.Json.JsonSerializer.Serialize(new[] { paths[0], paths[0] }));
        Assert.Equal(1, Sortopia.Cli.Program.main(["execute-results", "--parameters-list", selection, "--jobs", "auto"]));
        Assert.All(RunParameterExecution.inspectFolder(Path.GetDirectoryName(paths[0])!, null!, Token), row => Assert.Equal(outputState.Pending, row.Outputs.State));
        File.WriteAllText(selection, System.Text.Json.JsonSerializer.Serialize(new[] { paths[1] }));
        Assert.Equal(0, Sortopia.Cli.Program.main(["execute-results", "--parameters-list", selection, "--jobs", "auto"]));
        Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(paths[1], Token).Outputs.State);
        Assert.Equal(outputState.Pending, RunParameterExecution.inspectFile(paths[0], Token).Outputs.State);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("broken")]
    public void Cli_rejects_invalid_selection_lists(string json)
    {
        var selection = Path.Combine(temporary.FullName, "selection.json"); File.WriteAllText(selection, json);
        Assert.Equal(2, Sortopia.Cli.Program.main(["execute-results", "--parameters-list", selection]));
    }

    [Theory]
    [InlineData("0", "50%")]
    [InlineData("101", "50%")]
    [InlineData("50", "-1GiB")]
    [InlineData("50", "101%")]
    public void Cli_rejects_invalid_resource_limits_before_execution(string cpu, string memory)
    {
        Assert.Equal(2, Sortopia.Cli.Program.main(["execute-results", "--parameters", "unused", "--cpu-percent", cpu, "--memory", memory]));
    }
}
