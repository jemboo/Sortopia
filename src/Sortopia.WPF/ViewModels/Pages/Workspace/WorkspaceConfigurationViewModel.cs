using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public abstract class WorkspaceFormItem : ViewModelBase
{
    internal event Action? Changed;
    protected void NotifyChanged() => Changed?.Invoke();
    internal static void Watch<T>(ObservableCollection<T> items, Action changed) where T : WorkspaceFormItem
    {
        var tracked = new HashSet<T>();
        items.CollectionChanged += (_, _) =>
        {
            foreach (var item in tracked.Where(item => !items.Contains(item)).ToArray()) { item.Changed -= changed; tracked.Remove(item); }
            foreach (var item in items.Where(item => !tracked.Contains(item))) { item.Changed += changed; tracked.Add(item); }
            changed();
        };
    }
}

public sealed class WorkspaceParameterSpan : WorkspaceFormItem
{
    private readonly Func<paramKeyDescriptor[]?> requirements;
    private readonly Func<functionParameterSupport?>? support;
    private string key;
    private readonly Func<string, parameterBindingDto?> defaultValue;
    private readonly Func<IEnumerable<string>>? queryKeys;
    private readonly Func<bool> canEdit;
    private readonly Func<bool> isRepair;
    private readonly parameterValueType? storedType;
    private readonly Func<IEnumerable<parameterIssue>>? issues;
    public WorkspaceParameterSpan(string key, IEnumerable<string> values, Action<WorkspaceParameterSpan> remove, Func<string, parameterBindingDto?> defaultValue,
        Func<paramKeyDescriptor[]?>? requirements = null, Func<functionParameterSupport?>? support = null,
        Func<IEnumerable<string>>? queryKeys = null, Func<bool>? canEdit = null, Func<bool>? isRepair = null, parameterValueType? storedType = null,
        Func<IEnumerable<parameterIssue>>? issues = null)
    {
        this.key = key; this.defaultValue = defaultValue;
        this.requirements = requirements ?? (() => null);
        this.support = support;
        this.queryKeys = queryKeys;
        this.canEdit = canEdit ?? (() => true); this.isRepair = isRepair ?? (() => true); this.storedType = storedType;
        this.issues = issues;
        Watch(Values, ValuesChanged);
        foreach (var value in values) AddValue(value);
        AddValueCommand = new RelayCommand(() => AddValue(""), this.canEdit);
        RemoveCommand = new RelayCommand(() => { if (this.canEdit() && this.isRepair()) remove(this); }, () => this.canEdit() && this.isRepair());
        RefreshInherited();
    }
    private void ValuesChanged() { RefreshInherited(); NotifyChanged(); }
    private void AddValue(string value) => Values.Add(new(value, () => ValueType, item => { if (canEdit()) Values.Remove(item); }, () => Key, canEdit, support));
    public bool IsRepair => isRepair();
    public string Key { get => key; internal set { if (SetProperty(ref key, value)) ValuesChanged(); } }
    public ObservableCollection<WorkspaceTypedValue> Values { get; } = [];
    public RelayCommand AddValueCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public string Error => issues is not null ? issues().FirstOrDefault()?.Message ?? Values.Select(value => value.Error).FirstOrDefault(error => error.Length > 0) ?? ""
        : defaultValue(Key) is not null ? $"‘{Key}’ already has a default. Remove this span or exclude the default on the project."
        : string.IsNullOrWhiteSpace(Key) ? "Enter a parameter key."
        : requirements() is { } required && !required.Any(value => value.Name == Key) ? $"‘{Key}’ is not required by the selected function. Remove this span or choose one of its required keys."
        : queryKeys is not null && !queryKeys().Contains(Key, StringComparer.Ordinal) ? $"‘{Key}’ is not a project query key. Select it on the project, or supply a fixed default and remove this span."
        : PhaseParameterContract.spanValuesIssues(Key, Values.Count).FirstOrDefault() is { } issue ? issue.Message
        : Values.Select(value => value.Error).FirstOrDefault(error => error.Length > 0) ?? "";
    public parameterValueType ValueType => requirements()?.FirstOrDefault(value => value.Name == Key)?.ValueType
        ?? storedType ?? (defaultValue(Key) is { } value ? (parameterValueType)value.valueType : ParamKeys.valueType(Key));
    public string TypeLabel => ParameterValue.label(ValueType);
    public string ValueHint => WorkspaceParameterRules.Hint(support?.Invoke(), Key, ValueType);
    public string DefaultHint => defaultValue(Key) is { } value ? $"Conflicts with default: {value.value}"
        : IsRepair && queryKeys is not null ? "Retained input; resolve the issue below before generating parameters."
        : queryKeys is not null ? "Project query parameter; supply one or more values in this run." : "Additional parameter; its values are included in the combinations.";
    internal void RefreshInherited()
    {
        foreach (var value in Values) value.RefreshType();
        AddValueCommand?.RaiseCanExecuteChanged(); RemoveCommand?.RaiseCanExecuteChanged();
        OnPropertyChanged("");
    }
    internal parameterSpanDto ToDto() => new() { keyName = Key, values = Values.Select(value => value.Text).ToArray(), valueType = (int)ValueType };
}

public sealed record WorkspaceOutputType(string Kind, string Text)
{
    public string TextSummary => string.IsNullOrEmpty(Text) ? "Text: (empty)" : $"Text: {Text}";
    internal sortopiaDataTypeDto ToDto() => new() { kind = Kind, value = Text };
}

public sealed record WorkspaceFunctionDefinitionSource(WorkspaceConfigurationViewModel Module, string Path, string FunctionName);

/// <summary>A local value wins over the nearest ancestor; disabling it restores inheritance.</summary>
public abstract class WorkspaceOverrideState : WorkspaceFormItem
{
    private readonly Func<WorkspaceConfigurationViewModel?> inheritedSource;
    private readonly string ownerKind;
    private bool isOverridden, hasDraft;
    protected WorkspaceOverrideState(string title, string ownerKind, Func<WorkspaceConfigurationViewModel?> inheritedSource, bool canOverride = true)
    {
        Title = title; this.ownerKind = ownerKind; this.inheritedSource = inheritedSource; this.canOverride = canOverride;
        UseInheritedFunctionCommand = new RelayCommand(() => IsOverridden = false, () => CanClearFunctionDefinition);
    }
    public bool IsLocal => IsOverridden;
    public string Title { get; }
    private readonly bool canOverride;
    internal Func<bool>? FixedByFunction { get; set; }
    internal bool IsFunctionDefinition { get; set; }
    internal Func<IReadOnlyList<WorkspaceFunctionDefinitionSource>>? FindDescendantDefinitions { get; set; }
    public IReadOnlyList<WorkspaceFunctionDefinitionSource> DescendantFunctionDefinitions { get; private set; } = [];
    public bool HasDescendantFunctionDefinitions => DescendantFunctionDefinitions.Count > 0;
    public bool IsReadOnly { get; internal set; }
    public bool ShowOverrideToggle => canOverride && !IsReadOnly && FixedByFunction?.Invoke() != true;
    private WorkspaceConfigurationViewModel? ParentFunctionDefinition => IsFunctionDefinition && ownerKind == "module" ? inheritedSource() : null;
    public bool CanOverride => ShowOverrideToggle && ParentFunctionDefinition is null && !HasDescendantFunctionDefinitions;
    public bool CanClearFunctionDefinition => ShowOverrideToggle && IsOverridden && (ParentFunctionDefinition is not null || HasDescendantFunctionDefinitions);
    public RelayCommand UseInheritedFunctionCommand { get; }
    public string ClearFunctionDefinitionCaption => ParentFunctionDefinition is not null ? "Use inherited function" : "Remove local definition";
    public string ClearFunctionDefinitionHint => ParentFunctionDefinition is not null
        ? "Remove this local function definition and use the function defined on the parent module."
        : "Remove this local function definition. Descendant modules keep their definitions.";
    public string? DefinitionHint => ParentFunctionDefinition is { } source
        ? $"This function is already defined on {source.Name}. Follow the source link to edit it there."
        : HasDescendantFunctionDefinitions ? "This function is already defined in descendant modules. Follow the links below to edit those definitions." : null;
    protected virtual string OverrideAction => IsFunctionDefinition ? "Define" : "Override";
    public string OverrideCaption => IsFunctionDefinition ? "Define on this module" : ownerKind == "run" ? $"{OverrideAction} for this run" : $"{OverrideAction} on this {ownerKind}";
    public string OverrideAutomationName => $"{OverrideAction} {Title}";
    public bool IsOverridden
    {
        get => isOverridden;
        set
        {
            if (!(value ? CanOverride : ShowOverrideToggle) || isOverridden == value) return;
            if (value && !hasDraft) { SeedOverride(); hasDraft = true; }
            isOverridden = value;
            RefreshInherited(); NotifyChanged();
        }
    }
    public bool IsInherited => !IsOverridden;
    public bool CanEditValue => IsOverridden && !IsReadOnly && FixedByFunction?.Invoke() != true && ParentFunctionDefinition is null && !HasDescendantFunctionDefinitions;
    public bool ShowPreview => !CanEditValue;
    public WorkspaceConfigurationViewModel? InheritedSource => ParentFunctionDefinition ?? (IsInherited || FixedByFunction?.Invoke() == true ? inheritedSource() : null);
    public bool HasInheritedSource => InheritedSource is not null;
    public string SourceLabel => IsReadOnly ? "Locked while generated run parameter files exist" : ParentFunctionDefinition is { } definition ? $"{(IsOverridden ? "Also defined on" : "Defined on")} {definition.Name}" : FixedByFunction?.Invoke() == true ? "Defined by the selected function" : IsOverridden ? IsFunctionDefinition ? "Defined on this module" : ownerKind == "run" ? "Overridden for this run" : $"Local override on this {ownerKind}"
        : InheritedSource is { } source ? $"Inherited from {source.Name}" : HasDescendantFunctionDefinitions ? "This module leaves the function to its descendants." : IsFunctionDefinition ? "Not defined on the module path" : "Default value · no ancestor override";
    protected abstract void SeedOverride();
    internal void MarkLocal() { isOverridden = hasDraft = true; RefreshInherited(); }
    protected void LoadState(bool local) { isOverridden = hasDraft = local; RefreshInherited(); }
    internal virtual void RefreshInherited()
    {
        DescendantFunctionDefinitions = FindDescendantDefinitions?.Invoke() ?? [];
        OnPropertyChanged(""); UseInheritedFunctionCommand.RaiseCanExecuteChanged();
    }
}

public sealed class WorkspaceOverrideField : WorkspaceOverrideState
{
    private readonly Func<string> inherited;
    private readonly bool required, requireInherited;
    private string value = "";
    public WorkspaceOverrideField(string title, bool required, Func<string> inherited, Func<WorkspaceConfigurationViewModel?> source,
        string ownerKind, bool canOverride = true, bool requireInherited = true, string[]? choices = null) : base(title, ownerKind, source, canOverride)
    { this.required = required; this.requireInherited = requireInherited; this.inherited = inherited; catalogChoices = choices ?? []; }
    private readonly string[] catalogChoices;
    internal Func<string[]>? AvailableChoices { get; set; }
    internal Func<bool>? SelectionEnabled { get; set; }
    internal bool EmptyMeansInherited { get; set; }
    public string[] Choices => AvailableChoices?.Invoke() ?? catalogChoices;
    public bool HasChoices => catalogChoices.Length > 0;
    public bool IsSetupCatalog { get; internal set; }
    public bool ShowCatalogPicker => HasChoices && ShowOverrideToggle;
    public bool CanSelectValue => CanEditValue && SelectionEnabled?.Invoke() != false;
    public bool IsMultiline => !required;
    protected override void SeedOverride() => value = inherited();
    public string EffectiveValue
    {
        get => IsOverridden ? value : inherited();
        set
        {
            if (IsReadOnly || !IsOverridden || this.value == value) return;
            if (EmptyMeansInherited && string.IsNullOrWhiteSpace(value)) { Load(null); NotifyChanged(); return; }
            if (!CanEditValue) return;
            this.value = value; RefreshInherited(); NotifyChanged();
        }
    }
    public string Error
    {
        get
        {
            if (!required || !(IsOverridden || requireInherited) || !string.IsNullOrWhiteSpace(EffectiveValue)) return "";
            var section = HasChoices ? Title : Title.StartsWith("Setup", StringComparison.Ordinal) ? "Setup settings" : "Results settings";
            var action = HasChoices ? $"choose a {Title.ToLowerInvariant()} from the catalog" : $"enter a {Title.ToLowerInvariant()}";
            if (IsOverridden) return $"{Title} is empty. In {section}, {action}."
                + (!string.IsNullOrWhiteSpace(inherited()) ? $" You can also clear ‘{OverrideCaption}’ to use the inherited value." : "");
            if (CanOverride) return $"No {Title.ToLowerInvariant()} is selected. Open {section}, check ‘{OverrideCaption}’, and {action}. You can also select the function on a parent module to share it with its projects.";
            return $"No {Title.ToLowerInvariant()} is selected. Select a parent module in the tree, click ‘{Title}’, enable ‘Define on this module’, and {action}. Functions can only be selected on modules.";
        }
    }
    public string? OverrideValue => IsOverridden ? value : null;
    internal void Load(string? input) { if (EmptyMeansInherited && string.IsNullOrWhiteSpace(input)) input = null; value = input ?? ""; LoadState(input is not null); }
    internal void SetLocal(string input)
    {
        if (EmptyMeansInherited && string.IsNullOrWhiteSpace(input) && ShowOverrideToggle) { Load(null); NotifyChanged(); return; }
        if (IsReadOnly || !CanOverride) return;
        value = input; MarkLocal(); NotifyChanged();
    }
}

public sealed class WorkspaceListOverride : WorkspaceOverrideState
{
    private readonly Action seed;
    public WorkspaceListOverride(string title, string ownerKind, Func<WorkspaceConfigurationViewModel?> source, Action seed, bool canOverride)
        : base(title, ownerKind, source, canOverride) => this.seed = seed;
    protected override void SeedOverride() => seed();
    internal void Load(bool local) => LoadState(local);
}

/// <summary>Editable local values and effective inherited values are kept separate throughout the tree.</summary>
public sealed partial class WorkspaceConfigurationViewModel : ViewModelBase
{
    private readonly Func<WorkspaceConfigurationViewModel?> parent;
    private readonly Func<string> name;
    private bool loading;
    // The sealed state owns both the execution snapshot and the original editing choices.
    // Draft state is materialized from the live form when producing a document.
    private projectStateDto? sealedState;
    private projectSettingsDto? frozenSettings => sealedState is null ? null : ProjectLifecycle.configuration(sealedState);
    internal bool IsSealed => sealedState is not null;
    internal projectStateDto ToProjectState(projectSettingsDto inherited, inheritedFromDto? origin) =>
        sealedState ?? projectStateDto.NewDraft(ToLocalSettings(), inherited, origin!);
    private WorkspaceParameterSetsPreview? resultsPreview, setupPreview;
    private WorkspaceParameterBinding[] activeResultsDefaults = [], activeSetupDefaults = [];
    public WorkspaceEditorSections Sections { get; } = new();
    private readonly Func<IEnumerable<WorkspaceSourceProject>> sources;
    private bool generatedParametersLock;
    private bool reportingEnabled;
    private string snapshotInterval = "100", snapshotCount = "1", tempSnapshotInterval = "10";
    private sgdRunConfig? frozenSnapshotControls;
    public bool UsesSnapshotControls => SupportsReporting && PhaseVersionCurrent(false);
    public bool ShowUnsupportedReporting => ShowReportingConfiguration && !UsesSnapshotControls;
    public bool CanEditSnapshotInterval => UsesSnapshotControls && IsEditable && frozenSnapshotControls is null;
    public bool CanEditSnapshotCount => UsesSnapshotControls;
    public string SnapshotInterval { get => snapshotInterval; set { if (CanEditSnapshotInterval && SetProperty(ref snapshotInterval, value)) NotifyChanged(); } }
    public string SnapshotCount { get => snapshotCount; set { if (CanEditSnapshotCount && SetProperty(ref snapshotCount, value)) NotifyChanged(); } }
    public string TempSnapshotInterval { get => tempSnapshotInterval; set { if (UsesSnapshotControls && SetProperty(ref tempSnapshotInterval, value)) NotifyChanged(); } }
    private sgdRunConfig SnapshotControls()
    {
        if (!int.TryParse(SnapshotInterval, out var interval) || !int.TryParse(SnapshotCount, out var count) || !int.TryParse(TempSnapshotInterval, out var temporary))
            throw new ArgumentException("SnapshotInterval, SnapshotCount and temp snapshot interval must be whole numbers.");
        var controls = SgdRun.validate(new(interval, count, temporary == 10 ? null : Microsoft.FSharp.Core.FSharpOption<int>.Some(temporary)));
        if (frozenSnapshotControls is not null) SgdRun.validateExtension(frozenSnapshotControls, controls);
        return controls;
    }
    internal void SetSnapshotControlsLock(sgdRunConfig? controls)
    {
        if (Equals(frozenSnapshotControls, controls)) return;
        frozenSnapshotControls = controls;
        OnPropertyChanged("");
    }
    public string SnapshotTargetText
    {
        get
        {
            try { var controls = SnapshotControls(); return $"Target: {SgdRun.target(controls):N0} generations · {(long)controls.SnapshotCount + 1:N0} snapshots including generation zero."; }
            catch { return "Enter a positive interval and a nonnegative snapshot count."; }
        }
    }
    public WorkspaceReportingSchedule SnapshotReporting { get; }
    public WorkspaceReportingSchedule SummaryReporting { get; }
    public bool SupportsReporting => IsRun && ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var definition) && definition.SupportsEvolutionReporting;
    public bool ShowReportingConfiguration => IsRun && (SupportsReporting || ReportingEnabled);
    public bool ReportingEnabled { get => UsesSnapshotControls || reportingEnabled; set { if (IsEditable && !UsesSnapshotControls && SetProperty(ref reportingEnabled, value)) NotifyChanged(); } }
    public string ReportingError
    {
        get
        {
            if (!IsRun || !ReportingEnabled) return "";
            if (!SupportsReporting) return "The results function does not support evolution reporting.";
            if (UsesSnapshotControls)
            {
                try { SnapshotControls(); return SummaryReporting.Error; }
                catch (Exception error) { return error.GetBaseException().Message; }
            }
            return "This saved SGD version is not supported. Create a new project version with SnapshotInterval and SnapshotCount.";
        }
    }
    public bool IsReadOnly => frozenSettings is not null || generatedParametersLock;
    public bool IsEditable => !IsReadOnly;
    internal void SetGeneratedParametersLock(bool locked)
    {
        generatedParametersLock = locked;
        foreach (var state in properties.Values) state.IsReadOnly = IsReadOnly;
        RefreshInherited();
    }
    internal void Freeze(projectSettingsDto? snapshot = null) => LockProjectState(ProjectLifecycle.seal(
        snapshot ?? ToProjectSettings(), ToProjectState(Parent?.ToProjectSettings()!, null)));
    private void LockProjectState(projectStateDto state)
    {
        sealedState = state;
        var snapshot = ProjectLifecycle.configuration(state);
        ResultsFunctionOverride.Load(snapshot.resultsFunctionName);
        SetupFunctionOverride.Load(IsBranch ? snapshot.setupFunctionName : null);
        foreach (var property in properties.Values) property.IsReadOnly = true;
        RefreshInherited();
    }
    internal void ApplyProjectState(projectStateDto state)
    {
        if (!IsProject) throw new InvalidOperationException("Project state belongs to a project.");
        sealedState = null; generatedParametersLock = false;
        foreach (var property in properties.Values) property.IsReadOnly = false;
        ResultsFunctionOverride.Load(null); SetupFunctionOverride.Load(null);
        if (state.IsSealed)
        {
            var snapshot = ProjectLifecycle.configuration(state);
            LoadProject(snapshot);
            LockProjectState(state);
        }
        else { LoadLocalSettings(ProjectLifecycle.localSettings(state)); RefreshInherited(); }
    }
    private readonly Dictionary<string, WorkspaceOverrideState> properties = [];
    public WorkspaceConfigurationViewModel(WorkspaceEntityKind kind, bool isBranch, Func<WorkspaceConfigurationViewModel?> parent, Func<string> name, Func<IEnumerable<WorkspaceSourceProject>>? sources = null, Func<IEnumerable<WorkspaceConfigurationViewModel>>? childEditors = null)
    {
        this.sources = sources ?? (() => []);
        this.childEditors = childEditors ?? (() => []);
        IsProject = kind == WorkspaceEntityKind.Project; IsModule = kind == WorkspaceEntityKind.Module;
        IsBranch = isBranch; this.parent = parent; this.name = name;
        SnapshotReporting = new("Full snapshots", "100", () => IsEditable, NotifyChanged);
        SummaryReporting = new("Pool summaries", "1", () => IsEditable, NotifyChanged);
        var owner = kind.ToString().ToLowerInvariant();
        WorkspaceOverrideField Field(string key, string title, bool required, Func<string> inherited, bool canOverride = true, string[]? choices = null)
        {
            var field = new WorkspaceOverrideField(title, required, inherited, () => Parent?.ValueSource(key), owner, canOverride, !IsModule, choices);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        DescriptionOverride = Field(nameof(Description), "Description", false, () => Parent?.Description ?? "");
        ResultsDatabaseField = new("Results database", "Results", this, () => Parent?.ResultsDatabaseName ?? "", () => Parent);
        ResultsDatabaseField.Changed += NotifyChanged;
        ResultsFunctionOverride = Field(nameof(ResultsFunctionName), "Results function", !IsModule, () => Parent?.ResultsFunctionName ?? "", IsModule,
            ResultsFunctionCatalog.functions.Keys.ToArray());
        SetupDatabaseField = new("Setup database", "Setup", this, () => Parent?.SetupDatabaseName ?? "", () => Parent);
        SetupDatabaseField.Changed += NotifyChanged;
        SetupFunctionOverride = Field(nameof(SetupFunctionName), "Setup function", !IsModule, () => Parent?.SetupFunctionName ?? "", IsModule, choices:
            SetupFunctionCatalog.functions.Keys.ToArray());
        ResultsFunctionOverride.EmptyMeansInherited = SetupFunctionOverride.EmptyMeansInherited = true;
        ResultsFunctionOverride.IsFunctionDefinition = SetupFunctionOverride.IsFunctionDefinition = true;
        if (IsModule)
        {
            ResultsFunctionOverride.FindDescendantDefinitions = () => DescendantFunctionSources(false).ToArray();
            SetupFunctionOverride.FindDescendantDefinitions = () => DescendantFunctionSources(true).ToArray();
        }
        SetupFunctionOverride.IsSetupCatalog = true;
        ResultsFunctionOverride.AvailableChoices = () => IsModule ? CompatibleResultsFunctions : ResultsFunctionCatalog.functions.Keys.ToArray();
        WorkspaceListOverride List(string key, string title, Action seed)
        {
            var field = new WorkspaceListOverride(title, owner, () => key switch {
                nameof(OutputTypesOverride) when HasResultsContract => ValueSource(nameof(ResultsFunctionName)),
                _ => Parent?.ValueSource(key)
            }, () => Quietly(seed), IsDefaults);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        OutputTypesOverride = List(nameof(OutputTypesOverride), "Results data types", () => { });
        OutputTypesOverride.FixedByFunction = () => true;
        ResultsParameterDefaultsOverride = List(nameof(ResultsParameterDefaults), "Results parameter defaults", () => { });
        SetupParameterDefaultsOverride = List(nameof(SetupParameterDefaults), "Setup parameter defaults", () => { });
        WorkspaceFormItem.Watch(ResultsParameterDefaults, () => ListChanged(ResultsParameterDefaultsOverride));
        WorkspaceFormItem.Watch(SetupParameterDefaults, () => ListChanged(SetupParameterDefaultsOverride));
        AddResultsParameterDefaultCommand = new RelayCommand(() => { ResultsParameterDefaultsOverride.IsOverridden = true; AddDefault(ResultsParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults);
        AddSetupParameterDefaultCommand = new RelayCommand(() => { SetupParameterDefaultsOverride.IsOverridden = true; AddDefault(SetupParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults && IsBranch);
        AddRequiredResultsDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(false), () => CanAddRequiredDefaults(false));
        AddRequiredSetupDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(true), () => CanAddRequiredDefaults(true));
        WorkspaceFormItem.Watch(ResultsParameters, NotifyChanged); WorkspaceFormItem.Watch(SetupParameters, NotifyChanged);
        RefreshInherited();
    }
    private WorkspaceConfigurationViewModel? Parent => parent();
    internal string Name => name();
    private WorkspaceConfigurationViewModel? ValueSource(string property)
    {
        if (HasResultsContract && property is nameof(OutputTypesOverride)) property = nameof(ResultsFunctionName);
        var field = properties[property];
        var definesValue = field.IsOverridden && (field is not WorkspaceOverrideField { IsFunctionDefinition: true } function || !string.IsNullOrWhiteSpace(function.OverrideValue));
        return definesValue ? this : Parent?.ValueSource(property);
    }
    public bool IsProject { get; }
    public bool IsModule { get; }
    public bool ShowDatabaseSettings => !IsModule;
    public bool IsDefaults => IsModule || IsProject;
    public bool CanEditDefaults => IsDefaults && IsEditable;
    public bool IsRun => !IsDefaults;
    public bool IsBranch { get; }
    public string Title => IsModule ? "Module defaults" : IsProject ? "Project defaults" : "Run inputs";
    public string Hint => IsReadOnly ? "Settings are locked while descendant runs have generated parameter files. Remove those files or runs to edit again; save run removals to finish deleting their files." : IsDefaults
        ? "Parameter defaults inherit by key. Setup source choices inherit independently by data type. Each local override replaces only its matching inherited value."
        : "Functions and defaults come from the parent project. Supply spans for exactly its query keys; other required parameters use fixed defaults.";
    public string Description { get => DescriptionOverride.EffectiveValue; set => DescriptionOverride.SetLocal(value); }
    public string ResultsDatabaseName { get => ResultsDatabaseField.EffectiveValue; set => ResultsDatabaseField.EffectiveValue = value; }
    public string ResultsFunctionName { get => frozenSettings?.resultsFunctionName ?? ResultsFunctionOverride.EffectiveValue; set { if (IsModule) ResultsFunctionOverride.SetLocal(value); } }
    public string SetupDatabaseName { get => SetupDatabaseField.EffectiveValue; set => SetupDatabaseField.EffectiveValue = value; }
    public string SetupFunctionName { get => frozenSettings?.setupFunctionName ?? SetupFunctionOverride.EffectiveValue; set { if (IsModule) SetupFunctionOverride.SetLocal(value); } }

    private paramKeyDescriptor[]? Requirements(bool setup) => !PhaseVersionCurrent(setup) ? null : setup
        ? SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var setupDefinition)
            ? setupDefinition.ParametersFromSources
                ? IsModule && EffectiveSetupDataTypes.Any(row => row.sourceProjectId == Guid.Empty)
                    ? null
                    : SourceSetupContract.RequiredParameters.ToArray()
                : setupDefinition.RequiredParameters.ToArray() : null
        : ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var resultsDefinition) ? resultsDefinition.RequiredParameters.ToArray() : null;
    private bool SetupParametersFromSources => SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var definition) && definition.ParametersFromSources;
    public string ResultsRequirementsHint => RequirementsHint(false);
    public string SetupRequirementsHint => RequirementsHint(true);
    private string RequirementsHint(bool setup) => setup && SetupParametersFromSources && SourceSetupKeys.Length == 0
        ? "Choose source projects for SorterModelSet and SortableTests. Their results query keys determine the setup parameters; no models or tests are generated."
        : Requirements(setup) is { } required
        ? "Required parameters: " + string.Join(", ", required.Select(key => $"{key.Name} ({ParameterValue.label(key.ValueType)})")) + ". Project query keys require run spans. All other required parameters need fixed defaults. Defaults and spans cannot share keys."
        : "Select a catalog function on a module to define the required parameter keys.";
    private parameterBindingDto[] InheritedDefaults(bool setup)
    {
        var inherited = (setup ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults) ?? [];
        if ((setup ? SetupFunctionName == Parent?.SetupFunctionName : ResultsFunctionName == Parent?.ResultsFunctionName)) return inherited;
        var required = Requirements(setup);
        return required is null ? inherited : inherited.Where(value => required.Any(key => key.Name == value.keyName)).ToArray();
    }
    public WorkspaceOverrideField DescriptionOverride { get; }
    public WorkspaceProjectField ResultsDatabaseField { get; }
    public WorkspaceOverrideField ResultsFunctionOverride { get; }
    public WorkspaceProjectField SetupDatabaseField { get; }
    public WorkspaceOverrideField SetupFunctionOverride { get; }
    public WorkspaceListOverride OutputTypesOverride { get; }
    public WorkspaceListOverride ResultsParameterDefaultsOverride { get; }
    public WorkspaceListOverride SetupParameterDefaultsOverride { get; }
    public ObservableCollection<WorkspaceParameterBinding> ResultsParameterDefaults { get; } = [];
    public ObservableCollection<WorkspaceParameterBinding> SetupParameterDefaults { get; } = [];
    public RelayCommand AddResultsParameterDefaultCommand { get; }
    public RelayCommand AddSetupParameterDefaultCommand { get; }
    public RelayCommand AddRequiredResultsDefaultsCommand { get; }
    public RelayCommand AddRequiredSetupDefaultsCommand { get; }
    public string RequiredResultsDefaultsHint => RequiredDefaultsHint(false);
    public string RequiredSetupDefaultsHint => RequiredDefaultsHint(true);
    private paramKeyDescriptor[] MissingDefaultParameters(bool setup)
    {
        var existing = (setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        return Requirements(setup)?.Where(key => !existing.Contains(key.Name) && !(setup ? excludedSetupDefaults : excludedResultsDefaults).Contains(key.Name)).ToArray() ?? [];
    }
    private bool CanAddRequiredDefaults(bool setup) => IsModule && CanEditDefaults && (!setup || IsBranch) && MissingDefaultParameters(setup).Length > 0;
    private string RequiredDefaultsHint(bool setup) => Requirements(setup) is null
        ? "Choose or inherit a catalog function to add its required defaults."
        : MissingDefaultParameters(setup) is { Length: > 0 } missing
            ? $"Adds {missing.Length} missing default entries with suggested initial values. Inherited and local values are preserved; review the new values before saving."
            : "Every required parameter already has a default entry.";
    private void AddRequiredDefaults(bool setup)
    {
        if (!CanAddRequiredDefaults(setup)) return;
        var missing = MissingDefaultParameters(setup);
        var additions = WorkspaceParameterInitialValues.CreateMissing(
            setup ? EffectiveSetupDefaults : EffectiveOutputDefaults,
            setup ? EffectiveOutputDefaults : EffectiveSetupDefaults, missing, ParameterSupport(setup));
        Quietly(() =>
        {
            (setup ? SetupParameterDefaultsOverride : ResultsParameterDefaultsOverride).IsOverridden = true;
            var rows = setup ? SetupParameterDefaults : ResultsParameterDefaults;
            foreach (var value in additions)
                if (!rows.Any(row => row.Key == value.keyName))
                    AddDefault(rows, value);
        });
        NotifyChanged();
    }
    private IEnumerable<WorkspaceParameterBinding> ActiveLocalDefaults(bool setup)
    {
        IEnumerable<WorkspaceParameterBinding> rows = (setup ? SetupParameterDefaultsOverride : ResultsParameterDefaultsOverride).IsOverridden
            ? setup ? SetupParameterDefaults : ResultsParameterDefaults : [];
        // Keep other functions' values in the editing session, outside the active configuration.
        var active = Requirements(setup) is { } required
            ? rows.Where(row => string.IsNullOrWhiteSpace(row.Key) || required.Any(key => key.Name == row.Key)) : rows;
        return setup ? StableRows(active, ref activeSetupDefaults) : StableRows(active, ref activeResultsDefaults);
    }
    // Keep the bound collection stable while values are edited, so filtering does not
    // recreate the WPF row controls and discard keyboard focus on every keystroke.
    private static T[] StableRows<T>(IEnumerable<T> rows, ref T[] current)
    {
        var values = rows.ToArray();
        if (!current.SequenceEqual(values)) current = values;
        return current;
    }
    public IEnumerable<WorkspaceParameterBinding> LocalResultsParameterDefaults => ActiveLocalDefaults(false);
    public IEnumerable<WorkspaceParameterBinding> LocalSetupParameterDefaults => ActiveLocalDefaults(true);
    private WorkspaceParameterBinding[] editableResultsDefaults = [], editableSetupDefaults = [];
    public IEnumerable<WorkspaceParameterBinding> EditableResultsParameterDefaults => StableRows(LocalResultsParameterDefaults.Where(row => !excludedResultsDefaults.Contains(row.Key)), ref editableResultsDefaults);
    public IEnumerable<WorkspaceParameterBinding> EditableSetupParameterDefaults => StableRows(LocalSetupParameterDefaults.Where(row => !excludedSetupDefaults.Contains(row.Key)), ref editableSetupDefaults);
    private static parameterBindingDto[] MergeDefaults(parameterBindingDto[] inherited, IEnumerable<WorkspaceParameterBinding> rows)
    {
        var local = rows.Select(row => row.ToDto()).ToArray();
        var inheritedKeys = inherited.Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        return inherited.Select(value => local.FirstOrDefault(binding => binding.keyName == value.keyName) ?? value)
            .Concat(local.Where(value => !inheritedKeys.Contains(value.keyName))).ToArray();
    }
    private parameterBindingDto[] EffectiveOutputDefaults => frozenSettings?.resultsParameterDefaults ?? MergeDefaults(InheritedDefaults(false), LocalResultsParameterDefaults).Where(value => !excludedResultsDefaults.Contains(value.keyName)).ToArray();
    private parameterBindingDto[] EffectiveSetupDefaults => frozenSettings?.setupParameterDefaults ?? MergeDefaults(InheritedDefaults(true), LocalSetupParameterDefaults).Where(value => !excludedSetupDefaults.Contains(value.keyName)).ToArray();
    private string DefaultsError(bool setup, parameterBindingDto[] values)
    {
        if (values.Any(value => string.IsNullOrWhiteSpace(value.keyName))) return "Enter a key for each default parameter.";
        if (values.Select(value => value.keyName).Distinct(StringComparer.Ordinal).Count() != values.Length) return "Default parameter keys must be unique.";
        return values.Select(value => (value.keyName, Error: WorkspaceParameterRules.Error(ParameterSupport(setup), value.keyName, (parameterValueType)value.valueType, value.value)))
            .Where(value => value.Error.Length > 0).Select(value => $"Parameter ‘{value.keyName}’: {value.Error}").FirstOrDefault() ?? "";
    }
    private string ParameterDefaultsError(bool setup)
    {
        var local = setup ? EditableSetupParameterDefaults : EditableResultsParameterDefaults;
        var localError = IsReadOnly ? "" : local.Where(row => row.Error.Length > 0).Select(row => $"Parameter ‘{row.Key}’: {row.Error}").FirstOrDefault()
            ?? DefaultsError(setup, local.Select(row => row.ToDto()).ToArray());
        var values = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var required = Requirements(setup);
        var outside = required is null ? null : values.FirstOrDefault(value => !required.Any(key => key.Name == value.keyName));
        return localError.Length > 0 ? localError : outside is not null
            ? $"Parameter ‘{outside.keyName}’ is not required by function ‘{(setup ? SetupFunctionName : ResultsFunctionName)}’. Remove its local default or choose a required key."
            : FirstError(DefaultsError(setup, values), FunctionInputsError(setup, false));
    }
    public string ResultsParameterDefaultsError => ParameterDefaultsError(false);
    public string SetupParameterDefaultsError => ParameterDefaultsError(true);
    public string ResultsParameterDefaultsSummary => EffectiveOutputDefaults.Length == 0 ? "No parameter defaults." : "";
    public string SetupParameterDefaultsSummary => EffectiveSetupDefaults.Length == 0 ? "No parameter defaults." : "";
    private WorkspaceConfigurationViewModel? ParameterSource(string key, bool setup)
    {
        if (frozenSettings is not null)
            return (setup ? frozenSettings.setupParameterDefaults : frozenSettings.resultsParameterDefaults).Any(value => value.keyName == key) ? this : null;
        if ((setup ? LocalSetupParameterDefaults : LocalResultsParameterDefaults).Any(value => value.Key == key)) return this;
        return Parent?.ParameterSource(key, setup);
    }
    private WorkspaceParameterValueSource LocalParameterSource(string key, bool setup) => Parent?.ParameterSource(key, setup) is { } source
        ? new($"Overrides value from {source.Name} · remove this row to inherit again", source, true)
        : new($"Defined on {Name}", null, true);
    private void OverrideParameter(string key, bool setup)
    {
        if (!CanEditDefaults) return;
        var state = setup ? SetupParameterDefaultsOverride : ResultsParameterDefaultsOverride;
        var rows = setup ? SetupParameterDefaults : ResultsParameterDefaults;
        var inherited = setup ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults;
        if (inherited?.FirstOrDefault(value => value.keyName == key) is not { } value) return;
        state.IsOverridden = true;
        if (!rows.Any(row => row.Key == key)) AddDefault(rows, value);
    }
    private IEnumerable<WorkspaceParameterBindingPreview> Preview(bool setup)
    {
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var spans = setup ? SetupParameters : ResultsParameters;
        foreach (var value in defaults)
        {
            var source = ParameterSource(value.keyName, setup);
            if (CanEditDefaults && ReferenceEquals(source, this)) continue;
            var origin = ReferenceEquals(source, this) ? new WorkspaceParameterValueSource("Locked while generated run parameter files exist", null)
                : new WorkspaceParameterValueSource(source is null ? "Default value" : $"Inherited from {source.Name}", source);
            yield return new(value.keyName, ParameterValue.label((parameterValueType)value.valueType), value.value,
                IsRun ? spans.Any(span => span.Key == value.keyName) ? "Conflict: also supplied by a run span"
                    : QueryKeys(setup).Contains(value.keyName) ? "Query key · requires an explicit run span" : "Used in every parameter set" : "",
                origin, CanEditDefaults ? new RelayCommand(() => OverrideParameter(value.keyName, setup)) : null,
                CanEditDefaults ? new RelayCommand(() => ExcludeDefault(value.keyName, setup)) : null);
        }
    }
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedOutputDefaults => Preview(false);
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedSetupDefaults => Preview(true);
    public bool HasInheritedOutputDefaults => InheritedOutputDefaults.Any();
    public bool HasInheritedSetupDefaults => InheritedSetupDefaults.Any();
    public string ParameterDefaultsPreviewTitle => IsReadOnly ? "Fixed parameter defaults" : IsRun ? "Inherited project defaults" : "Inherited defaults";
    private static string SetCount(IEnumerable<WorkspaceParameterSpan> spans) => spans.Aggregate(BigInteger.One, (count, span) => count * span.Values.Count).ToString("N0", CultureInfo.InvariantCulture);
    public string ResultsParameterSetsSummary => $"{SetCount(ResultsParameters)} execution parameter set(s). Defaults stay fixed; span keys must be different from default keys.";
    public string SetupParameterSetsSummary => $"{SetCount(SetupParameters)} setup parameter set(s). Defaults stay fixed; span keys must be different from default keys.";
    public WorkspaceParameterSetsPreview ResultsParameterSetsPreview => !IsRun ? WorkspaceParameterSetsPreview.Empty
        : resultsPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveOutputDefaults, ResultsParameters,
            FirstError(ResultsParameterDefaultsError, ResultsParametersError), PhaseVersionCurrent(false) ? ParameterSupport(false) : null);
    public WorkspaceParameterSetsPreview SetupParameterSetsPreview => !IsRun || !IsBranch ? WorkspaceParameterSetsPreview.Empty
        : setupPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveSetupDefaults, SetupParameters,
            FirstError(SetupParameterDefaultsError, SetupParametersError), PhaseVersionCurrent(true) ? ParameterSupport(true) : null);
    private static string FirstError(params string[] errors) => errors.FirstOrDefault(error => error.Length > 0) ?? "";
    public string ResultsSettingsError => FirstError(ResultsDatabaseError, ResultsFunctionError, OutputTypesError, ResultsQueryKeysError);
    public string SetupSettingsError => FirstError(SetupDatabaseError, SetupFunctionError, SetupDataTypesError, SetupQueryKeysError);
    public ObservableCollection<WorkspaceSetupDataTypeEntry> SetupDataTypes { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> ResultsParameters { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> SetupParameters { get; } = [];
    private IEnumerable<WorkspaceParameterSpan> ParameterSpans(bool setup) => setup ? SetupParameters : ResultsParameters;
    private string[] QueryKeys(bool setup) => PhaseContract(setup).SpanKeys.ToArray();
    private WorkspaceParameterSpan[] declaredResultsSpans = [], declaredSetupSpans = [], repairResultsSpans = [], repairSetupSpans = [];
    public IEnumerable<WorkspaceParameterSpan> DeclaredResultsParameters => StableRows(DeclaredParameters(false), ref declaredResultsSpans);
    public IEnumerable<WorkspaceParameterSpan> DeclaredSetupParameters => StableRows(DeclaredParameters(true), ref declaredSetupSpans);
    public IEnumerable<WorkspaceParameterSpan> RepairResultsParameters => StableRows(ResultsParameters.Where(row => row.IsRepair), ref repairResultsSpans);
    public IEnumerable<WorkspaceParameterSpan> RepairSetupParameters => StableRows(SetupParameters.Where(row => row.IsRepair), ref repairSetupSpans);
    public bool HasResultsParameterRepairs => RepairResultsParameters.Any();
    public bool HasSetupParameterRepairs => RepairSetupParameters.Any();
    private IEnumerable<WorkspaceParameterSpan> DeclaredParameters(bool setup) => ParameterSpans(setup).Where(row => !row.IsRepair);
    private bool IsRepairSpan(ObservableCollection<WorkspaceParameterSpan> rows, WorkspaceParameterSpan span) =>
        !QueryKeys(ReferenceEquals(rows, SetupParameters)).Contains(span.Key, StringComparer.Ordinal)
        || !ReferenceEquals(rows.FirstOrDefault(row => row.Key == span.Key), span);
    private sortopiaDataTypeDto[] EffectiveOutputs => EffectiveResultsDataTypes.Select(output => output.dataType).ToArray();
    private string[] EffectiveOutputKeys => EffectiveResultsDataTypes.SelectMany(output => output.queryParameterKeys).Distinct(StringComparer.Ordinal).ToArray();
    private setupDataTypeConfigDto[] EffectiveSetupDataTypes => (IsRun ? Parent?.EffectiveSetupDataTypes : null) ?? frozenSettings?.setupDataTypes ?? ResolveSetupTypes();
    public IEnumerable<WorkspaceSetupDataTypeEntry> InheritedSetupDataTypes => EffectiveSetupDataTypes.Select(value =>
    {
        var row = new WorkspaceSetupDataTypeEntry(value, sources) { SelectionOwner = DefinedSetupSourceOwner(value.dataType) };
        if (HasSetupContract) row.SetContract(NeedsSource(value.dataType), IsModule, value.queryParameterKeys, Requirements(true)?.Select(key => key.Name).ToArray(), SetupParametersFromSources);
        return row;
    });
    private sortopiaDataTypeDto[] EffectiveSetupOutputs => EffectiveSetupDataTypes.Select(value => value.dataType).ToArray();
    public sortopiaDataTypeDto[] InheritedOutputTypes => EffectiveOutputs;
    public sortopiaDataTypeDto[] InheritedSetupOutputTypes => EffectiveSetupOutputs;
    private static string DataTypesError(sortopiaDataTypeDto[] values) => values.Any(value => !SortopiaDataType.kinds.Contains(value.kind, StringComparer.Ordinal))
        ? "Choose a data type for each row." : values.Any(value => value.value is null) ? "Data type text cannot be null; an empty string is allowed."
        : values.Select(value => (value.kind, value.value)).Distinct().Count() != values.Length ? "Each data type and text pair must be unique." : "";
    public string OutputTypesError => DataTypesError(EffectiveOutputs);
    public string SetupDataTypesError => DataTypesError(EffectiveSetupOutputs) is { Length: > 0 } error ? error
        : InheritedSetupDataTypes.Select(row => row.Error).FirstOrDefault(e => e.Length > 0) ?? "";
    private string[] EffectiveSetupKeys => EffectiveSetupDataTypes.SelectMany(row => row.queryParameterKeys).Distinct(StringComparer.Ordinal).ToArray();
    private sourceQueryContract SourceContractFor(setupDataTypeConfigDto row) => WorkspaceSourceContracts.Resolve(
        sources().FirstOrDefault(p => p.Id == row.sourceProjectId), row.dataType.kind, row.dataType.value);
    private string[] SourceKeysFor(setupDataTypeConfigDto row) => SourceContractFor(row).QueryKeys.ToArray();
    private sourceQueryContract SourceSetupContract => SourceContracts.combine(EffectiveSetupDataTypes.Where(row => NeedsSource(row.dataType)).Select(SourceContractFor));
    private string[] SourceSetupKeys => SourceSetupContract.QueryKeys.ToArray();
    private string[] RequiredSetupKeys => EffectiveSetupKeys.Concat(SourceSetupKeys).Distinct(StringComparer.Ordinal).ToArray();
    public string ResultsDatabaseError => ResultsDatabaseField.Error;
    public string ResultsFunctionError => FirstError(FunctionDefinitionError(false), ResultsFunctionOverride.Error, ResultsFunctionName.Length > 0 && !HasResultsContract ? $"Unknown results function ‘{ResultsFunctionName}’. Choose a registered function on the module." : "", FunctionCompatibilityError);
    public string SetupDatabaseError => SetupDatabaseField.Error;
    public string SetupFunctionError => FirstError(FunctionDefinitionError(true), SetupFunctionOverride.Error, SetupFunctionName.Length > 0 && !HasSetupContract ? $"Unknown setup function ‘{SetupFunctionName}’. Choose a registered function on the module." : "");
    private static string KeysError(string[] values) => values.Any(string.IsNullOrWhiteSpace) ? "Enter a name for each query key, or remove its row."
        : values.Distinct(StringComparer.Ordinal).Count() != values.Length ? "Query keys must be unique." : "";
    private static string[] MissingKeys(IEnumerable<WorkspaceParameterSpan> spans, IEnumerable<string>? keys) =>
        keys?.Where(key => !string.IsNullOrWhiteSpace(key) && !spans.Any(span => span.Key == key)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private string ParametersError(bool setup)
    {
        var rows = ParameterSpans(setup).ToArray();
        return rows.Where(row => row.Error.Length > 0).Select(row => $"Parameter ‘{(string.IsNullOrWhiteSpace(row.Key) ? "unnamed" : row.Key)}’: {row.Error}").FirstOrDefault()
            ?? FirstError(PhaseParameterContract.messages(PhaseIssues(setup)), FunctionInputsError(setup, true));
    }
    private string QueryContractError(bool setup, string[] keys)
    {
        var outside = Requirements(setup) is { } required ? keys.Where(key => !required.Any(value => value.Name == key)).ToArray() : [];
        if (setup && HasSetupContract && outside.Length > 0)
            return $"The source project requires parameters outside the setup function’s contract: {string.Join(", ", outside)}. Choose a compatible source project or change the setup function on the module.";
        var defaults = (setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(value => value.keyName);
        var overlap = keys.Intersect(defaults, StringComparer.Ordinal).ToArray();
        if (overlap.Length > 0) return $"Query keys require spans and cannot have defaults: {string.Join(", ", overlap)}. Deselect these query keys or exclude their defaults on the project.";
        return outside.Length == 0 ? "" : $"Query keys must be required inputs of the {(setup ? "setup" : "results")} function. Unsupported keys: {string.Join(", ", outside)}. Update the query keys or select a compatible function on the module.";
    }
    public string ResultsQueryKeysError => FirstError(KeysError(EffectiveOutputKeys), QueryContractError(false, EffectiveOutputKeys));
    public string SetupQueryKeysError => FirstError(KeysError(EffectiveSetupKeys), FirstError(QueryContractError(true, EffectiveSetupKeys), SourceQueryContractError()));
    public string ResultsParametersError => ParametersError(false);
    public string SetupParametersError => ParametersError(true);
    public string OutputTypesSummary => EffectiveOutputs.Length == 0 ? "None" : "";
    public string SetupOutputTypesSummary => EffectiveSetupOutputs.Length == 0 ? "None" : "";
    public string ResultsQueryKeysSummary => Summary(EffectiveOutputKeys);
    public string SetupQueryKeysSummary => Summary(EffectiveSetupKeys);
    private static string Summary(IEnumerable<string> values) { var items = values.ToArray(); return items.Length == 0 ? "None" : string.Join(", ", items); }
    internal IEnumerable<(string Section, string Message)> ValidationIssues
    {
        get
        {
            var errors = new (string Section, string Message)[] {
                ("Results settings", ResultsDatabaseError), ("Results settings", ResultsFunctionError),
                ("Results settings", OutputTypesError), ("Results settings", ResultsQueryKeysError),
                ("Results parameter defaults", ResultsParameterDefaultsError),
                ("Setup settings", IsBranch ? SetupDatabaseError : ""), ("Setup settings", IsBranch ? SetupFunctionError : ""),
                ("Setup settings", IsBranch ? SetupDataTypesError : ""), ("Setup settings", IsBranch ? SetupQueryKeysError : ""),
                ("Setup parameter defaults", IsBranch ? SetupParameterDefaultsError : ""),
                ("Results parameter spans", IsRun ? ResultsParametersError : ""),
                ("Setup parameter spans", IsRun && IsBranch ? SetupParametersError : ""),
                ("Evolution reporting", ReportingError) };
            return errors.Where(error => error.Message.Length > 0);
        }
    }
    public string ValidationError => ValidationIssues.Select(issue => $"{issue.Section}: {issue.Message}").FirstOrDefault() ?? "";
    internal event Action? Changed;
    private void ListChanged(WorkspaceListOverride local)
    {
        if (loading) return;
        local.MarkLocal(); NotifyChanged();
    }
    private void NotifyChanged() { if (!loading) { RefreshInherited(); Changed?.Invoke(); } }
    internal void RefreshInherited()
    {
        resultsPreview = setupPreview = null;
        RefreshContractRows();
        SynchronizeQuerySpans();
        foreach (var state in setupSourceOverrides.Values) { state.IsReadOnly = IsReadOnly; state.RefreshInherited(); }
        foreach (var field in properties.Values) field.RefreshInherited();
        ResultsDatabaseField.RefreshInherited(); SetupDatabaseField.RefreshInherited();
        foreach (var span in ResultsParameters.Concat(SetupParameters)) span.RefreshInherited();
        foreach (var row in SetupDataTypes) row.RefreshSources();
        foreach (var row in ResultsParameterDefaults.Concat(SetupParameterDefaults)) row.RefreshSource();
        OnPropertyChanged("");
        AddRequiredResultsDefaultsCommand?.RaiseCanExecuteChanged(); AddRequiredSetupDefaultsCommand?.RaiseCanExecuteChanged();
        AddResultsParameterDefaultCommand?.RaiseCanExecuteChanged(); AddSetupParameterDefaultCommand?.RaiseCanExecuteChanged();
    }
    private void Quietly(Action action) { var wasLoading = loading; loading = true; try { action(); } finally { loading = wasLoading; } }
    private void AddSpan(ObservableCollection<WorkspaceParameterSpan> rows, string key, IEnumerable<string> values, parameterValueType? storedType = null)
    {
        WorkspaceParameterSpan? span = null;
        span = new(key, values, item => rows.Remove(item), name => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults : EffectiveOutputDefaults).FirstOrDefault(value => value.keyName == name),
            () => Requirements(ReferenceEquals(rows, SetupParameters)), () => ParameterSupport(ReferenceEquals(rows, SetupParameters)),
            () => QueryKeys(ReferenceEquals(rows, SetupParameters)), () => IsEditable, () => span is not null && IsRepairSpan(rows, span), storedType,
            () => PhaseIssues(ReferenceEquals(rows, SetupParameters)).Where(issue => issue.Key == span!.Key));
        rows.Add(span);
    }
    // Raw draft input is also used when importing old files; the normal editor only edits values.
    internal void AddDraftSpan(bool setup, string key = "", string[]? values = null) => AddSpan(setup ? SetupParameters : ResultsParameters, key, values ?? [""]);
    private void AddMissing(ObservableCollection<WorkspaceParameterSpan> rows, IEnumerable<string>? keys)
    { foreach (var key in MissingKeys(rows, keys)) AddSpan(rows, key, []); }
    private void SynchronizeQuerySpans()
    {
        if (!IsRun || !IsEditable || loading) return;
        Quietly(() => { AddMissing(ResultsParameters, QueryKeys(false)); if (IsBranch) AddMissing(SetupParameters, QueryKeys(true)); });
    }
    private void AddDefault(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto value)
    {
        var setup = ReferenceEquals(rows, SetupParameterDefaults);
        var row = new WorkspaceParameterBinding(value.keyName, (parameterValueType)value.valueType, value.value, item => rows.Remove(item), key => LocalParameterSource(key, setup),
            () => (ReferenceEquals(rows, SetupParameterDefaults) ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(binding => binding.keyName),
            key => (ReferenceEquals(rows, SetupParameterDefaults) ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults)?.FirstOrDefault(binding => binding.keyName == key),
            () => Requirements(setup), () => IsProject ? QueryKeys(setup) : [], () => !IsReadOnly, () => ParameterSupport(setup));
        void ActivateEditedDefault()
        {
            // Editing this row must not restore unrelated, retained exclusions.
            if (!loading && !QueryKeys(setup).Contains(row.Key)) (setup ? excludedSetupDefaults : excludedResultsDefaults).Remove(row.Key);
        }
        row.Changed += ActivateEditedDefault;
        ActivateEditedDefault();
        rows.Add(row);
    }
    private void LoadDefaults(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto[] values)
    { rows.Clear(); foreach (var value in values) AddDefault(rows, value); }
    public void LoadProject(projectSettingsDto value)
    {
        if (!IsProject) throw new InvalidOperationException("Project settings belong to a project.");
        LoadLocalSettings(new projectLocalSettingsDto {
        description = value.description, resultsDatabaseName = value.resultsDatabaseName, resultsParameterDefaults = value.resultsParameterDefaults,
        resultsQueries = IsProject ? value.resultsDataTypes : null, setupQueries = IsProject && IsBranch ? (value.setupDataTypes ?? []).Select(row => new resultsDataTypeConfigDto { dataType = row.dataType, queryParameterKeys = row.queryParameterKeys }).ToArray() : null,
        setupDatabaseName = IsBranch ? value.setupDatabaseName : null, setupParameterDefaults = IsBranch ? value.setupParameterDefaults : null,
        setupSources = IsBranch ? (value.setupDataTypes ?? []).Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId }).ToArray() : null
        });
    }
    public void LoadModuleOverrides(moduleOverridesDto value)
    {
        RequireEditableDefaults(IsModule, "Module overrides belong to a module.");
        Quietly(() =>
        {
            ResultsFunctionOverride.Load(value.resultsFunctionName); SetupFunctionOverride.Load(IsBranch ? value.setupFunctionName : null);
            LoadDefaultValues(value.description, value.resultsParameterDefaults, value.setupParameterDefaults, value.setupSources,
                value.excludedResultsDefaults, value.excludedSetupDefaults);
        });
        NotifyChanged();
    }
    public void LoadModule(moduleSettingsDto value) => LoadModuleOverrides(new moduleOverridesDto {
        description = value.description, resultsFunctionName = value.resultsFunctionName, resultsParameterDefaults = value.resultsParameterDefaults,
        setupFunctionName = IsBranch ? value.setupFunctionName : null, setupParameterDefaults = IsBranch ? value.setupParameterDefaults : null,
        setupSources = IsBranch ? value.setupSources : null
    });
    private void RequireEditableDefaults(bool correctOwner, string message)
    {
        if (!correctOwner) throw new InvalidOperationException(message);
        if (IsReadOnly) throw new InvalidOperationException("Settings are locked while generated run parameter files exist.");
    }
    private void LoadDefaultValues(string? description, parameterBindingDto[]? resultsDefaults, parameterBindingDto[]? setupDefaults,
        setupSourceConfigDto[]? sources, string[]? resultsExclusions, string[]? setupExclusions)
    {
        DescriptionOverride.Load(description);
        LoadDefaultExclusions(resultsExclusions, setupExclusions);
        LoadSetupSources(sources);
        LoadDefaults(ResultsParameterDefaults, resultsDefaults ?? []); LoadDefaults(SetupParameterDefaults, setupDefaults ?? []);
        ResultsParameterDefaultsOverride.Load(resultsDefaults is not null); SetupParameterDefaultsOverride.Load(IsBranch && setupDefaults is not null);
    }
    public void LoadLocalSettings(projectLocalSettingsDto value)
    {
        RequireEditableDefaults(IsProject, "Project settings belong to a project.");
        Quietly(() =>
        {
            ResultsDatabaseField.Load(value.resultsDatabaseName ?? "Results");
            SetupDatabaseField.Load(IsBranch ? value.setupDatabaseName ?? "Setup" : null);
            resultsQueries = value.resultsQueries ?? []; setupQueries = IsBranch ? value.setupQueries ?? [] : [];
            LoadDefaultValues(value.description, value.resultsParameterDefaults, value.setupParameterDefaults, value.setupSources,
                value.excludedResultsDefaults, value.excludedSetupDefaults);
        });
        NotifyChanged();
    }
    private void LoadSetupDataTypes(setupDataTypeConfigDto[] values)
    {
        SetupDataTypes.Clear(); foreach (var value in values) SetupDataTypes.Add(new(value, sources, SetupSourceOverride(value.dataType)));
    }
    public void LoadRun(runDto value)
    {
        if (!IsRun) throw new InvalidOperationException("Run inputs belong to a run.");
        if (value.runKind != (IsBranch ? "branch" : "root")) throw new ArgumentException("A project can only contain runs of its own type.");
        Quietly(() =>
        {
            DescriptionOverride.Load(value.descriptionOverride);
            reportingEnabled = value.reporting is not null;
            snapshotInterval = (value.sgd?.Value.SnapshotInterval ?? SgdRun.defaults.SnapshotInterval).ToString();
            snapshotCount = (value.sgd?.Value.SnapshotCount ?? SgdRun.defaults.SnapshotCount).ToString();
            tempSnapshotInterval = SgdRun.tempInterval(value.sgd?.Value ?? SgdRun.defaults).ToString();
            SnapshotReporting.Load(value.reporting?.Value.SnapshotSchedule ?? SgdReporting.defaults.SnapshotSchedule);
            SummaryReporting.Load(value.reporting?.Value.SummarySchedule ?? SgdReporting.defaults.SummarySchedule);
            ResultsParameters.Clear(); foreach (var span in value.resultsParameterSpans) AddSpan(ResultsParameters, span.keyName, span.values, span.valueType is { } type ? (parameterValueType)type : null);
            SetupParameters.Clear(); foreach (var span in value.setupParameterSpans) AddSpan(SetupParameters, span.keyName, span.values, span.valueType is { } type ? (parameterValueType)type : null);
        });
        NotifyChanged();
    }
    // Source lookup depends only on published results. Do not traverse another
    // project's setup sources while building source choices (which may form a draft cycle).
    internal projectSettingsDto ToSourceSettings() => new()
    {
        resultsDatabaseName = ResultsDatabaseName, resultsFunctionName = ResultsFunctionName, resultsFunctionVersion = ResultsFunctionVersion,
        resultsDataTypes = EffectiveResultsDataTypes, resultsParameterDefaults = EffectiveOutputDefaults
    };
    internal projectSettingsDto ToProjectSettings() => new()
    {
        description = Description, resultsDatabaseName = ResultsDatabaseName, resultsFunctionName = ResultsFunctionName, resultsFunctionVersion = ResultsFunctionVersion,
        resultsDataTypes = EffectiveResultsDataTypes, resultsParameterDefaults = EffectiveOutputDefaults, setupParameterDefaults = IsBranch ? EffectiveSetupDefaults : [],
        setupDatabaseName = IsBranch ? SetupDatabaseName : "", setupFunctionName = IsBranch ? SetupFunctionName : "", setupFunctionVersion = IsBranch ? SetupFunctionVersion : "",
        setupDataTypes = IsBranch ? EffectiveSetupDataTypes : []
    };
    internal moduleSettingsDto ToModuleSettings() => new()
    {
        description = Description, resultsFunctionName = ResultsFunctionName, resultsParameterDefaults = EffectiveOutputDefaults,
        setupFunctionName = IsBranch ? SetupFunctionName : "", setupParameterDefaults = IsBranch ? EffectiveSetupDefaults : [],
        setupSources = IsBranch ? EffectiveSetupDataTypes.Where(row => NeedsSource(row.dataType))
            .Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId }).ToArray() : []
    };
    private parameterBindingDto[]? SaveDefaults(bool setup) => (setup ? SetupParameterDefaultsOverride : ResultsParameterDefaultsOverride).IsOverridden
        ? (setup ? LocalSetupParameterDefaults : LocalResultsParameterDefaults).Select(row => row.ToDto()).ToArray() : null;
    private string[] SaveExclusions(bool setup) => setup && !IsBranch ? [] : (setup ? excludedSetupDefaults : excludedResultsDefaults).ToArray();
    private setupSourceConfigDto[]? SaveSources() => IsBranch && LocalSetupSources.Length > 0 ? LocalSetupSources : null;
    internal projectLocalSettingsDto ToLocalSettings()
    {
        if (!IsProject) throw new InvalidOperationException("Project settings belong to a project.");
        return new() {
            description = DescriptionOverride.OverrideValue, resultsDatabaseName = ResultsDatabaseName,
            resultsQueries = EffectiveResultsDataTypes, setupQueries = IsBranch ? EffectiveSetupDataTypes.Select(row => new resultsDataTypeConfigDto { dataType = row.dataType, queryParameterKeys = row.queryParameterKeys }).ToArray() : null,
            excludedResultsDefaults = SaveExclusions(false), excludedSetupDefaults = SaveExclusions(true),
            resultsParameterDefaults = SaveDefaults(false), setupParameterDefaults = IsBranch ? SaveDefaults(true) : null,
            setupDatabaseName = IsBranch ? SetupDatabaseName : null, setupSources = SaveSources()
        };
    }
    internal moduleOverridesDto ToModuleOverrides()
    {
        if (!IsModule) throw new InvalidOperationException("Module overrides belong to a module.");
        return new() {
            description = DescriptionOverride.OverrideValue, resultsFunctionName = ResultsFunctionOverride.OverrideValue,
            setupFunctionName = IsBranch ? SetupFunctionOverride.OverrideValue : null,
            resultsParameterDefaults = SaveDefaults(false), setupParameterDefaults = IsBranch ? SaveDefaults(true) : null,
            excludedResultsDefaults = SaveExclusions(false), excludedSetupDefaults = SaveExclusions(true), setupSources = SaveSources()
        };
    }
    private bool PhaseVersionCurrent(bool setup) => setup
        ? !SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var definition) || definition.Version == SetupFunctionVersion
        : !ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var results) || results.Version == ResultsFunctionVersion;
    private bool FunctionVersionsCurrent => PhaseVersionCurrent(false) && (!IsBranch || PhaseVersionCurrent(true));
    private string ResultsFunctionVersion => frozenSettings?.resultsFunctionVersion ?? (!IsModule ? Parent?.ResultsFunctionVersion : null)
        ?? (ResultsFunctionCatalog.functions.TryGetValue(ResultsFunctionName, out var definition) ? definition.Version : "");
    private string SetupFunctionVersion => frozenSettings?.setupFunctionVersion ?? (!IsModule ? Parent?.SetupFunctionVersion : null)
        ?? (SetupFunctionCatalog.functions.TryGetValue(SetupFunctionName, out var definition) ? definition.Version : "");
    internal runDto ToRunInputs(Guid id, string name, Guid parentProjectId) => new()
    {
        schemaVersion = RunDtoSchema.version, id = id, parentProjectId = parentProjectId, runName = name, runKind = IsBranch ? "branch" : "root",
        descriptionOverride = DescriptionOverride.OverrideValue, resultsParameterSpans = ResultsParameters.Select(span => span.ToDto()).ToArray(),
        setupParameterSpans = IsBranch ? SetupParameters.Select(span => span.ToDto()).ToArray() : [],
        reporting = ReportingEnabled ? Microsoft.FSharp.Core.FSharpOption<sgdReportingConfig>.Some(new(UsesSnapshotControls ? $"Constant({SnapshotControls().SnapshotInterval})" : SnapshotReporting.Schedule, SummaryReporting.Schedule)) : null,
        sgd = UsesSnapshotControls ? Microsoft.FSharp.Core.FSharpOption<sgdRunConfig>.Some(SnapshotControls()) : null
    };
}
