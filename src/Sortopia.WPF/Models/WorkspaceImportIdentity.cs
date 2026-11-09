using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.Models;

/// <summary>Allocate the entire imported subtree before remapping its internal project references.</summary>
internal sealed class WorkspaceImportIdentity
{
    private readonly Dictionary<Guid, Guid> replacements = [];
    internal IReadOnlyDictionary<Guid, Guid> OriginalIds => replacements.ToDictionary(pair => pair.Value, pair => pair.Key);
    private void Allocate(Guid id)
    {
        if (!replacements.TryAdd(id, Guid.NewGuid())) throw new ArgumentException($"Duplicate entity ID in import: {id}");
    }
    private void Allocate(projectDto value) { Allocate(value.id); foreach (var run in value.runs) Allocate(run.id); }
    private void Allocate(moduleDto value)
    {
        Allocate(value.id);
        foreach (var child in value.modules) Allocate(child);
        foreach (var child in value.projects) Allocate(child);
    }
    private void Settings(projectSettingsDto value)
    {
        foreach (var entry in value.setupDataTypes ?? []) entry.sourceProjectId = replacements.GetValueOrDefault(entry.sourceProjectId, entry.sourceProjectId);
    }
    private void Rewrite(projectDto value)
    {
        value.id = replacements[value.id];
        Settings(value.configuration); Settings(value.localOverrides);
        foreach (var run in value.runs) { run.id = replacements[run.id]; run.parentProjectId = value.id; }
    }
    private void Rewrite(moduleDto value)
    {
        value.id = replacements[value.id];
        Settings(value.configuration); Settings(value.localOverrides);
        foreach (var child in value.modules) Rewrite(child);
        foreach (var child in value.projects) Rewrite(child);
    }
    internal void Reidentify(projectDto value) { Allocate(value); Rewrite(value); }
    internal void Reidentify(moduleDto value) { Allocate(value); Rewrite(value); }
}
