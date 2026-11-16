using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    internal IEnumerable<WorkspaceItemSnapshot> InspectorSnapshots()
    {
        static string State(WorkspaceOverrideState state) => state.IsReadOnly ? "Locked · generated parameters"
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
            var status = IsReadOnly ? "Locked · generated parameters" : IsRun ? "Project defaults" : $"{values.Length - localCount} inherited · {localCount} local";
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
        if (HasResultsContract)
        {
            yield return List("resultsTypes", "Results", OutputTypesOverride, IsModule ? Types(EffectiveOutputs)
                : Summary(ResultsQueryOutputs.Select(output => output.InspectorSummary)), FirstError(OutputTypesError, ResultsQueryKeysError))
                with { Title = IsModule ? "Results data types" : "Outputs and query keys", Status = IsModule ? "Defined by function" : IsRun ? "Inherited from project" : CanEditQuerySelection ? "Project query choices" : "Locked · generated parameters", Source = IsRun ? Parent : OutputTypesOverride.InheritedSource };
            yield return Defaults(false);
            if (IsRun)
                yield return new("resultsSpans", "Results", "Required parameters and spans", Spans(ActiveResultsParameters), ResultsRequiredInputs.Summary, ResultsParametersError);
        }
        if (!IsBranch || !HasSetupContract) yield break;


        yield return List("setupOutputs", "Setup", SetupDataTypesOverride,
            Summary(InheritedSetupDataTypes.Select(row => $"{row.DataType.Kind}: {row.DataType.Text} · {(row.RequiresSource ? row.Source?.Name ?? "Source not assigned" : "Generated from parameters")} · keys: {row.QueryKeysSummary}")), FirstError(SetupDataTypesError, SetupQueryKeysError))
            with { Source = IsRun ? Parent : SetupDataTypesOverride.InheritedSource };
        yield return Defaults(true);
        if (IsRun)
        {
            yield return new("setupSpans", "Setup", "Required parameters and spans", Spans(ActiveSetupParameters), SetupRequiredInputs.Summary, SetupParametersError);
        }
    }
}
