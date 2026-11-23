using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

internal sealed record WorkspaceParameterState(string Key, parameterValueType ValueType, bool IsQuery,
    parameterBindingDto[] Defaults, WorkspaceParameterSpan[] Spans, parameterIssue[] Issues,
    string ValueError, WorkspaceConfigurationViewModel? DefaultEditor)
{
    public bool HasIssue => Issues.Length > 0 || ValueError.Length > 0;
    public bool NeedsSpan => IsQuery && Defaults.Length == 0 && HasIssue;
    public WorkspaceParameterSupply Supply => Defaults.Length > 0 ? Defaults.All(row => string.IsNullOrWhiteSpace(row.value)) && ValueError.Length > 0
        ? WorkspaceParameterSupply.None : WorkspaceParameterSupply.Default
        : Spans.Any(span => span.Values.Count > 0) ? WorkspaceParameterSupply.Span : WorkspaceParameterSupply.None;
    public string Error => Issues.FirstOrDefault()?.Message ?? ValueError;
    public string Status => Issues.FirstOrDefault()?.Kind switch
    {
        parameterIssueKind.UnexpectedSpan => "Undeclared span",
        parameterIssueKind.DefaultSpanConflict => "Default/span conflict",
        parameterIssueKind.MissingDefault => "Needs default",
        parameterIssueKind.MissingSpan => "Needs span",
        parameterIssueKind.EmptySpan => "Needs span",
        parameterIssueKind.DuplicateSpan => "Duplicate spans",
        _ => Defaults.Length > 0 ? HasIssue ? "Invalid default" : "Supplied by default"
            : HasIssue ? "Span needs attention" : "Supplied by span"
    };
}

public sealed partial class WorkspaceConfigurationViewModel
{
    internal phaseParameterContract PhaseContract(bool setup)
    {
        var outputs = setup ? EffectiveSetupDataTypes.Select(row => row.queryParameterKeys) : EffectiveResultsDataTypes.Select(row => row.queryParameterKeys);
        var required = Requirements(setup)?.Select(key => key.Name)
            ?? outputs.SelectMany(keys => keys).Concat((setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(row => row.keyName));
        return PhaseParameterContract.create(required, outputs);
    }

    private parameterIssue[] PhaseIssues(bool setup)
    {
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = IsRun ? ParameterSpans(setup).ToArray() : [];
        return PhaseParameterContract.keyIssues(PhaseContract(setup), defaults.Select(row => row.keyName), spans.Select(row => row.Key))
            .Where(issue => IsRun || issue.Kind != parameterIssueKind.MissingSpan)
            .Concat(spans.SelectMany(span => PhaseParameterContract.spanValuesIssues(span.Key, span.Values.Count))).ToArray();
    }

    private WorkspaceParameterState[] ParameterStates(bool setup)
    {
        var contract = PhaseContract(setup);
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = IsRun ? ParameterSpans(setup).ToArray() : [];
        var issues = PhaseIssues(setup);
        return (FunctionGridRequirements(setup) ?? []).Select(key =>
        {
            var bindings = defaults.Where(row => row.keyName == key.Name).ToArray();
            var matches = spans.Where(row => row.Key == key.Name).ToArray();
            var valueError = bindings.Select(row => (parameterValueType)row.valueType != key.ValueType
                ? $"The default must have type {ParameterValue.label(key.ValueType)}."
                : WorkspaceParameterRules.Error(ParameterSupport(setup), key.Name, key.ValueType, row.value))
                .Concat(matches.SelectMany(row => row.Values).Select(value => value.Error)).FirstOrDefault(error => error.Length > 0) ?? "";
            return new WorkspaceParameterState(key.Name, key.ValueType, contract.SpanKeys.Contains(key.Name), bindings, matches,
                issues.Where(issue => issue.Key == key.Name).ToArray(), valueError, ParameterSource(key.Name, setup));
        }).ToArray();
    }
}
