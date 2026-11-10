using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    internal IEnumerable<WorkspaceItemSnapshot> InspectorSnapshots()
    {
        static string State(WorkspaceOverrideState state) => state.IsReadOnly ? "Fixed · sealed project"
            : state.FixedByFunction?.Invoke() == true ? "Defined by function"
            : state.HasInheritedSource ? "Inherited" : state.IsOverridden || state.IsProjectValue ? "Local" : "Default";
        WorkspaceItemSnapshot Field(string key, string group, WorkspaceOverrideField field) =>
            new(key, group, field.Title, field.IsOverridden && field.EffectiveValue.Length == 0 ? "(empty)" : field.EffectiveValue, State(field), field.Error, field.InheritedSource);
        WorkspaceItemSnapshot List(string key, string group, WorkspaceListOverride state, string value, string error) =>
            new(key, group, state.Title, value, State(state), error, state.InheritedSource);
        WorkspaceItemSnapshot Defaults(bool setup)
        {
            var values = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
            var local = setup ? LocalSetupParameterDefaults : LocalResultsParameterDefaults;
            var localCount = IsReadOnly ? 0 : local.Count();
            var status = IsReadOnly ? "Fixed · sealed project" : IsRun ? "Project defaults" : $"{values.Length - localCount} inherited · {localCount} local";
            return new(setup ? "setupDefaults" : "resultsDefaults", setup ? "Setup" : "Results", "Parameter defaults",
                values.Length == 0 ? "None" : string.Join("; ", values.Select(value => $"{value.keyName} = {value.value}")), status,
                setup ? SetupParameterDefaultsError : ResultsParameterDefaultsError);
        }
        static string Types(IEnumerable<sortopiaDataTypeDto> values) => Summary(values.Select(value => value.value.Length == 0 ? value.kind : $"{value.kind}: {value.value}"));
        static string Spans(IEnumerable<WorkspaceParameterSpan> values) => Summary(values.Select(span => $"{span.Key}: {string.Join(", ", span.Values.Select(value => value.Text))}"));
        if (IsBranch) yield return Field("setupFunction", "Functions", SetupFunctionOverride) with { Error = SetupFunctionError };
        yield return Field("resultsFunction", "Functions", ResultsFunctionOverride) with { Error = ResultsFunctionError };
        yield return Field("description", "General", DescriptionOverride);
        if (!IsModule) yield return Field("resultsDatabase", "Results", ResultsDatabaseOverride);
        if (!IsModule && IsBranch) yield return Field("setupDatabase", "Setup", SetupDatabaseOverride);
        if (!ShowFunctionConfiguration) yield break;
        yield return List("resultsTypes", "Results", OutputTypesOverride, Types(EffectiveOutputs), FirstError(OutputTypesError, ResultsQueryKeysError));
        yield return Defaults(false);
        if (IsRun)
        {
            yield return new("resultsSpans", "Results", "Parameter spans", Spans(ActiveResultsParameters), "Local run inputs", ResultsParametersError);
            yield return new("resultsPreview", "Results", "Generated parameter sets", ResultsParameterSetsPreview.Summary, "Preview", ResultsParameterSetsPreview.Error);
        }
        if (!IsBranch) yield break;


        yield return List("setupOutputs", "Setup", SetupDataTypesOverride,
            Summary(InheritedSetupDataTypes.Select(row => $"{row.DataType.Kind}: {row.DataType.Text} · {(row.RequiresSource ? row.Source?.Name ?? "Source not assigned" : "Generated from parameters")} · keys: {row.QueryKeysSummary}")), FirstError(SetupDataTypesError, SetupQueryKeysError));
        yield return Defaults(true);
        if (IsRun)
        {
            yield return new("setupSpans", "Setup", "Parameter spans", Spans(ActiveSetupParameters), "Local run inputs", SetupParametersError);
            yield return new("setupPreview", "Setup", "Generated parameter sets", SetupParameterSetsPreview.Summary, "Preview", SetupParameterSetsPreview.Error);
        }
    }
}
