using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Sortopia.FileDb;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    private sealed class CancelOnWorkerStart(CancellationTokenSource cancellation) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            if (value?.Contains("] Execution tuning:", StringComparison.Ordinal) == true) cancellation.Cancel();
            base.WriteLine(value);
        }
        public override Task WriteLineAsync(string? value) { WriteLine(value); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Tuned_launcher_forwards_cancellation_and_leaves_inputs_resumable()
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
            using var output = new CancelOnWorkerStart(cancel);
            try
            {
                Console.SetOut(output);
                var result = Sortopia.Cli.ExecutionWorker.launch(["--parameters-folder", folder], 25, 2L * 1073741824,
                    new(1, ExecutionResources.cpuWorkers(Environment.ProcessorCount, 25), 2L * 1073741824), null, cancel.Token);
                Assert.True(cancel.IsCancellationRequested, output.ToString());
                Assert.Equal(130, result);
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
        start.ArgumentList.Add("--filter-method"); start.ArgumentList.Add("*Tuned_launcher_forwards_cancellation*");
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
    [InlineData(3, "0.25GiB", 8)]
    [InlineData(25, "0.5GiB", 128)]
    [InlineData(25, "2GiB", 256)]
    [InlineData(80, "30%", 8)]
    [InlineData(25, "0.3GiB", 8)]
    public async Task Tuned_terminal_batch_matches_serial_results_under_cpu_and_memory_budgets(int percent, string memory, int population)
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
                     "--jobs", "auto", "--cpu-percent", percent.ToString(), "--memory", memory, "--execution-metrics", metrics })
            start.ArgumentList.Add(arg);
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
        Assert.Equal(0, measured.GetProperty("TotalMemoryBytes").GetInt64() % Environment.SystemPageSize);
        // Windows may also account for a hidden console host. Every associated
        // process is included in the same hard cap and aggregate peak below.
        Assert.True(measured.GetProperty("TotalProcesses").GetInt32() >= 2);
        var workers = ExecutionResources.cpuWorkers(Environment.ProcessorCount, percent);
        Assert.Equal(workers, measured.GetProperty("CpuWorkers").GetInt32());
        Assert.Equal(Math.Min(8, workers), measured.GetProperty("WorkersPerFile").GetInt32());
        Assert.True(measured.GetProperty("PeakCommittedBytes").GetInt64() <= measured.GetProperty("TotalMemoryBytes").GetInt64());
        Assert.True(measured.GetProperty("WorkerMemoryBytes").GetInt64() < measured.GetProperty("TotalMemoryBytes").GetInt64());
        Assert.Contains(measured.GetProperty("ServerGC").GetBoolean() ? "server GC" : "workstation GC", output);
        var expected = Directory.GetFiles(Path.Combine(original, "data", "results"), "*.msgpack", SearchOption.AllDirectories);
        Assert.NotEmpty(expected);
        foreach (var file in expected)
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(file)), SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, Path.GetRelativePath(original, file)))));
    }
}
