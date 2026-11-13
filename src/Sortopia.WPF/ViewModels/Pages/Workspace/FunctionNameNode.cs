namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>A catalog name split into navigable segments; only registered names can be chosen.</summary>
public sealed class FunctionNameNode(string segment, string path) : ViewModelBase
{
    private bool expanded, selected;
    public string Segment { get; } = segment;
    public string Path { get; } = path;
    public bool IsFunction { get; private set; }
    public FunctionNameNode[] Children { get; private set; } = [];
    public bool IsExpanded { get => expanded; set => SetProperty(ref expanded, value); }
    public bool IsSelected { get => selected; set => SetProperty(ref selected, value); }
    public string Hint => IsFunction ? Path : $"Expand {Path} to browse functions.";

    public static FunctionNameNode[] Build(IEnumerable<string> names)
    {
        var roots = new SortedDictionary<string, FunctionNameNode>(StringComparer.Ordinal);
        var children = new Dictionary<FunctionNameNode, SortedDictionary<string, FunctionNameNode>>();
        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.Ordinal))
        {
            var level = roots;
            var path = "";
            var segments = name.Split('.');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                path = index == 0 ? segment : path + "." + segment;
                if (!level.TryGetValue(segment, out var node))
                {
                    node = new(segment, path);
                    level.Add(segment, node);
                    children.Add(node, new(StringComparer.Ordinal));
                }
                if (index == segments.Length - 1) node.IsFunction = true;
                level = children[node];
            }
        }
        foreach (var entry in children) entry.Key.Children = entry.Value.Values.ToArray();
        return roots.Values.ToArray();
    }
}
