using System.Diagnostics;
using System.IO;
using System.Text;
using Sortopia.WPF.Settings;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

internal static class ResultsCommandTerminal
{
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

    internal static string FormatBatchCommand(string executable, IEnumerable<string> parameterPaths, ResultsLaunchPreferences preferences)
    {
        preferences.Validate();
        var paths = parameterPaths.ToArray();
        if (paths.Length == 0) return "";
        // The displayed script is also the executed script. A UTF-8 JSON selection
        // avoids Windows' argument limit and preserves literal Unicode paths in PS 5.
        return string.Join(Environment.NewLine, new[] {
            "$sortopiaFiles = @(",
            string.Join(Environment.NewLine, paths.Select(path => "  " + Quote(path))),
            ")",
            "$sortopiaSelection = [System.IO.Path]::GetTempFileName()",
            "try {",
            "  [System.IO.File]::WriteAllText($sortopiaSelection, (ConvertTo-Json -Compress -InputObject $sortopiaFiles), [System.Text.UTF8Encoding]::new($false))",
            $"  & {Quote(executable)} execute-results --parameters-list $sortopiaSelection --cpu-percent {Quote(preferences.CpuPercent)} --memory {Quote(preferences.MemoryLimit)} --jobs {Quote(preferences.ParallelFiles)}",
            "} finally {",
            "  Remove-Item -LiteralPath $sortopiaSelection",
            "}"
        });
    }

    internal static string FindExecutable(string applicationFolder)
    {
        // Published CLI beside the desktop app, or the CLI build in a source checkout.
        var adjacent = Path.Combine(applicationFolder, "sortopia.exe");
        if (File.Exists(adjacent)) return adjacent;
        var configuration = Directory.GetParent(Path.TrimEndingDirectorySeparator(applicationFolder))?.Name;
        for (var folder = new DirectoryInfo(applicationFolder); folder is not null; folder = folder.Parent)
        {
            if (!File.Exists(Path.Combine(folder.FullName, "Sortopia.sln"))) continue;
            var built = Path.Combine(folder.FullName, "src", "Sortopia.Cli", "bin", configuration ?? "Debug", "net10.0", "sortopia.exe");
            if (File.Exists(built)) return built;
            break;
        }
        return "sortopia"; // Resolve an installed CLI using the terminal's PATH/profile.
    }

    internal static void Launch(string command, string workingDirectory) => Launch(command, workingDirectory, start =>
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The terminal could not be started.");
    });

    internal static void Launch(string command, string workingDirectory, Action<ProcessStartInfo> startProcess)
    {
        // A sweep can exceed Windows' process command-line limit. Pass only a short bootstrap;
        // the terminal reads the complete displayed text and removes the temporary file before running it.
        var scriptPath = Path.Combine(Path.GetTempPath(), $"sortopia-results-{Guid.NewGuid():N}.txt");
        File.WriteAllText(scriptPath, command, new UTF8Encoding(false));
        try
        {
            var bootstrap = $"try {{ $sortopiaCommands = [System.IO.File]::ReadAllText({Quote(scriptPath)}) }} "
                + $"finally {{ Remove-Item -LiteralPath {Quote(scriptPath)} }}; & ([scriptblock]::Create($sortopiaCommands))";
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal, WorkingDirectory = workingDirectory
            };
            foreach (var argument in new[] { "-NoLogo", "-NoExit", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(bootstrap)) })
                start.ArgumentList.Add(argument);
            startProcess(start);
        }
        catch
        {
            File.Delete(scriptPath);
            throw;
        }
    }
}
