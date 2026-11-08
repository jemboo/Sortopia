using System.IO;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.Tests;

internal static class WorkspaceTestFiles
{
    internal static Guid Id(string name) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));

    // Deterministic filenames let fixtures refer to a definition before it exists.
    internal static string DefinitionPath(params string[] parts)
    {
        var folder = Path.Combine(parts[..^1]);
        var kind = parts[^1];
        return WorkspaceDefinitionFiles.Find(folder, kind) ?? Path.Combine(folder, WorkspaceDefinitionFiles.Name(Id(folder + "/" + kind)));
    }
}
