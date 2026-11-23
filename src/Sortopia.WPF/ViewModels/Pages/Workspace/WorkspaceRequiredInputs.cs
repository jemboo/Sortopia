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
        var rows = ParameterStates(setup).Select(state =>
        {
            var origin = state.Defaults.Length == 0 ? null : new WorkspaceParameterValueSource(
                state.DefaultEditor is null ? "Project default" : $"Default from {state.DefaultEditor.Name}", state.DefaultEditor);
            return new WorkspaceRequiredInput(state.Key, ParameterValue.label(state.ValueType),
                state.HasIssue && !state.NeedsSpan ? "Conflict" : state.Supply == WorkspaceParameterSupply.Default ? "Default"
                    : state.Supply == WorkspaceParameterSupply.Span ? "Span" : "Missing",
                state.Status, state.HasIssue ? state.Error : state.Defaults.Length > 0 ? state.Defaults[0].value
                    : $"{state.Spans.Sum(span => span.Values.Count)} explicitly entered value(s)", state.NeedsSpan, state.HasIssue, origin);
        }).ToArray();
        return new(rows, "Enter values for the project's query keys. Other required parameters use fixed defaults.");
    }
}
