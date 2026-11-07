using System.IO;

namespace Sortopia.WPF.Settings;

/// <summary>Remembers the workspace root independently of any open workspace's runs.</summary>
public sealed class WorkspaceFolderSettings
{
    private readonly string filePath;
    private readonly string legacyFilePath;

    public WorkspaceFolderSettings(string? filePath = null)
    {
        this.filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortopia", "workspace-folder.txt");
        // Keep an existing folder selection readable until it is saved under the new setting name.
        legacyFilePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(this.filePath))!, "projects-folder.txt");
    }

    public string? Load() => ReadIfPresent(filePath) ?? ReadIfPresent(legacyFilePath);

    private static string? ReadIfPresent(string path)
    {
        try { return File.ReadAllText(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Save(string folder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, folder);
            File.Move(temporaryPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
