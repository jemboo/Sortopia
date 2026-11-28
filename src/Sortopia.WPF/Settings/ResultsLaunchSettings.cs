using System.IO;
using System.Text.Json;
using Sortopia.FileDb;

namespace Sortopia.WPF.Settings;

public sealed record ResultsLaunchPreferences(string CpuPercent, string MemoryLimit, string ParallelFiles, string HardMemoryLimit = "")
{
    public static ResultsLaunchPreferences Default => new("50", "50%", "Auto");

    public void Validate()
    {
        ExecutionResources.cpuPercent(CpuPercent);
        ExecutionResources.validateMemory(MemoryLimit);
        if (!string.IsNullOrWhiteSpace(HardMemoryLimit)) ExecutionResources.validateMemory(HardMemoryLimit);
        ExecutionResources.parallelJobs(int.MaxValue, ParallelFiles);
    }
}

/// <summary>Machine-local launch preferences, separate from workspace definitions.</summary>
public sealed class ResultsLaunchSettings(string? filePath = null)
{
    private readonly string path = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortopia", "results-launch.json");

    public ResultsLaunchPreferences Load()
    {
        try
        {
            var value = JsonSerializer.Deserialize<ResultsLaunchPreferences>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty launch preferences.");
            value.Validate();
            return value with { HardMemoryLimit = value.HardMemoryLimit ?? "" };
        }
        catch (FileNotFoundException) { return ResultsLaunchPreferences.Default; }
        catch (DirectoryNotFoundException) { return ResultsLaunchPreferences.Default; }
    }

    public void Save(ResultsLaunchPreferences value)
    {
        value.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(staging, JsonSerializer.Serialize(value));
            File.Move(staging, path, overwrite: true);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
}
