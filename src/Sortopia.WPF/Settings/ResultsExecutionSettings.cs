using System.Globalization;
using System.IO;

namespace Sortopia.WPF.Settings;

/// <summary>Machine preferences, independent of portable project configuration.</summary>
public sealed class ResultsExecutionSettings(string? filePath = null)
{
    private readonly string path = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortopia", "parallel-jobs.txt");

    public int Load()
    {
        try { return int.TryParse(File.ReadAllText(path), out var count) && count > 0 ? count : 1; }
        catch (IOException) { return 1; }
        catch (UnauthorizedAccessException) { return 1; }
    }

    public void Save(int count)
    {
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, count.ToString(CultureInfo.InvariantCulture));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
