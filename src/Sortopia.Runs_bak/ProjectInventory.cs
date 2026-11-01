using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Sortopia.Sorting.Catalog;

namespace Sortopia.Runs;

public sealed record InventorySelection(string OutputDataType, Guid DataId, int Order);

/// <summary>CSV inventories and manifests share the GeneSort Data/Project directory as their path base.</summary>
public sealed class ProjectInventory
{
    public string ProjectDirectory { get; }
    public string InventoryPath => Path.Combine(ProjectDirectory, "Inventory.csv");
    public string ManifestsDirectory => Path.Combine(ProjectDirectory, "Manifests");
    private string ResultsDirectory => Path.Combine(Path.GetDirectoryName(ProjectDirectory)!, "Results");

    public ProjectInventory(string folder) => ProjectDirectory = FindProjectDirectory(folder)
        ?? throw new DirectoryNotFoundException("Choose a GeneSort project containing Data/Project and Data/Results.");

    public static string? FindProjectDirectory(string folder)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        foreach (string candidate in new[] { path, Path.Combine(path, "Project"), Path.Combine(path, "Data", "Project") })
            if (string.Equals(Path.GetFileName(candidate), "Project", StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(candidate) && Directory.Exists(Path.Combine(Path.GetDirectoryName(candidate)!, "Results")))
                return candidate;
        return null;
    }

    public DataTable Open() => File.Exists(InventoryPath) ? ReadInventory() : Refresh();

    public DataTable Refresh()
    {
        const string type = nameof(OutputDataType.SortableTestsWithPrefix);
        string root = Path.Combine(ResultsDirectory, type);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"No {type} Result folder at {root}.");
        var rows = Table(["OutputDataType", "ChannelCount", "PrefixStageCount", "PrefixVariant", "PrefixCeCount",
            "TestCount", "SortableDataFormat", "Repl", "DataId", "PrefixLibId", "RelativePath"]);
        foreach (string path in Directory.EnumerateFiles(root, "*.msgpack", System.IO.SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            try
            {
                var data = GeneSortResultMetadata.Read(path);
                var key = data.CatalogPrefix;
                string replica = Path.GetRelativePath(root, Path.GetDirectoryName(path)!);
                if (replica != "." && (!int.TryParse(replica, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                    throw new InvalidDataException("Expected a numeric replica folder.");
                rows.Rows.Add(type, data.Channels, key?.StageLength.ToString(CultureInfo.InvariantCulture),
                    key == null ? "" : PrefixLibVariant.toString(key.PrefixLibVariant), data.PrefixCeCount,
                    data.Count, data.Format, replica == "." ? "" : replica, data.Id.ToString(),
                    key == null ? "" : PrefixLibId.toString(key), Path.GetRelativePath(ProjectDirectory, path));
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            { throw new InvalidDataException($"Cannot inventory {path}. {e.Message}", e); }
        }
        Validate(rows);
        Write(InventoryPath, rows, replace: true);
        return rows;
    }

    public DataTable ReadInventory()
    {
        using var parser = new TextFieldParser(InventoryPath, Encoding.UTF8) {
            TextFieldType = FieldType.Delimited, TrimWhiteSpace = false, HasFieldsEnclosedInQuotes = true
        };
        parser.SetDelimiters(",");
        var table = Table(parser.ReadFields() ?? throw new InvalidDataException("The inventory has no header."));
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            if (fields.Length != table.Columns.Count) throw new InvalidDataException("Inventory row does not match the header.");
            table.Rows.Add(fields);
        }
        Validate(table);
        return table;
    }

    public string SaveManifest(string name, IEnumerable<InventorySelection> selection)
    {
        name = name.Trim();
        if (name.Length == 0 || name is "." or ".." || name.EndsWith('.') ||
            name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Enter a manifest filename without directory or special characters.");
        var entries = selection.OrderBy(s => s.OutputDataType, StringComparer.Ordinal).ThenBy(s => s.Order).ToArray();
        if (entries.Length == 0) throw new ArgumentException("Include at least one Result.");
        var inventory = ReadInventory();
        var contents = inventory.Rows.Cast<DataRow>().ToDictionary(r => ((string)r["OutputDataType"], Guid.Parse((string)r["DataId"])));
        string[] columns = inventory.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToArray();
        var manifest = Table(["Index", .. columns]);
        foreach (var group in entries.GroupBy(s => s.OutputDataType))
        {
            var usedIds = new HashSet<Guid>();
            var usedOrders = new HashSet<int>();
            foreach (var entry in group)
            {
                if (!usedIds.Add(entry.DataId) || entry.Order < 0 || !usedOrders.Add(entry.Order))
                    throw new ArgumentException($"Each selected {group.Key} needs its own nonnegative order.");
                if (!contents.TryGetValue((group.Key, entry.DataId), out var row))
                    throw new InvalidDataException($"Result {entry.DataId} is absent from the current {group.Key} inventory.");
                var file = Path.GetFullPath((string)row["RelativePath"], ProjectDirectory);
                if (!File.Exists(file)) throw new FileNotFoundException("The inventoried Result is missing.", file);
                manifest.Rows.Add([usedIds.Count - 1, .. row.ItemArray]);
            }
        }
        Directory.CreateDirectory(ManifestsDirectory);
        var path = Path.Combine(ManifestsDirectory, name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? name : name + ".csv");
        Write(path, manifest, replace: false);
        return path;
    }

    private static DataTable Table(string[] names)
    {
        var table = new DataTable { Locale = CultureInfo.InvariantCulture };
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name) || table.Columns.Contains(name))
                throw new InvalidDataException("CSV columns must have distinct names.");
            table.Columns.Add(name, typeof(string));
        }
        return table;
    }

    private void Validate(DataTable table)
    {
        foreach (string col in new[] { "OutputDataType", "DataId", "RelativePath" })
            if (!table.Columns.Contains(col)) throw new InvalidDataException($"Inventory column {col} is missing.");
        if (new[] { "Include", "Order", "Index" }.Any(table.Columns.Contains))
            throw new InvalidDataException("Inventory columns Include, Order and Index are reserved for manifest selection.");
        var keys = new HashSet<(string, Guid)>();
        foreach (DataRow row in table.Rows)
        {
            var type = (string)row["OutputDataType"];
            if (string.IsNullOrWhiteSpace(type) || !Guid.TryParse(row["DataId"].ToString(), out var id) || !keys.Add((type, id)))
                throw new InvalidDataException("Every Result needs an output type and a unique dataset ID within that type.");
            var path = (string)row["RelativePath"];
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                !Path.GetFullPath(path, ProjectDirectory).StartsWith(ResultsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Result location must be project-relative and inside Results: {path}");
        }
    }

    private static void Write(string path, DataTable table, bool replace)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        static string Field(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        try
        {
            using (var writer = new StreamWriter(temp, false, new UTF8Encoding(true)) { NewLine = "\r\n" })
            {
                writer.WriteLine(string.Join(",", table.Columns.Cast<DataColumn>().Select(c => Field(c.ColumnName))));
                foreach (DataRow row in table.Rows)
                    writer.WriteLine(string.Join(",", row.ItemArray.Select(v => Field(v?.ToString()))));
            }
            File.Move(temp, path, replace);
        }
        finally { File.Delete(temp); }
    }
}
