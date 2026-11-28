using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private sealed class CancelOnExecutionStart(CancellationTokenSource cancellation) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            if (value?.Contains("] Execution tuning:", StringComparison.Ordinal) == true) cancellation.Cancel();
            base.WriteLine(value);
        }
        public override Task WriteLineAsync(string? value) { WriteLine(value); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Bounded_execution_honors_cancellation_and_leaves_inputs_resumable()
    {
        const string probe = "SORTOPIA_CANCEL_LAUNCH_TEST";
        if (Environment.GetEnvironmentVariable(probe) == "1")
        {
            var (world, consumer) = SgdWorkspace(sgd: new(10, 100, null));
            Completed(Prepare(world, consumer).Execution);
            var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder, "runParameters");
            var files = Directory.GetFiles(folder, "*.msgpack").ToDictionary(path => path, File.ReadAllBytes);
            using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var previous = Console.Out;
            using var output = new CancelOnExecutionStart(cancel);
            try
            {
                Console.SetOut(output);
                var execute = FuncConvert.FromFunc<resultsExecutionOptions, CancellationToken, int>((_, _) =>
                    throw new Xunit.Sdk.XunitException("Cancelled execution must not begin reading inputs."));
                Assert.ThrowsAny<OperationCanceledException>(() => Sortopia.Cli.BoundedExecution.run(25, 2L * 1073741824,
                    new(1, ExecutionResources.cpuWorkers(Environment.ProcessorCount, 25), 2L * 1073741824), null, execute, cancel.Token));
                Assert.True(cancel.IsCancellationRequested, output.ToString());
            }
            finally { Console.SetOut(previous); }
            foreach (var file in files) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
            Assert.All(files.Keys, path => Assert.NotEqual(outputState.Invalid, RunParameterExecution.inspectFile(path, Token).Outputs.State));
            return;
        }
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet") {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(PreparedExecutionTests).Assembly.Location);
        start.ArgumentList.Add("--filter-method"); start.ArgumentList.Add("*Bounded_execution_honors_cancellation*");
        start.Environment[probe] = "1";
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(Token);
        var stderr = process.StandardError.ReadToEndAsync(Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
        Assert.True(process.ExitCode == 0, await stdout + await stderr);
    }

    [Theory]
    [InlineData(3, "0.25GiB", 8, false)]
    [InlineData(25, "0.5GiB", 128, false)]
    [InlineData(25, "2GiB", 256, false)]
    [InlineData(80, "30%", 8, false)]
    [InlineData(25, "0.3GiB", 8, true)]
    [InlineData(25, "2GiB", 256, true)]
    [InlineData(25, "", 8, false)]
    [InlineData(0, "0.25GiB", 8, false)]
    public async Task Tuned_terminal_batch_matches_serial_results_with_soft_targets_and_optional_caps(int percent, string memory, int population, bool hardCap)
    {
        var (world, consumer) = SgdWorkspace(sourceCount: population, sgd: new(4, 2, null), resultsSpans: [
            new() { keyName = "SorterCountPerPool", values = [population.ToString()] },
            new() { keyName = "SelectedSorterCountPerPool", values = [population.ToString()] },
            new() { keyName = "OrthoRate", values = ["1", "2", "3"] }]);
        Completed(Prepare(world, consumer).Execution);
        var original = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        var folder = Path.Combine(temporary.FullName, "Tuned");
        foreach (var file in Directory.GetFiles(original, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(folder, Path.GetRelativePath(original, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var baseline = RunParameterExecution.executeFolderWithOptions(Path.Combine(original, "runParameters"), null, new(1, 1, 0), null!, Token);
        Completed(baseline);
        var metrics = Path.Combine(temporary.FullName, "metrics.json");
        var inputOption = "--parameters-folder";
        var inputPath = Path.Combine(folder, "runParameters");
        if (memory == "30%")
        {
            // Match the Results page's checked-file command, including percentage budgets.
            var selectionPath = Path.Combine(temporary.FullName, "selection.json");
            File.WriteAllText(selectionPath, JsonSerializer.Serialize(Directory.GetFiles(inputPath, "*.msgpack")));
            inputOption = "--parameters-list";
            inputPath = selectionPath;
        }
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "sortopia.exe")) {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "execute-results", inputOption, inputPath,
                     "--jobs", "auto", "--execution-metrics", metrics })
            start.ArgumentList.Add(arg);
        if (percent > 0) { start.ArgumentList.Add("--cpu-percent"); start.ArgumentList.Add(percent.ToString()); }
        if (memory.Length > 0) { start.ArgumentList.Add("--memory"); start.ArgumentList.Add(memory); }
        if (hardCap) { start.ArgumentList.Add("--memory-cap"); start.ArgumentList.Add(memory); }
        using var child = Process.Start(start)!;
        var stdout = child.StandardOutput.ReadToEndAsync(Token);
        var stderr = child.StandardError.ReadToEndAsync(Token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try { await child.WaitForExitAsync(timeout.Token); }
        finally { if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(CancellationToken.None); } }
        var output = await stdout + await stderr;
        Assert.True(child.ExitCode == 0, output);
        Assert.All(output.Split('\n', StringSplitOptions.RemoveEmptyEntries), line =>
            Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}\] (?!\[\d{4}-)", line));
        using var doc = JsonDocument.Parse(File.ReadAllText(metrics));
        var measured = doc.RootElement;
        var cap = measured.GetProperty("HardMemoryLimitBytes").GetInt64();
        Assert.Equal(0, cap % Environment.SystemPageSize);
        Assert.Equal(memory.Length == 0, measured.GetProperty("MemoryTargetBytes").GetInt64() == 0);
        // Windows may also account for a hidden console host. Every associated
        // process is included in the aggregate peak below.
        Assert.True(measured.GetProperty("TotalProcesses").GetInt32() >= 1);
        var workers = ExecutionResources.cpuWorkers(Environment.ProcessorCount, percent == 0 ? 100 : percent);
        Assert.Equal(workers, measured.GetProperty("CpuWorkers").GetInt32());
        Assert.Equal(Math.Min(8, workers), measured.GetProperty("WorkersPerFile").GetInt32());
        Assert.True(measured.GetProperty("PeakCommittedBytes").GetInt64() > 0);
        if (hardCap)
        {
            Assert.True(cap > 0);
            Assert.True(measured.GetProperty("PeakCommittedBytes").GetInt64() <= cap);
            Assert.Equal(ExecutionResources.managedHeapLimitBytes(cap), measured.GetProperty("ManagedHeapLimitBytes").GetInt64());
        }
        else
        {
            Assert.Equal(0, cap);
            Assert.Contains("Hard memory cap: none.", output);
        }
        Assert.Contains(measured.GetProperty("ServerGC").GetBoolean() ? "server GC" : "workstation GC", output);
        var expected = Directory.GetFiles(Path.Combine(original, "data", "results"), "*.msgpack", SearchOption.AllDirectories);
        Assert.NotEmpty(expected);
        foreach (var file in expected)
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(file)), SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, Path.GetRelativePath(original, file)))));
    }
}
