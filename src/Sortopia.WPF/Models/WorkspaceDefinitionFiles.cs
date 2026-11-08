using System.IO;
using Sortopia.Core.Serialization;

namespace Sortopia.WPF.Models;

/// <summary>Locates one entity definition, including pre-GUID workspace files.</summary>
public static class WorkspaceDefinitionFiles
{
    public static string Name(Guid id) => $"{id:D}.msgpack";

    public sealed class Header
    {
        public string? workspaceName { get; set; }
        public string? moduleName { get; set; }
        public string? projectName { get; set; }
        public string? runName { get; set; }
    }
    private static bool BareGuid(string name) => name.EndsWith(".msgpack", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParseExact(name[..^8], "D", out _);
    private static bool Matches(string path, string kind)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, kind + ".msgpack", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase) && HasGuid(name, kind)) return true;
        if (!BareGuid(name)) return false;
        CheckFile(path);
        Header header;
        try { header = MessagePackSetup.deserialize<Header>(WorkspaceFileStore.ReadDefinition(path)); }
        catch (MessagePack.MessagePackSerializationException exception)
        { throw new InvalidDataException($"Unreadable entity definition: {path}", exception); }
        if (header is null) throw new InvalidDataException($"Missing entity definition: {path}");
        var actual = header.runName is not null ? "run" : header.projectName is not null ? "project"
            : header.moduleName is not null ? "module" : header.workspaceName is not null ? "workspace" : "";
        if (actual.Length == 0) throw new InvalidDataException($"Unrecognized entity definition: {path}");
        return actual == kind;
    }
    private static void CheckFile(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException($"Linked definition files are not supported: {path}");
        if ((attributes & FileAttributes.Directory) != 0) throw new IOException($"A folder occupies a definition file path: {path}");
    }
    public static string[] All(string folder, string kind)
    {
        if (!Directory.Exists(folder)) return [];
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Linked entity folders are not supported: {folder}");
        var matches = Directory.EnumerateFileSystemEntries(folder).Where(path => Matches(path, kind)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var path in matches) CheckFile(path);
        return matches;
    }

    public static bool HasGuid(string name, string kind) =>
        BareGuid(name) || name.StartsWith(kind + "-", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith(".msgpack", StringComparison.OrdinalIgnoreCase)
        && Guid.TryParseExact(name[(kind.Length + 1)..^8], "D", out _);

    public static string? Find(string folder, string kind)
    {
        var matches = All(folder, kind);
        if (matches.Length > 1) throw new IOException($"Multiple {kind} definition files found in {folder}. Keep one definition in this folder.");
        if (matches.Length == 0) return null;
        return matches[0];
    }

    public static string Require(string folder, string kind) => Find(folder, kind)
        ?? throw new FileNotFoundException($"Missing {kind} definition in {folder}.");
}
