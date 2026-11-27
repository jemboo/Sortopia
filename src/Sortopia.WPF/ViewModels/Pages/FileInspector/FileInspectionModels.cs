using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sortopia.Workspaces.Persistence;
using Sortopia.FileDb;
using Sortopia.Runs;
using Sortopia.Evaluation.Sgd;

namespace Sortopia.WPF.ViewModels.Pages.FileInspector;

public sealed class InspectorFile(inspectionEntry entry) : INotifyPropertyChanged
{
    public inspectionEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string FullPath => Entry.FullPath;
    public bool IsDirectory => Entry.IsDirectory;
    public string Kind => Entry.IsLink ? "Link" : IsDirectory ? "Folder" : Path.GetExtension(Name).ToLowerInvariant() switch
    { ".msgpack" => "MessagePack", ".lock" => "Lock", ".tmp" => "Temporary", _ => "File" };
    public string Size => IsDirectory ? "" : FormatSize(Entry.Length);
    public DateTime Created => Entry.CreatedUtc.ToLocalTime();
    public string Modified => Entry.ModifiedUtc.ToLocalTime().ToString("g");
    internal Dictionary<InspectorQueryKey, parameterBinding> QueryParameters { get; } = [];
    public InspectorQueryCell[] QueryCells { get; private set; } = [];
    public int? GenerationCurrent { get; private set; }
    public bool IsEvolution { get; private set; }
    public string QueryError { get; private set; } = "";
    public string ToolTip => QueryError.Length == 0 ? FullPath : $"{FullPath}{Environment.NewLine}{QueryError}";

    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetColumns(IEnumerable<InspectorQueryKey> keys)
    {
        QueryCells = keys.Select(key => QueryParameters.TryGetValue(key, out var binding)
            ? new InspectorQueryCell(binding.FormattedValue, binding.Value)
            : new InspectorQueryCell(QueryError.Length > 0 ? "Unavailable" : "—", null)).ToArray();
        PropertyChanged?.Invoke(this, new(nameof(QueryCells)));
    }

    internal static InspectorFile Read(string root, inspectionEntry entry, CancellationToken token)
    {
        var file = new InspectorFile(entry);
        token.ThrowIfCancellationRequested();
        if (entry.IsDirectory || entry.IsLink || !Path.GetExtension(entry.Name).Equals(".msgpack", StringComparison.OrdinalIgnoreCase)) return file;
        var folder = Path.GetDirectoryName(entry.FullPath)!;
        var category = Path.GetFileName(folder);
        var phase = Path.GetFileName(Path.GetDirectoryName(folder));
        try
        {
            void Add(sortopiaDataType type, parameterBinding binding) => file.QueryParameters[new(type, binding.KeyName)] = binding;
            if (category.Equals("runParameters", StringComparison.OrdinalIgnoreCase))
            {
                WorkspaceFileInspection.checkedPath(root, entry.FullPath);
                var item = RunParameterFiles.loadFile(entry.FullPath);
                foreach (var output in item.Results.Outputs)
                    foreach (var key in output.QueryParameterKeys)
                        if (item.Parameters.TryFind(key) is { } binding) Add(output.DataType, binding.Value);
                file.IsEvolution = item.Sgd is not null;
                if (file.IsEvolution) file.GenerationCurrent = RunOutputFiles.completedGeneration(Path.GetDirectoryName(folder)!, item, token)?.Value;
            }
            else if (phase?.Equals("results", StringComparison.OrdinalIgnoreCase) == true || phase?.Equals("setup", StringComparison.OrdinalIgnoreCase) == true)
            {
                WorkspaceFileInspection.checkedPath(root, entry.FullPath);
                if (category.Equals("temporarySnapshots", StringComparison.OrdinalIgnoreCase))
                {
                    file.IsEvolution = true;
                    var data = FileInspection.inspectTemporary(entry.FullPath, token);
                    if (data.Json.Length > 0)
                    {
                        using var json = JsonDocument.Parse(data.Json);
                        var value = json.RootElement.GetProperty("Generation");
                        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var generation)) file.GenerationCurrent = generation;
                    }
                }
                else
                {
                    var query = FileInspection.readDataQuery(entry.FullPath, token);
                    foreach (var binding in query.Parameters)
                    {
                        if (binding.KeyName == "GenerationCurrent")
                        {
                            file.IsEvolution = true;
                            file.GenerationCurrent = ((parameterValue.IntegerValue)binding.Value).Item;
                        }
                        else Add(query.DataType, binding);
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { file.QueryError = $"Query parameters unavailable: {e.GetBaseException().Message}"; }
        return file;
    }
    public static string FormatSize(long bytes) => bytes switch
    { < 1024 => $"{bytes:N0} B", < 1048576 => $"{bytes / 1024d:N1} KiB", < 1073741824 => $"{bytes / 1048576d:N1} MiB", _ => $"{bytes / 1073741824d:N2} GiB" };
}

internal sealed record InspectorQueryKey(sortopiaDataType DataType, string Name)
{
    internal string DataTypeLabel => SortopiaDataType.kindName(DataType)
        + (SortopiaDataType.text(DataType) is { Length: > 0 } label ? $" · {label}" : "");
}
public sealed record InspectorQueryCell(string Text, parameterValue? SortValue);

public sealed record InspectionNode(string Name, string Value, IReadOnlyList<InspectionNode> Children)
{
    public bool IsExpanded => Name is "Captured inherited defaults" or "Local overrides" or "Fixed project configuration"
        or "Results spans" or "Setup spans" or "Invocation parameters" or "Setup query parameters" or "Query Parameters" or "Parameters";
    public static InspectionNode Leaf(string name, string value) => new(name, value, []);
}

public sealed record InspectorReference(string Label, string FullPath, bool Exists)
{
    public string Status => Exists ? "Open file →" : "Missing file";
}

internal sealed record InspectionReport(string Kind, string Description, IReadOnlyList<InspectionNode> Details,
    IReadOnlyList<InspectorReference> References, sorterPoolSummarySet? PoolSummary = null, string Error = "", SummaryHistorySource? HistorySource = null,
    bool SummaryOnly = false)
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
                if (value.TryGetProperty("Key", out var key) && value.TryGetProperty("Value", out var formatted)
                    && value.TryGetProperty("Type", out var typeName))
                    return InspectionNode.Leaf(key.GetString()!, $"{formatted.GetString()} ({typeName.GetString()})");
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
                        foreach (var key in new[] { "keyName", "Key", "name", "kind", "Kind" })
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

    private static InspectionReport ReadParameterHistory(string path, dataFileInspection data, CancellationToken token)
    {
        var details = DecodeDetails(data.Json);
        SummaryHistorySource? source = null;
        try
        {
            var item = RunParameterFiles.loadFile(path);
            if (item.Sgd is null) return new(data.Kind, data.Description, details, []);
            var project = RunParameterFiles.projectFolder(Path.GetDirectoryName(path)!);
            var store = new SortopiaFileDb(project, item.ProjectId);
            source = new(project, RunOutputFiles.samples(item).Summaries.Select(batch => store.GetPath(batch.Item2.Reference))
                .Append(path).Append(Path.Combine(project, "runs", $"{item.RunId:D}.msgpack")).ToArray());
            var history = RunOutputFiles.loadSummaryHistory(project, item, token);
            if (history is null)
                return new(data.Kind, data.Description + " No pool summary sets have been saved yet. Published summaries appear automatically while this page is open.", details, [], HistorySource: source);
            var saved = history.Value;
            var generations = saved.Summary.Rows.Select(row => row.Generation).Distinct().ToArray();
            var gaps = saved.MissingGenerations.Length == 0 ? "" : $" {saved.MissingGenerations.Length:N0} scheduled generations before the latest sample are missing; playback skips those gaps.";
            var description = $"Evolution history for this parameter file: {generations.Length:N0} saved generations ({generations[0]:N0}–{generations[^1]:N0}) from {saved.Files.Length:N0} summary sets. Select a population and generation, or play the full sequence. New summaries appear automatically while this page is open." + gaps;
            var links = saved.Files.Take(200).Select(batch => new InspectorReference(
                batch.Item1.Length == 1 ? $"Summary · generation {batch.Item1[0]:N0}" : $"Summary · generations {batch.Item1[0]:N0}–{batch.Item1[^1]:N0}", batch.Item2, true)).ToArray();
            if (saved.Files.Length > links.Length) description += $" Showing the first {links.Length:N0} summary-file links.";
            return new(data.Kind, description, details, links, saved.Summary, HistorySource: source);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e)
        {
            return new(data.Kind, data.Description + " Evolution history could not be loaded.", details, [],
                Error: $"Cannot load evolution history: {e.Message}", HistorySource: source);
        }
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
        else if (category.Equals("temporarySnapshots", StringComparison.OrdinalIgnoreCase))
            data = FileInspection.inspectTemporary(path, token);
        else if (Path.GetFileName(Path.GetDirectoryName(folder)) is { } phase
                 && (phase.Equals("setup", StringComparison.OrdinalIgnoreCase) || phase.Equals("results", StringComparison.OrdinalIgnoreCase)))
            data = FileInspection.inspectData(path, token);
        if (data is not null)
        {
            if (category.Equals("runParameters", StringComparison.OrdinalIgnoreCase) && data.Kind == "Run parameters")
                return ReadParameterHistory(path, data, token);
            var summary = category.Equals("SorterPoolSummarySet", StringComparison.OrdinalIgnoreCase)
                ? FileInspection.loadPoolSummary(path, token) : null;
            return new(data.Kind, summary is null ? data.Description
                : "Saved pool histograms for the generations in this file. Select a population and generation, or play the sequence. Payload identity, checksum and summary contents validated.",
                DecodeDetails(data.Json), [], summary, SummaryOnly: summary is not null);
        }
        var config = WorkspaceFileInspection.inspectConfiguration(root, path, token);
        var description = config.Description + (config.References.Length > 200 ? $" Showing the first 200 of {config.References.Length:N0} referenced-file links; browse the child folder for the rest." : "");
        return new(config.Kind, description, DecodeDetails(config.Json),
            config.References.Take(200).Select(r => new InspectorReference(r.Label, r.FullPath, r.Exists)).ToArray());
    }
}
