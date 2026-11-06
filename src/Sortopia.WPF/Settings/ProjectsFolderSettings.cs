using System.IO;

namespace Sortopia.WPF.Settings;

/// <summary>Remembers the projects root independently of any open project's runs.</summary>
public sealed class ProjectsFolderSettings
{
    private readonly string filePath;

    public ProjectsFolderSettings(string? filePath = null) => this.filePath = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortopia", "projects-folder.txt");

    public string? Load()
    {
        try { return File.ReadAllText(filePath); }
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
