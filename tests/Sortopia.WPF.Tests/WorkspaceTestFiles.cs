using System.IO;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.Tests;

internal static class WorkspaceTestFiles
{
    // For typed round-trip assertions on saved drafts. Resolve validated snapshots
    // in memory only; saving a draft must not seal the file on disk.
    internal static workspace LoadRuntimeSnapshot(string root)
    {
        var document = WorkspaceFileStore.LoadDocument(root);
        static ModuleDocument Snapshot(ModuleDocument module) => new(module.Definition,
            module.Modules.Select(Snapshot).ToArray(), module.Projects.Select(value => {
                var parent = project.seal.Invoke(WorkspaceDefinitionCodec.projectHeader(null!, value.Definition));
                var definition = WorkspaceDefinitionCodec.projectWithOrigin(null!, parent);
                definition.runs = value.Definition.runs;
                return new ProjectDocument(definition, value.Runs);
            }).ToArray());
        return WorkspaceDocumentModule.toDomain(new(document.Definition, document.Modules.Select(Snapshot).ToArray()));
    }
    internal static Guid Id(string name) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(name)));

    // Deterministic filenames let fixtures refer to a definition before it exists.
    internal static string DefinitionPath(params string[] parts)
    {
        var folder = Path.Combine(parts[..^1]);
        var kind = parts[^1];
        return WorkspaceDefinitionFiles.Find(folder, kind) ?? Path.Combine(folder, WorkspaceDefinitionFiles.Name(Id(folder + "/" + kind)));
    }
}
