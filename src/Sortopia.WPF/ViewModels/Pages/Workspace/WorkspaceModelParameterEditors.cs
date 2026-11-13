namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed partial class WorkspaceConfigurationViewModel
{
    private WorkspaceParameterBinding[] visibleResultsDefaults = [], visibleSetupDefaults = [];
    private WorkspaceParameterSpan[] visibleResultsSpans = [], visibleSetupSpans = [];
    private bool showOtherResultsModelInputs, showOtherSetupModelInputs;
    public bool HasResultsModelInputs => Requirements(false)?.Any(value => value.Name == "SorterModelType") == true;
    public bool HasSetupModelInputs => Requirements(true)?.Any(value => value.Name == "SorterModelType") == true;
    public bool ShowOtherResultsModelInputs
    {
        get => showOtherResultsModelInputs;
        set { if (SetProperty(ref showOtherResultsModelInputs, value)) RefreshInherited(); }
    }
    public bool ShowOtherSetupModelInputs
    {
        get => showOtherSetupModelInputs;
        set { if (SetProperty(ref showOtherSetupModelInputs, value)) RefreshInherited(); }
    }

    // Presentation only: stored inputs and the function's fixed parameter contract
    // stay intact when the selected model family changes.
    private bool ModelParameterVisible(string key, bool setup)
    {
        var family = key switch { "AMax" => "SimpleR", "ActivationProbability" => "SimpleS", _ => null };
        if (family is null || !(setup ? HasSetupModelInputs : HasResultsModelInputs)
            || (setup ? ShowOtherSetupModelInputs : ShowOtherResultsModelInputs)) return true;
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        if (defaults.Any(value => value.keyName == "SorterModelType" && value.value == family)) return true;
        return IsRun && ActiveParameters(setup).Any(span => span.Key == "SorterModelType" && span.Values.Any(value => value.Text == family));
    }

    public IEnumerable<WorkspaceParameterBinding> VisibleResultsParameterDefaults =>
        StableRows(LocalResultsParameterDefaults.Where(row => ModelParameterVisible(row.Key, false)), ref visibleResultsDefaults);
    public IEnumerable<WorkspaceParameterBinding> VisibleSetupParameterDefaults =>
        StableRows(LocalSetupParameterDefaults.Where(row => ModelParameterVisible(row.Key, true)), ref visibleSetupDefaults);
    public IEnumerable<WorkspaceParameterBindingPreview> VisibleInheritedOutputDefaults => InheritedOutputDefaults.Where(row => ModelParameterVisible(row.Key, false));
    public IEnumerable<WorkspaceParameterBindingPreview> VisibleInheritedSetupDefaults => InheritedSetupDefaults.Where(row => ModelParameterVisible(row.Key, true));
    public bool HasVisibleInheritedOutputDefaults => VisibleInheritedOutputDefaults.Any();
    public bool HasVisibleInheritedSetupDefaults => VisibleInheritedSetupDefaults.Any();
    public IEnumerable<WorkspaceParameterSpan> VisibleResultsParameters =>
        StableRows(ActiveResultsParameters.Where(row => ModelParameterVisible(row.Key, false)), ref visibleResultsSpans);
    public IEnumerable<WorkspaceParameterSpan> VisibleSetupParameters =>
        StableRows(ActiveSetupParameters.Where(row => ModelParameterVisible(row.Key, true)), ref visibleSetupSpans);
}
