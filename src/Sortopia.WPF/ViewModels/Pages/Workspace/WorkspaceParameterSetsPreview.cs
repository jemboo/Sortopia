using System.Globalization;
using System.Numerics;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceParameterSetColumn(string Key, string TypeLabel, string Source)
{
    public string Header => $"{Key}\n{TypeLabel} · {Source}";
}

public sealed record WorkspaceParameterSetRow(int Number, parameterSet ParameterSet)
{
    // Formatting belongs to this display boundary; the underlying set keeps its typed bindings.
    public string[] Values => ParameterSet.ParamMap.Values.Select(binding => binding.FormattedValue).ToArray();
}

public sealed record WorkspaceParameterSetsPreview(
    WorkspaceParameterSetColumn[] Columns, WorkspaceParameterSetRow[] Rows, BigInteger TotalCount, string Error)
{
    public const int MaximumRows = 50;
    public static WorkspaceParameterSetsPreview Empty { get; } = new([], [], BigInteger.Zero, "");
    public bool HasRows => Rows.Length > 0;
    public string Summary => Error.Length > 0 ? "Preview unavailable until the parameter errors are corrected."
        : !HasRows ? ""
        : Columns.Length == 0 ? "1 empty parameter set (no defaults or spans)."
        : TotalCount > Rows.Length ? $"Showing the first {Rows.Length} of {TotalCount.ToString("N0", CultureInfo.InvariantCulture)} parameter sets."
        : $"{Rows.Length} parameter set(s). Showing all combinations.";

    internal static WorkspaceParameterSetsPreview Create(parameterBindingDto[] defaults, IEnumerable<WorkspaceParameterSpan> spans, string error)
    {
        var inputs = spans.ToArray();
        var count = inputs.Aggregate(BigInteger.One, (product, span) => product * span.Values.Count);
        if (error.Length > 0) return new([], [], count, error);
        try
        {
            var bindings = ListModule.OfSeq(defaults.Select(ParameterBindingDto.toDomain));
            var candidates = ListModule.OfSeq(inputs.Select(span => ParameterSpan.fromTexts(span.Key, span.ValueType, ListModule.OfSeq(span.Values.Select(value => value.Text)))));
            var sets = ParameterSets.preview(MaximumRows, bindings, candidates);
            var varied = inputs.Select(span => span.Key).ToHashSet(StringComparer.Ordinal);
            var columns = sets[0].ParamMap.Values.Select(binding => new WorkspaceParameterSetColumn(binding.KeyName,
                ParameterValue.label(binding.ValueType), varied.Contains(binding.KeyName) ? "run span" : "default")).ToArray();
            return new(columns, sets.Select((set, index) => new WorkspaceParameterSetRow(index + 1, set)).ToArray(), count, "");
        }
        catch (ArgumentException exception) { return new([], [], count, exception.Message); }
    }
}

/// <summary>Each entity remembers its own expanded sections while navigating the workspace.</summary>
public sealed class WorkspaceEditorSections : ViewModelBase
{
    private bool description = true, resultsSettings = true, resultsDefaults = true, resultsSpans = true, resultsPreview = true;
    private bool setupSettings = true, setupDefaults = true, setupSpans = true, setupPreview = true;
    public bool Description { get => description; set => SetProperty(ref description, value); }
    public bool ResultsSettings { get => resultsSettings; set => SetProperty(ref resultsSettings, value); }
    public bool ResultsDefaults { get => resultsDefaults; set => SetProperty(ref resultsDefaults, value); }
    public bool ResultsSpans { get => resultsSpans; set => SetProperty(ref resultsSpans, value); }
    public bool ResultsPreview { get => resultsPreview; set => SetProperty(ref resultsPreview, value); }
    public bool SetupSettings { get => setupSettings; set => SetProperty(ref setupSettings, value); }
    public bool SetupDefaults { get => setupDefaults; set => SetProperty(ref setupDefaults, value); }
    public bool SetupSpans { get => setupSpans; set => SetProperty(ref setupSpans, value); }
    public bool SetupPreview { get => setupPreview; set => SetProperty(ref setupPreview, value); }
}
