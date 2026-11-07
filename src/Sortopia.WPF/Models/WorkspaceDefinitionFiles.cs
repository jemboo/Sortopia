using System.IO;

namespace Sortopia.WPF.Models;

/// <summary>Locates one entity definition, including pre-GUID workspace files.</summary>
public static class WorkspaceDefinitionFiles
{
    public static string NewName(string kind) => $"{kind}-{Guid.NewGuid():D}.msgpack";

    public static bool HasGuid(string name, string kind) =>
        name.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".msgpack", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParseExact(name[(kind.Length + 1)..^8], "D", out _);

    public static string? Find(string folder, string kind)
    {
        if (!Directory.Exists(folder)) return null;
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Linked entity folders are not supported: {folder}");
        var matches = Directory.EnumerateFileSystemEntries(folder)
            .Where(path => string.Equals(Path.GetFileName(path), kind + ".msgpack", StringComparison.OrdinalIgnoreCase)
                || HasGuid(Path.GetFileName(path), kind)).ToArray();
        if (matches.Length > 1) throw new IOException($"Multiple {kind} definition files found in {folder}. Keep one definition in this folder.");
        if (matches.Length == 0) return null;
        var marker = matches[0];
        var attributes = File.GetAttributes(marker);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"Linked definition files are not supported: {marker}");
        if ((attributes & FileAttributes.Directory) != 0) throw new IOException($"A folder occupies a definition file path: {marker}");
        return marker;
    }

    public static string Require(string folder, string kind) => Find(folder, kind)
        ?? throw new FileNotFoundException($"Missing {kind} definition in {folder}.");
}
