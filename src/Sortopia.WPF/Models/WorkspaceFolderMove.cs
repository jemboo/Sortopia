using System.IO;

namespace Sortopia.WPF.Models;

/// <summary>A folder relocation within one workspace, committed with its definitions.</summary>
public sealed record WorkspaceFolderMove(string SourceFolder, string DestinationFolder)
{
    internal static void CheckPath(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path)) throw new IOException("A moved entity requires an absolute folder path.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (!WorkspaceFolderCopy.Inside(root, path) || string.Equals(root, path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("A moved entity must stay inside its workspace root.");
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Linked paths cannot be moved: {current}");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) break;
        }
    }
}
