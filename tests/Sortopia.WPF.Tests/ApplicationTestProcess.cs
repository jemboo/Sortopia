using System.Diagnostics;
using Xunit;

namespace Sortopia.WPF.Tests;

internal static class ApplicationTestProcess
{
    internal static async Task RunAsync(Type testClass, string method, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(testClass.Assembly.Location);
        start.ArgumentList.Add("--progress"); start.ArgumentList.Add("off");
        start.ArgumentList.Add("--filter-class"); start.ArgumentList.Add(testClass.FullName!);
        start.ArgumentList.Add("--filter-method"); start.ArgumentList.Add("*" + method);
        start.Environment["SORTOPIA_APPLICATION_TEST"] = method;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the application test.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        var log = await output + await error;
        Assert.True(process.ExitCode == 0, log);
        Assert.Contains("total: 1", log);
        Assert.DoesNotContain("Foreground threads were left running", log);
    }
}
