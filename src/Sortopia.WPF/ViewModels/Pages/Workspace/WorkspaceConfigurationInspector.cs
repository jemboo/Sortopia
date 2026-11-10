using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    internal IEnumerable<WorkspaceItemSnapshot> InspectorSnapshots()
    {
        static string State(WorkspaceOverrideState state) => state.IsReadOnly ? "Fixed · sealed project"
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
        yield return Field("description", "General", DescriptionOverride);
        if (!IsModule) yield return Field("resultsDatabase", "Results", ResultsDatabaseOverride);
        yield return Field("resultsFunction", "Results", ResultsFunctionOverride);
        yield return List("resultsTypes", "Results", OutputTypesOverride, Types(EffectiveOutputs), OutputTypesError);
        yield return List("resultsQueryKeys", "Results", ResultsQueryKeysOverride, Summary(EffectiveOutputKeys), ResultsQueryKeysError);
        yield return Defaults(false);
        if (IsRun)
        {
            yield return new("resultsSpans", "Results", "Parameter spans", Spans(ResultsParameters), "Local run inputs", ResultsParametersError);
            yield return new("resultsPreview", "Results", "Generated parameter sets", ResultsParameterSetsPreview.Summary, "Preview", ResultsParameterSetsPreview.Error);
        }
        if (!IsBranch) yield break;
        if (!IsModule) yield return Field("setupDatabase", "Setup", SetupDatabaseOverride);
        yield return Field("setupFunction", "Setup", SetupFunctionOverride);
        yield return List("setupOutputs", "Setup", SetupDataTypesOverride,
            Summary(InheritedSetupDataTypes.Select(row => $"{row.DataType.Kind}: {row.DataType.Text} · {row.Source?.Name ?? "Missing source"} · keys: {row.QueryKeysSummary}")), SetupDataTypesError);
        yield return Defaults(true);
        if (IsRun)
        {
            yield return new("setupSpans", "Setup", "Parameter spans", Spans(SetupParameters), "Local run inputs", SetupParametersError);
            yield return new("setupPreview", "Setup", "Generated parameter sets", SetupParameterSetsPreview.Summary, "Preview", SetupParameterSetsPreview.Error);
        }
    }
}
