using System.IO;
using System.Text.Json;
using Sortopia.FileDb;
using Sortopia.Runs;
using Sortopia.WPF.ViewModels.Shared;
using Sortopia.Workspaces.Persistence;

namespace Sortopia.WPF.ViewModels.Pages.FileInspector;

public sealed record InspectorRunContext(string Name, ProducingRun? Target, IReadOnlyList<InspectionNode> Details)
{
    internal static IReadOnlyList<InspectorRunContext> Read(string path, CancellationToken token)
    {
        var parent = Path.GetDirectoryName(path)!;
        var category = Path.GetFileName(parent);
        if (category.Equals("runs", StringComparison.OrdinalIgnoreCase))
            return [ReadRun(Path.GetDirectoryName(parent)!, path, [], token)];
        var phase = Path.GetFileName(Path.GetDirectoryName(parent));
        if (!category.Equals("runParameters", StringComparison.OrdinalIgnoreCase) && phase is not ("results" or "setup")) return [];
        var project = category.Equals("runParameters", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(parent)! : Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(parent)))!;
        return FileInspection.producingParameters(path, token).GroupBy(item => item.RunId)
            .Select(group => ReadRun(project, Path.Combine(project, "runs", $"{group.Key:D}.msgpack"), group.ToArray(), token)).ToArray();
    }

    private static InspectorRunContext ReadRun(string project, string path, runParameters[] invocations, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var details = new List<InspectionNode>();
        var runId = Guid.Parse(Path.GetFileNameWithoutExtension(path));
        var projectId = invocations.FirstOrDefault()?.ProjectId ?? Guid.Empty;
        var name = runId.ToString();
        if (File.Exists(path))
        {
            var saved = WorkspaceFileInspection.inspectConfiguration(project, path, token);
            using var json = JsonDocument.Parse(saved.Json);
            name = json.RootElement.GetProperty("runName").GetString()!;
            projectId = json.RootElement.GetProperty("parentProjectId").GetGuid();
            foreach (var (key, label) in new[] { ("resultsParameterSpans", "Results spans"), ("setupParameterSpans", "Setup spans") })
            {
                var spans = json.RootElement.GetProperty(key).EnumerateArray().Select(span =>
                    InspectionNode.Leaf(span.GetProperty("keyName").GetString()!, string.Join(", ", span.GetProperty("values").EnumerateArray().Select(value => value.GetString())))).ToArray();
                details.Add(new(label, spans.Length == 0 ? "No spans" : $"{spans.Length} span(s)", spans));
            }
            details.Add(new("Saved run configuration", "Current saved spans and reporting settings", InspectionReport.DecodeDetails(saved.Json)));
        }
        else details.Add(InspectionNode.Leaf("Run definition", "No longer present in this project."));
        var savedSetup = new SavedSetupQueries(project);
        foreach (var item in invocations)
        {
            details.Add(new("Invocation parameters", $"Set {item.SetNumber} · {item.Results.FunctionName} · {item.Id}",
                item.Parameters.ParamMap.Values.Select(binding => InspectionNode.Leaf(binding.KeyName, binding.FormattedValue)).ToArray()));
            foreach (var input in savedSetup.Read(item, token).Queries.Inputs)
                details.Add(new("Setup query parameters", $"{SortopiaDataType.kindName(input.Key)} · {SortopiaDataType.text(input.Key)}",
                    input.Value is null ? [InspectionNode.Leaf("Unavailable", "The saved setup query could not be read.")]
                        : input.Value.Select(binding => InspectionNode.Leaf(binding.KeyName, binding.FormattedValue)).ToArray()));
        }
        var target = ProducingRun.Find(project, projectId, runId);
        var location = target is null ? project : Path.GetRelativePath(target.WorkspaceFolder, project);
        return new($"{location} / {name}", target, details);
    }
}
