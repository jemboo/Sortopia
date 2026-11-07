using System.IO;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.Tests;

internal static class WorkspaceTestFiles
{
    // Locate saved GUID files; use a legacy filename when a fixture has not been written yet.
    internal static string DefinitionPath(params string[] parts)
    {
        var folder = Path.Combine(parts[..^1]);
        var kind = parts[^1];
        return WorkspaceDefinitionFiles.Find(folder, kind) ?? Path.Combine(folder, kind + ".msgpack");
    }
}
