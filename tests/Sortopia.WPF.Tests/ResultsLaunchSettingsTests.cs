using System.IO;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsLaunchSettingsTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-launch-settings-");

    [Fact]
    public void Valid_limits_are_remembered_and_invalid_edits_do_not_replace_them()
    {
        var settings = new ResultsLaunchSettings(Path.Combine(temporary.FullName, "launch.json"));
        using (var page = new ResultsExecutionPageViewModel(() => { }, _ => null, launchSettings: settings))
        {
            Assert.Equal("50", page.CpuPercent); Assert.Equal("50%", page.MemoryLimit); Assert.Equal("Auto", page.ParallelFiles);
            page.CpuPercent = "30"; page.MemoryLimit = "2.5GiB"; page.ParallelFiles = "3";
            Assert.Empty(page.LaunchLimitsError);
            page.CpuPercent = "101";
            Assert.NotEmpty(page.LaunchLimitsError); Assert.False(page.CanExecuteCommandLine); Assert.Empty(page.CommandLine);
        }
        using var reopened = new ResultsExecutionPageViewModel(() => { }, _ => null, launchSettings: settings);
        Assert.Equal("30", reopened.CpuPercent); Assert.Equal("2.5GiB", reopened.MemoryLimit); Assert.Equal("3", reopened.ParallelFiles);
        reopened.MemoryLimit = "-1"; Assert.NotEmpty(reopened.LaunchLimitsError);
        reopened.MemoryLimit = "40%"; Assert.Empty(reopened.LaunchLimitsError);
        reopened.ParallelFiles = "0"; Assert.NotEmpty(reopened.LaunchLimitsError);
        Assert.Equal(new("30", "40%", "3"), settings.Load());
    }

    [Fact]
    public void Hard_cap_is_optional_and_remembered_separately_from_the_target()
    {
        var path = Path.Combine(temporary.FullName, "legacy.json");
        File.WriteAllText(path, """{"CpuPercent":"30","MemoryLimit":"2GiB","ParallelFiles":"3"}""");
        var settings = new ResultsLaunchSettings(path);
        using (var page = new ResultsExecutionPageViewModel(() => { }, _ => null, launchSettings: settings))
        {
            Assert.Empty(page.HardMemoryLimit);
            page.HardMemoryLimit = "4GiB";
            Assert.Empty(page.LaunchLimitsError);
        }
        using var reopened = new ResultsExecutionPageViewModel(() => { }, _ => null, launchSettings: settings);
        Assert.Equal("2GiB", reopened.MemoryLimit);
        Assert.Equal("4GiB", reopened.HardMemoryLimit);
        reopened.HardMemoryLimit = "bad";
        Assert.NotEmpty(reopened.LaunchLimitsError);
        Assert.Equal("4GiB", settings.Load().HardMemoryLimit);
        reopened.HardMemoryLimit = "";
        Assert.Empty(reopened.LaunchLimitsError);
        Assert.Empty(settings.Load().HardMemoryLimit);
    }

    [Fact]
    public void Malformed_preferences_fall_back_to_safe_defaults_with_an_explanation()
    {
        var path = Path.Combine(temporary.FullName, "launch.json"); File.WriteAllText(path, "broken");
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null, launchSettings: new(path));
        Assert.Equal("50", page.CpuPercent); Assert.Equal("50%", page.MemoryLimit);
        Assert.Contains("using defaults", page.Error);
    }

    public void Dispose() => temporary.Delete(true);
}
