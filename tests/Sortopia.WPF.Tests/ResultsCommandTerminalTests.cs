using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsCommandTerminalTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-terminal-");

    [Fact]
    public void Terminal_executes_the_complete_text_and_preserves_literal_arguments_beyond_the_process_command_line_limit()
    {
        var output = Path.Combine(temporary.FullName, "arguments.json");
        var paths = new[] { @"C:\O'Brien $HOME $(throw 'expanded') & 100% ! [x] café\one.msgpack", @"C:\second folder\two.msgpack" };
        var command = "#" + new string('x', 40000) + Environment.NewLine
            + "function sortopia { [pscustomobject]@{ Arguments = @($args); PathsJson = [System.IO.File]::ReadAllText($args[2]) } | ConvertTo-Json -Compress | Set-Content -Encoding UTF8 -LiteralPath '"
            + output.Replace("'", "''") + "' }" + Environment.NewLine
            + ResultsCommandTerminal.FormatBatchCommand("sortopia", paths, new("25", "2GiB", "3"));
        ResultsCommandTerminal.Launch(command, temporary.FullName, start =>
        {
            Assert.True(start.UseShellExecute);
            Assert.Equal(ProcessWindowStyle.Normal, start.WindowStyle);
            Assert.Contains("-NoExit", start.ArgumentList);
            Assert.Equal(temporary.FullName, start.WorkingDirectory);
            Assert.True(start.ArgumentList.Sum(argument => argument.Length) < 30000);
            // Exercise the actual PowerShell bootstrap without opening an interactive test window.
            start.UseShellExecute = false; start.CreateNoWindow = true; start.WindowStyle = ProcessWindowStyle.Hidden;
            start.RedirectStandardOutput = start.RedirectStandardError = true;
            start.ArgumentList.Remove("-NoExit");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20000)) { process.Kill(entireProcessTree: true); Assert.Fail("Terminal command timed out."); }
            Assert.True(process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        });
        using var captured = JsonDocument.Parse(File.ReadAllText(output));
        var arguments = captured.RootElement.GetProperty("Arguments").Deserialize<string[]>()!;
        Assert.Equal(new[] { "execute-results", "--parameters-list" }, arguments.Take(2));
        Assert.Equal(new[] { "--cpu-percent", "25", "--memory", "2GiB", "--jobs", "3" }, arguments.Skip(3));
        Assert.Equal(paths, JsonSerializer.Deserialize<string[]>(captured.RootElement.GetProperty("PathsJson").GetString()!));
        Assert.False(File.Exists(arguments[2]));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetTempPath(), "sortopia-results-*.txt"), path => File.ReadAllText(path) == command);
    }

    [Fact]
    public void Failed_terminal_start_removes_its_temporary_command_file()
    {
        var command = "# " + Guid.NewGuid();
        string? scriptPath = null;
        var error = Assert.Throws<IOException>(() => ResultsCommandTerminal.Launch(command, temporary.FullName, _ =>
        {
            scriptPath = Directory.EnumerateFiles(Path.GetTempPath(), "sortopia-results-*.txt").Single(path => File.ReadAllText(path) == command);
            throw new IOException("Terminal unavailable");
        }));
        Assert.Equal("Terminal unavailable", error.Message);
        Assert.NotNull(scriptPath); Assert.False(File.Exists(scriptPath));
    }

    [Fact]
    public void Command_display_prefers_an_adjacent_or_matching_source_build_then_the_installed_cli()
    {
        var app = Path.Combine(temporary.FullName, "src", "Sortopia.WPF", "bin", "Debug", "net10.0-windows");
        Directory.CreateDirectory(app);
        Assert.Equal("sortopia", ResultsCommandTerminal.FindExecutable(app));
        File.WriteAllText(Path.Combine(temporary.FullName, "Sortopia.sln"), "");
        var cli = Path.Combine(temporary.FullName, "src", "Sortopia.Cli", "bin", "Debug", "net10.0", "sortopia.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(cli)!); File.WriteAllText(cli, "");
        Assert.Equal(cli, ResultsCommandTerminal.FindExecutable(app + Path.DirectorySeparatorChar));
        var adjacent = Path.Combine(app, "sortopia.exe"); File.WriteAllText(adjacent, "");
        Assert.Equal(adjacent, ResultsCommandTerminal.FindExecutable(app));
    }

    public void Dispose() => temporary.Delete(true);
}
