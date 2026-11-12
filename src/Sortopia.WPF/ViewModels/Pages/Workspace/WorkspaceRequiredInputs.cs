using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceRequiredInput(string Key, string TypeLabel, string Supply, string Status, string Detail,
    bool NeedsSpan, bool HasIssue, WorkspaceParameterValueSource? Source = null);

public sealed record WorkspaceRequiredInputs(WorkspaceRequiredInput[] Rows, string Hint)
{
    public int DefaultCount => Rows.Count(row => row.Supply == "Default" && !row.HasIssue);
    public int SpanCount => Rows.Count(row => row.Supply == "Span" && !row.HasIssue);
    public int NeedsSpanCount => Rows.Count(row => row.NeedsSpan);
    public int OtherIssueCount => Rows.Count(row => row.HasIssue && !row.NeedsSpan);
    public string Summary => Rows.Length == 0 ? Hint
        : $"{DefaultCount} supplied by defaults · {SpanCount} supplied by spans · {NeedsSpanCount} need span input"
            + (OtherIssueCount == 0 ? "" : $" · {OtherIssueCount} other issue(s)");
}

public sealed partial class WorkspaceConfigurationViewModel
{
    public WorkspaceRequiredInputs ResultsRequiredInputs => RequiredInputs(false);
    public WorkspaceRequiredInputs SetupRequiredInputs => RequiredInputs(true);

    private WorkspaceRequiredInputs RequiredInputs(bool setup)
    {
        if (!IsRun || setup && !IsBranch) return new([], "");
        if (Requirements(setup) is not { } required)
            return new([], "Required inputs are unavailable for this function version. Check the inherited function.");
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = ActiveParameters(setup).ToArray();
        var rows = required.Select(key =>
        {
            var bindings = defaults.Where(value => value.keyName == key.Name).ToArray();
            var matches = spans.Where(span => span.Key == key.Name).ToArray();
            var type = ParameterValue.label(key.ValueType);
            if (bindings.Length > 0)
            {
                var value = bindings[0];
                var source = ParameterSource(key.Name, setup);
                var origin = new WorkspaceParameterValueSource(source is null ? "Project default" : $"Default from {source.Name}", source);
                if (matches.Length > 0)
                    return new WorkspaceRequiredInput(key.Name, type, "Conflict", "Default/span conflict",
                        "This parameter already has a default. Remove its span.", false, true, origin);
                var error = bindings.Length > 1 ? "Default keys must be unique."
                    : (parameterValueType)value.valueType != key.ValueType ? $"The default must have type {type}."
                    : WorkspaceParameterRules.Error(key.Name, key.ValueType, value.value);
                return new WorkspaceRequiredInput(key.Name, type, "Default", error.Length == 0 ? "Supplied by default" : "Invalid default",
                    error.Length == 0 ? value.value : $"{value.value} · {error}", false, error.Length > 0, origin);
            }
            if (matches.Length == 0)
                return new WorkspaceRequiredInput(key.Name, type, "Missing", "Needs span", "Enter one or more values.", true, true);
            if (matches.Length > 1)
                return new WorkspaceRequiredInput(key.Name, type, "Span", "Duplicate spans", "Keep one span for this parameter.", true, true);
            var span = matches[0];
            return new WorkspaceRequiredInput(key.Name, type, "Span", span.Error.Length == 0 ? "Supplied by span" : "Span needs attention",
                span.Error.Length == 0 ? $"{span.Values.Count} explicitly entered value(s)" : span.Error,
                span.Error.Length > 0, span.Error.Length > 0);
        }).ToArray();
        return new(rows, "Defaults stay fixed. Enter spans only for required parameters without defaults.");
    }
}
