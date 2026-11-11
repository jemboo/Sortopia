using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sortopia.Workspaces.Persistence;
using Sortopia.FileDb;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.FileInspector;

public sealed record InspectorFile(inspectionEntry Entry)
{
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public string Kind => Entry.IsLink ? "Link" : IsDirectory ? "Folder" : Path.GetExtension(Name).ToLowerInvariant() switch
    { ".msgpack" => "MessagePack", ".lock" => "Lock", ".tmp" => "Temporary", _ => "File" };
    public string Size => IsDirectory ? "" : FormatSize(Entry.Length);
    public string Modified => Entry.ModifiedUtc.ToLocalTime().ToString("g");
    public static string FormatSize(long bytes) => bytes switch
    { < 1024 => $"{bytes:N0} B", < 1048576 => $"{bytes / 1024d:N1} KiB", < 1073741824 => $"{bytes / 1048576d:N1} MiB", _ => $"{bytes / 1073741824d:N2} GiB" };
}

public sealed record InspectionNode(string Name, string Value, IReadOnlyList<InspectionNode> Children)
{
    public bool IsExpanded => Name is "Captured inherited defaults" or "Local overrides" or "Fixed project configuration";
    public static InspectionNode Leaf(string name, string value) => new(name, value, []);
}

public sealed record InspectorReference(string Label, string FullPath, bool Exists)
{
    public string Status => Exists ? "Open file →" : "Missing file";
}

internal sealed record InspectionReport(string Kind, string Description, IReadOnlyList<InspectionNode> Details,
    IReadOnlyList<InspectorReference> References)
{
    private static string Label(string name) => name switch
    {
        "inheritedDefaults" => "Captured inherited defaults",
        "localOverrides" => "Local overrides",
        "inheritedFrom" => "Captured from module",
        "configuration" => "Fixed project configuration",
        "" => "(empty key)",
        _ => Regex.Replace(char.ToUpperInvariant(name[0]) + name[1..], "(?<=[a-z0-9])([A-Z])", " $1")
    };

    internal static IReadOnlyList<InspectionNode> DecodeDetails(string json)
    {
        if (json.Length == 0) return [];
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var budget = 4000;
        InspectionNode Build(string name, JsonElement value, bool local = false)
        {
            if (--budget < 0) return InspectionNode.Leaf("Display limit", "Additional content omitted.");
            if (value.ValueKind == JsonValueKind.Object)
            {
                var children = new List<InspectionNode>();
                foreach (var property in value.EnumerateObject())
                {
                    children.Add(Build(Label(property.Name), property.Value, local || property.Name == "localOverrides"));
                    if (budget < 0) break;
                }
                return new(name, $"{value.EnumerateObject().Count():N0} fields", children);
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                var children = new List<InspectionNode>();
                var count = value.GetArrayLength();
                foreach (var item in value.EnumerateArray().Take(200))
                {
                    var itemName = $"[{children.Count + 1}]";
                    if (item.ValueKind == JsonValueKind.Object)
                        foreach (var key in new[] { "keyName", "Key", "folderName", "kind", "Kind" })
                            if (item.TryGetProperty(key, out var label) && label.ValueKind == JsonValueKind.String)
                            { itemName = label.GetString()!; break; }
                    children.Add(Build(itemName, item, local));
                    if (budget < 0) break;
                }
                if (count > children.Count) children.Add(InspectionNode.Leaf("Display limit", $"Showing the first {children.Count:N0} of {count:N0} items."));
                return new(name, count == 0 ? "Empty" : $"{count:N0} items", children);
            }
            if (name == "Value Type" && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var type) && Enum.IsDefined(typeof(parameterValueType), type))
                return InspectionNode.Leaf(name, $"{ParameterValue.label((parameterValueType)type)} ({type})");
            var text = value.ValueKind switch
            {
                JsonValueKind.Null when name is "Local overrides" or "Captured inherited defaults" or "Fixed project configuration" => "Not stored for this entity",
                JsonValueKind.Null => local ? "Inherited (no local override)" : "Not set",
                JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : "(empty string)",
                _ => value.ToString()
            };
            if (text.Length > 4096) text = text[..4096] + "… [display truncated]";
            return InspectionNode.Leaf(name, text);
        }
        var root = Build("Contents", document.RootElement);
        return document.RootElement.ValueKind == JsonValueKind.Object ? root.Children : [root];
    }

    internal static InspectionReport Read(string root, InspectorFile file, CancellationToken token)
    {
        var path = WorkspaceFileInspection.checkedPath(root, file.FullPath);
        if (file.IsDirectory) return new("Folder", "Open this folder to browse its files. Only the selected folder is listed; subfolders are loaded on demand.", [], []);
        if (file.Name == ".write.lock") return new("Database writer lock", "Coordinates writers to this project's data store. Its presence does not mean an executor is running. No contents to inspect.", [], []);
        if (Path.GetExtension(path).Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            return new("Temporary file", "An unpublished write or transaction file. It is not a completed data artifact.", [], []);
        if (!Path.GetExtension(path).Equals(".msgpack", StringComparison.OrdinalIgnoreCase))
            return new("Other file", "File metadata is shown. Detailed decoding is available for Sortopia MessagePack definitions and execution files.", [], []);

        var folder = Path.GetDirectoryName(path)!;
        var category = Path.GetFileName(folder);
        dataFileInspection? data = null;
        if (category.Equals("runParameters", StringComparison.OrdinalIgnoreCase))
            data = FileInspection.inspectPrepared(path, token);
        else if (Path.GetFileName(Path.GetDirectoryName(folder)) is { } phase
                 && (phase.Equals("setup", StringComparison.OrdinalIgnoreCase) || phase.Equals("results", StringComparison.OrdinalIgnoreCase)))
            data = FileInspection.inspectData(path, token);
        if (data is not null) return new(data.Kind, data.Description, DecodeDetails(data.Json), []);
        var config = WorkspaceFileInspection.inspectConfiguration(root, path, token);
        var description = config.Description + (config.References.Length > 200 ? $" Showing the first 200 of {config.References.Length:N0} referenced-file links; browse the child folder for the rest." : "");
        return new(config.Kind, description, DecodeDetails(config.Json),
            config.References.Take(200).Select(r => new InspectorReference(r.Label, r.FullPath, r.Exists)).ToArray());
    }
}
