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
    private string key;
    private readonly Func<string, parameterBindingDto?> defaultValue;
    private readonly Func<IEnumerable<string>> suggestedKeys;
    private string[] keyChoices = [];
    public WorkspaceParameterSpan(string key, IEnumerable<string> values, Action<WorkspaceParameterSpan> remove, Func<string, parameterBindingDto?> defaultValue,
        Func<IEnumerable<string>>? suggestedKeys = null, Func<paramKeyDescriptor[]?>? requirements = null)
    {
        this.key = key; this.defaultValue = defaultValue; this.suggestedKeys = suggestedKeys ?? (() => []);
        this.requirements = requirements ?? (() => null);
        Watch(Values, ValuesChanged);
        foreach (var value in values) AddValue(value);
        AddValueCommand = new RelayCommand(() => AddValue(""));
        RemoveCommand = new RelayCommand(() => remove(this));
        RefreshInherited();
    }
    private void ValuesChanged() { RefreshInherited(); NotifyChanged(); }
    private void AddValue(string value) => Values.Add(new(value, () => ValueType, item => Values.Remove(item), () => Key));
    public string[] KeyChoices => keyChoices;
    public bool HasRestrictedKeys => requirements() is not null;
    public string? SelectedKey
    {
        get => KeyChoices.Contains(Key, StringComparer.Ordinal) ? Key : null;
        set { if (value is not null && KeyChoices.Contains(value, StringComparer.Ordinal)) Key = value; }
    }

    public string Key { get => key; set { if (SetProperty(ref key, value)) ValuesChanged(); } }
    public ObservableCollection<WorkspaceTypedValue> Values { get; } = [];
    public RelayCommand AddValueCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public string Error => defaultValue(Key) is not null ? $"‘{Key}’ already has a default. Remove this span or exclude the default on the project."
        : string.IsNullOrWhiteSpace(Key) ? "Enter a parameter key."
        : requirements() is { } required && !required.Any(value => value.Name == Key) ? $"‘{Key}’ is not required by the selected function. Remove this span or choose one of its required keys."
        : Values.Count == 0 ? "Add at least one value. Blank values are allowed for text parameters."
        : Values.Select(value => value.Error).FirstOrDefault(error => error.Length > 0) ?? "";
    public parameterValueType ValueType => requirements()?.FirstOrDefault(value => value.Name == Key)?.ValueType
        ?? (defaultValue(Key) is { } value ? (parameterValueType)value.valueType : ParamKeys.valueType(Key));
    public string TypeLabel => ParameterValue.label(ValueType);
    public string ValueHint => WorkspaceParameterRules.Hint(Key, ValueType);
    public string DefaultHint => defaultValue(Key) is { } value ? $"Conflicts with default: {value.value}"
        : "Additional parameter; its values are included in the combinations.";
    internal void RefreshInherited()
    {
        var choices = (requirements() is { } required ? required.Select(value => value.Name) : WorkspaceParameterBinding.KnownKeys.Concat(suggestedKeys()).Append(Key))
            .Where(value => !string.IsNullOrWhiteSpace(value) && defaultValue(value) is null).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!keyChoices.SequenceEqual(choices)) keyChoices = choices;
        foreach (var value in Values) value.RefreshType();
        OnPropertyChanged("");
    }
    internal parameterSpanDto ToDto() => new() { keyName = Key, values = Values.Select(value => value.Text).ToArray(), valueType = (int)ValueType };
}

public sealed record WorkspaceOutputType(string Kind, string Text)
{
    public string TextSummary => string.IsNullOrEmpty(Text) ? "Text: (empty)" : $"Text: {Text}";
    internal sortopiaDataTypeDto ToDto() => new() { kind = Kind, value = Text };
}

/// <summary>A local value wins over the nearest ancestor; disabling it restores inheritance.</summary>
public abstract class WorkspaceOverrideState : WorkspaceFormItem
{
    private readonly Func<WorkspaceConfigurationViewModel?> inheritedSource;
    private readonly string ownerKind;
    private bool isOverridden, hasDraft;
    protected WorkspaceOverrideState(string title, string ownerKind, Func<WorkspaceConfigurationViewModel?> inheritedSource, bool canOverride = true, bool projectValue = false)
    { Title = title; this.ownerKind = ownerKind; this.inheritedSource = inheritedSource; this.canOverride = canOverride; IsProjectValue = projectValue; }
    public bool IsProjectValue { get; }
    public string Title { get; }
    private readonly bool canOverride;
    internal Func<bool>? FixedByFunction { get; set; }
    public bool IsReadOnly { get; internal set; }
    public bool CanOverride => canOverride && !IsReadOnly && !IsProjectValue && FixedByFunction?.Invoke() != true;
    public string OverrideCaption => ownerKind == "run" ? "Override for this run" : $"Override on this {ownerKind}";
    public bool IsOverridden
    {
        get => isOverridden;
        set
        {
            if (!CanOverride || isOverridden == value) return;
            if (value && !hasDraft) { SeedOverride(); hasDraft = true; }
            isOverridden = value;
            RefreshInherited(); NotifyChanged();
        }
    }
    public bool IsInherited => !IsOverridden;
    public bool CanEditValue => IsOverridden && !IsReadOnly && FixedByFunction?.Invoke() != true;
    public bool ShowPreview => !CanEditValue;
    public WorkspaceConfigurationViewModel? InheritedSource => IsInherited || FixedByFunction?.Invoke() == true ? inheritedSource() : null;
    public bool HasInheritedSource => InheritedSource is not null;
    public string SourceLabel => IsReadOnly ? "Fixed when this project was sealed" : FixedByFunction?.Invoke() == true ? "Defined by the selected function" : IsProjectValue ? "Defined on this project" : IsOverridden ? ownerKind == "run" ? "Overridden for this run" : $"Local override on this {ownerKind}"
        : InheritedSource is { } source ? $"Inherited from {source.Name}" : "Default value · no ancestor override";
    protected abstract void SeedOverride();
    internal void MarkLocal() { isOverridden = hasDraft = true; RefreshInherited(); }
    protected void LoadState(bool local) { isOverridden = hasDraft = local; RefreshInherited(); }
    internal virtual void RefreshInherited() => OnPropertyChanged("");
}

public sealed class WorkspaceOverrideField : WorkspaceOverrideState
{
    private readonly Func<string> inherited;
    private readonly bool required, requireInherited;
    private string value = "";
    public WorkspaceOverrideField(string title, bool required, Func<string> inherited, Func<WorkspaceConfigurationViewModel?> source,
        string ownerKind, bool canOverride = true, bool requireInherited = true, string[]? choices = null, bool projectValue = false) : base(title, ownerKind, source, canOverride, projectValue)
    { this.required = required; this.requireInherited = requireInherited; this.inherited = inherited; catalogChoices = choices ?? []; }
    private readonly string[] catalogChoices;
    internal Func<string[]>? AvailableChoices { get; set; }
    internal Func<bool>? SelectionEnabled { get; set; }
    public string[] Choices => AvailableChoices?.Invoke() ?? catalogChoices;
    public bool HasChoices => catalogChoices.Length > 0;
    public bool ShowCatalogPicker => HasChoices && CanOverride;
    public bool CanSelectValue => CanEditValue && SelectionEnabled?.Invoke() != false;
    public string? SelectedChoice
    {
        get => Choices.Contains(EffectiveValue, StringComparer.Ordinal) ? EffectiveValue : null;
        set
        {
            // A filtered ComboBox clears its selection when an item disappears. Keep that
            // draft value until the user chooses a replacement or restores inheritance.
            if (value is not null && Choices.Contains(value, StringComparer.Ordinal)) EffectiveValue = value;
        }
    }
    public bool IsMultiline => !required;
    protected override void SeedOverride() => value = inherited();
    public string EffectiveValue
    {
        get => IsOverridden ? value : inherited();
        set
        {
            if (IsReadOnly || !IsOverridden || this.value == value) return;
            this.value = value; RefreshInherited(); NotifyChanged();
        }
    }
    public string Error
    {
        get
        {
            if (!required || !(IsOverridden || requireInherited) || !string.IsNullOrWhiteSpace(EffectiveValue)) return "";
            var section = HasChoices ? "Choose functions" : Title.StartsWith("Setup", StringComparison.Ordinal) ? "Setup settings" : "Results settings";
            if (IsProjectValue) return $"Enter a {Title.ToLowerInvariant()} name in {section}. Database names are defined on this project.";
            var action = HasChoices ? $"choose a {Title.ToLowerInvariant()} from the catalog" : $"enter a {Title.ToLowerInvariant()}";
            if (IsOverridden) return $"{Title} is empty. In {section}, {action}."
                + (!string.IsNullOrWhiteSpace(inherited()) ? $" You can also clear ‘{OverrideCaption}’ to use the inherited value." : "");
            if (CanOverride) return $"No {Title.ToLowerInvariant()} is selected. Open {section}, check ‘{OverrideCaption}’, and {action}. You can also select the function on a parent module to share it with its projects.";
            return $"No {Title.ToLowerInvariant()} is selected. Select a parent module in the tree, click ‘{Title}’, enable ‘Override on this module’, and {action}. Functions can only be selected on modules.";
        }
    }
    public string? OverrideValue => IsOverridden ? value : null;
    internal void Load(string? input) { value = input ?? ""; LoadState(input is not null); }
    internal void SetLocal(string input) { if (IsReadOnly || (!CanOverride && !IsProjectValue)) return; value = input; MarkLocal(); NotifyChanged(); }
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
    private projectSettingsDto? frozenSettings;
    private WorkspaceParameterSetsPreview? resultsPreview, setupPreview;
    private WorkspaceParameterBinding[] activeResultsDefaults = [], activeSetupDefaults = [];
    private WorkspaceParameterSpan[] activeResultsSpans = [], activeSetupSpans = [];
    public WorkspaceEditorSections Sections { get; } = new();
    private readonly Func<IEnumerable<WorkspaceSourceProject>> sources;
    public bool IsReadOnly { get; private set; }
    public bool IsEditable => !IsReadOnly;
    internal void Freeze(projectSettingsDto? snapshot = null) { frozenSettings = snapshot ?? ToProjectSettings(); ResultsFunctionOverride.Load(frozenSettings.resultsFunctionName); SetupFunctionOverride.Load(IsBranch ? frozenSettings.setupFunctionName : null); IsReadOnly = true; foreach (var state in properties.Values) state.IsReadOnly = true; RefreshInherited(); }
    private readonly Dictionary<string, WorkspaceOverrideState> properties = [];
    public WorkspaceConfigurationViewModel(WorkspaceEntityKind kind, bool isBranch, Func<WorkspaceConfigurationViewModel?> parent, Func<string> name, Func<IEnumerable<WorkspaceSourceProject>>? sources = null, Func<IEnumerable<WorkspaceConfigurationViewModel>>? childEditors = null)
    {
        this.sources = sources ?? (() => []);
        this.childEditors = childEditors ?? (() => []);
        IsProject = kind == WorkspaceEntityKind.Project; IsModule = kind == WorkspaceEntityKind.Module;
        IsBranch = isBranch; this.parent = parent; this.name = name;
        var owner = kind.ToString().ToLowerInvariant();
        WorkspaceOverrideField Field(string key, string title, bool required, Func<string> inherited, bool canOverride = true, string[]? choices = null, bool projectValue = false)
        {
            var field = new WorkspaceOverrideField(title, required, inherited, () => Parent?.ValueSource(key), owner, canOverride, !IsModule, choices, projectValue);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        DescriptionOverride = Field(nameof(Description), "Description", false, () => Parent?.Description ?? "");
        ResultsDatabaseOverride = Field(nameof(ResultsDatabaseName), "Results database", !IsModule, () => IsRun ? Parent?.ResultsDatabaseName ?? "" : "Results", false, projectValue: IsProject);
        ResultsFunctionOverride = Field(nameof(ResultsFunctionName), "Results function", true, () => Parent?.ResultsFunctionName ?? "", IsModule,
            FunctionCatalogs.resultsFunctionCatalog.Keys.ToArray());
        SetupDatabaseOverride = Field(nameof(SetupDatabaseName), "Setup database", !IsModule, () => IsRun ? Parent?.SetupDatabaseName ?? "" : "Setup", false, projectValue: IsProject);
        if (IsProject) { ResultsDatabaseOverride.Load("Results"); if (IsBranch) SetupDatabaseOverride.Load("Setup"); }
        SetupFunctionOverride = Field(nameof(SetupFunctionName), "Setup function", true, () => Parent?.SetupFunctionName ?? "", IsModule, choices:
            FunctionCatalogs.setupFunctionCatalog.Keys.ToArray());
        ResultsFunctionOverride.AvailableChoices = () => IsModule ? CompatibleResultsFunctions : FunctionCatalogs.resultsFunctionCatalog.Keys.ToArray();
        ResultsFunctionOverride.SelectionEnabled = () => !IsModule || !IsBranch || HasSetupContract;
        WorkspaceListOverride List(string key, string title, Action seed)
        {
            var field = new WorkspaceListOverride(title, owner, () => key switch {
                nameof(OutputTypesOverride) when HasResultsContract => ValueSource(nameof(ResultsFunctionName)),
                nameof(SetupDataTypes) when SetupGeneratesAllOutputs => ValueSource(nameof(SetupFunctionName)),
                _ => Parent?.ValueSource(key)
            }, () => Quietly(seed), IsDefaults);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        OutputTypesOverride = List(nameof(OutputTypesOverride), "Results data types", () => { });
        SetupDataTypesOverride = List(nameof(SetupDataTypes), "Setup outputs and source projects", () => LoadSetupDataTypes(Parent?.EffectiveSetupDataTypes ?? []));
        OutputTypesOverride.FixedByFunction = () => true;
        SetupDataTypesOverride.FixedByFunction = () => SetupGeneratesAllOutputs;
        ResultsParameterDefaultsOverride = List(nameof(ResultsParameterDefaults), "Results parameter defaults", () => { });
        SetupParameterDefaultsOverride = List(nameof(SetupParameterDefaults), "Setup parameter defaults", () => { });
        WorkspaceFormItem.Watch(ResultsParameterDefaults, () => ListChanged(ResultsParameterDefaultsOverride));
        WorkspaceFormItem.Watch(SetupParameterDefaults, () => ListChanged(SetupParameterDefaultsOverride));
        AddResultsParameterDefaultCommand = new RelayCommand(() => { ResultsParameterDefaultsOverride.IsOverridden = true; AddDefault(ResultsParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults);
        AddSetupParameterDefaultCommand = new RelayCommand(() => { SetupParameterDefaultsOverride.IsOverridden = true; AddDefault(SetupParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults && IsBranch);
        AddRequiredResultsDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(false), () => CanAddRequiredDefaults(false));
        AddRequiredSetupDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(true), () => CanAddRequiredDefaults(true));
        WorkspaceFormItem.Watch(SetupDataTypes, () => ListChanged(SetupDataTypesOverride));
        WorkspaceFormItem.Watch(ResultsParameters, NotifyChanged); WorkspaceFormItem.Watch(SetupParameters, NotifyChanged);
        AddResultsParameterCommand = new RelayCommand(() => AddSpan(ResultsParameters, "", [""]));
        AddSetupParameterCommand = new RelayCommand(() => AddSpan(SetupParameters, "", [""]));
        AddMissingResultsParametersCommand = new RelayCommand(() => AddMissing(ResultsParameters, MissingOutputKeys), () => MissingOutputKeys.Length > 0);
        AddMissingSetupParametersCommand = new RelayCommand(() => AddMissing(SetupParameters, MissingSetupKeys), () => MissingSetupKeys.Length > 0);
    }
    private WorkspaceConfigurationViewModel? Parent => parent();
    internal string Name => name();
    private WorkspaceConfigurationViewModel? ValueSource(string property)
    {
        if (HasResultsContract && property is nameof(OutputTypesOverride)) property = nameof(ResultsFunctionName);
        if (SetupGeneratesAllOutputs && property == nameof(SetupDataTypes)) property = nameof(SetupFunctionName);
        return properties[property].IsOverridden ? this : Parent?.ValueSource(property);
    }
    public bool IsProject { get; }
    public bool IsModule { get; }
    public bool ShowDatabaseSettings => !IsModule;
    public bool IsDefaults => IsModule || IsProject;
    public bool CanEditDefaults => IsDefaults && IsEditable;
    public bool IsRun => !IsDefaults;
    public bool IsBranch { get; }
    public string Title => IsModule ? "Module defaults" : IsProject ? "Project defaults" : "Run inputs";
    public string Hint => IsReadOnly ? "Sealed project: settings were fixed when its first run was saved. Create a new version to change them." : IsDefaults
        ? "Parameter defaults inherit by key: local values replace matching keys and add new ones. Other properties follow their nearest ancestor override; other list overrides replace the whole list."
        : "Functions and defaults come from the sealed parent project. Override the description and supply spans for the functions’ required parameters.";
    public string Description { get => DescriptionOverride.EffectiveValue; set => DescriptionOverride.SetLocal(value); }
    public string ResultsDatabaseName { get => ResultsDatabaseOverride.EffectiveValue; set { if (IsProject) ResultsDatabaseOverride.SetLocal(value); } }
    public string ResultsFunctionName { get => frozenSettings?.resultsFunctionName ?? ResultsFunctionOverride.EffectiveValue; set { if (IsModule) ResultsFunctionOverride.SetLocal(value); } }
    public string SetupDatabaseName { get => SetupDatabaseOverride.EffectiveValue; set { if (IsProject) SetupDatabaseOverride.SetLocal(value); } }
    public string SetupFunctionName { get => frozenSettings?.setupFunctionName ?? SetupFunctionOverride.EffectiveValue; set { if (IsModule) SetupFunctionOverride.SetLocal(value); } }

    private paramKeyDescriptor[]? Requirements(bool setup) => !PhaseVersionCurrent(setup) ? null : setup
        ? FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var setupDefinition) ? setupDefinition.RequiredParameters.ToArray() : null
        : FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var resultsDefinition) ? resultsDefinition.RequiredParameters.ToArray() : null;
    public string ResultsRequirementsHint => RequirementsHint(false);
    public string SetupRequirementsHint => RequirementsHint(true);
    private string RequirementsHint(bool setup) => Requirements(setup) is { } required
        ? "Required parameters: " + string.Join(", ", required.Select(key => $"{key.Name} ({ParameterValue.label(key.ValueType)})")) + ". Supply each through exactly one default or run span. Defaults and spans cannot share keys. Each query key needs an explicit span."
        : "Select a catalog function on a module to define the required parameter keys.";
    private parameterBindingDto[] InheritedDefaults(bool setup)
    {
        var inherited = (setup ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults) ?? [];
        if ((setup ? SetupFunctionName == Parent?.SetupFunctionName : ResultsFunctionName == Parent?.ResultsFunctionName)) return inherited;
        var required = Requirements(setup);
        return required is null ? inherited : inherited.Where(value => required.Any(key => key.Name == value.keyName)).ToArray();
    }
    public WorkspaceOverrideField DescriptionOverride { get; }
    public WorkspaceOverrideField ResultsDatabaseOverride { get; }
    public WorkspaceOverrideField ResultsFunctionOverride { get; }
    public WorkspaceOverrideField SetupDatabaseOverride { get; }
    public WorkspaceOverrideField SetupFunctionOverride { get; }
    public WorkspaceListOverride OutputTypesOverride { get; }
    public WorkspaceListOverride SetupDataTypesOverride { get; }
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
        var channels = WorkspaceParameterInitialValues.ChannelCount(setup ? EffectiveSetupDefaults : EffectiveOutputDefaults);
        if (channels == 0) channels = WorkspaceParameterInitialValues.ChannelCount(setup ? EffectiveOutputDefaults : EffectiveSetupDefaults);
        var additions = missing.Select(key => new parameterBindingDto {
            keyName = key.Name, valueType = (int)key.ValueType, value = WorkspaceParameterInitialValues.For(key, channels)
        }).ToArray();
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
    private static parameterBindingDto[] MergeDefaults(parameterBindingDto[] inherited, IEnumerable<WorkspaceParameterBinding> rows)
    {
        var local = rows.Select(row => row.ToDto()).ToArray();
        var inheritedKeys = inherited.Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        return inherited.Select(value => local.FirstOrDefault(binding => binding.keyName == value.keyName) ?? value)
            .Concat(local.Where(value => !inheritedKeys.Contains(value.keyName))).ToArray();
    }
    private parameterBindingDto[] EffectiveOutputDefaults => frozenSettings?.resultsParameterDefaults ?? MergeDefaults(InheritedDefaults(false), LocalResultsParameterDefaults).Where(value => !excludedResultsDefaults.Contains(value.keyName)).ToArray();
    private parameterBindingDto[] EffectiveSetupDefaults => frozenSettings?.setupParameterDefaults ?? MergeDefaults(InheritedDefaults(true), LocalSetupParameterDefaults).Where(value => !excludedSetupDefaults.Contains(value.keyName)).ToArray();
    private static string DefaultsError(parameterBindingDto[] values)
    {
        if (values.Any(value => string.IsNullOrWhiteSpace(value.keyName))) return "Enter a key for each default parameter.";
        if (values.Select(value => value.keyName).Distinct(StringComparer.Ordinal).Count() != values.Length) return "Default parameter keys must be unique.";
        return values.Select(value => (value.keyName, Error: WorkspaceParameterRules.Error(value.keyName, (parameterValueType)value.valueType, value.value)))
            .Where(value => value.Error.Length > 0).Select(value => $"Parameter ‘{value.keyName}’: {value.Error}").FirstOrDefault() ?? "";
    }
    private string ParameterDefaultsError(bool setup)
    {
        var local = setup ? LocalSetupParameterDefaults : LocalResultsParameterDefaults;
        var localError = IsReadOnly ? "" : local.Where(row => row.Error.Length > 0).Select(row => $"Parameter ‘{row.Key}’: {row.Error}").FirstOrDefault()
            ?? DefaultsError(local.Select(row => row.ToDto()).ToArray());
        var values = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var required = Requirements(setup);
        var outside = required is null ? null : values.FirstOrDefault(value => !required.Any(key => key.Name == value.keyName));
        return localError.Length > 0 ? localError : outside is not null
            ? $"Parameter ‘{outside.keyName}’ is not required by function ‘{(setup ? SetupFunctionName : ResultsFunctionName)}’. Remove its local default or choose a required key."
            : FirstError(DefaultsError(values), FunctionInputsError(setup, false));
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
            var origin = ReferenceEquals(source, this) ? new WorkspaceParameterValueSource("Fixed when this project was sealed", null)
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
    public string ResultsParameterSetsSummary => $"{SetCount(ActiveResultsParameters)} execution parameter set(s). Defaults stay fixed; span keys must be different from default keys.";
    public string SetupParameterSetsSummary => $"{SetCount(ActiveSetupParameters)} setup parameter set(s). Defaults stay fixed; span keys must be different from default keys.";
    public WorkspaceParameterSetsPreview ResultsParameterSetsPreview => !IsRun ? WorkspaceParameterSetsPreview.Empty
        : resultsPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveOutputDefaults, ActiveResultsParameters,
            FirstError(ResultsParameterDefaultsError, ResultsParametersError));
    public WorkspaceParameterSetsPreview SetupParameterSetsPreview => !IsRun || !IsBranch ? WorkspaceParameterSetsPreview.Empty
        : setupPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveSetupDefaults, ActiveSetupParameters,
            FirstError(SetupParameterDefaultsError, SetupParametersError));
    private static string FirstError(params string[] errors) => errors.FirstOrDefault(error => error.Length > 0) ?? "";
    public string ResultsSettingsError => FirstError(ResultsDatabaseError, ResultsFunctionError, OutputTypesError, ResultsQueryKeysError);
    public string SetupSettingsError => FirstError(SetupDatabaseError, SetupFunctionError, SetupDataTypesError, SetupQueryKeysError);
    public ObservableCollection<WorkspaceSetupDataTypeEntry> SetupDataTypes { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> ResultsParameters { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> SetupParameters { get; } = [];
    private IEnumerable<WorkspaceParameterSpan> ActiveParameters(bool setup)
    {
        var rows = setup ? SetupParameters : ResultsParameters;
        IEnumerable<WorkspaceParameterSpan> active = Requirements(setup) is { } required
            ? rows.Where(row => string.IsNullOrWhiteSpace(row.Key) || required.Any(key => key.Name == row.Key)) : rows;
        return setup ? StableRows(active, ref activeSetupSpans) : StableRows(active, ref activeResultsSpans);
    }
    public IEnumerable<WorkspaceParameterSpan> ActiveResultsParameters => ActiveParameters(false);
    public IEnumerable<WorkspaceParameterSpan> ActiveSetupParameters => ActiveParameters(true);
    public RelayCommand AddResultsParameterCommand { get; }
    public RelayCommand AddSetupParameterCommand { get; }
    public RelayCommand AddMissingResultsParametersCommand { get; }
    public RelayCommand AddMissingSetupParametersCommand { get; }
    private string[] QueryKeys(bool setup) => setup ? EffectiveSetupKeys : EffectiveOutputKeys;
    private sortopiaDataTypeDto[] EffectiveOutputs => EffectiveResultsDataTypes.Select(output => output.dataType).ToArray();
    private string[] EffectiveOutputKeys => EffectiveResultsDataTypes.SelectMany(output => output.queryParameterKeys).Distinct(StringComparer.Ordinal).ToArray();
    private setupDataTypeConfigDto[] EffectiveSetupDataTypes => (IsRun ? Parent?.EffectiveSetupDataTypes : null) ?? frozenSettings?.setupDataTypes ?? ResolveSetupTypes(SetupDataTypesOverride.IsOverridden ? SetupDataTypes.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveSetupDataTypes ?? []);
    public IEnumerable<WorkspaceSetupDataTypeEntry> InheritedSetupDataTypes => EffectiveSetupDataTypes.Select(value =>
    {
        var row = new WorkspaceSetupDataTypeEntry(value, sources);
        if (HasSetupContract) row.SetContract(NeedsSource(value.dataType), IsModule, value.queryParameterKeys, Requirements(true)?.Select(key => key.Name).ToArray());
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
    private string[] RequiredSetupKeys => EffectiveSetupKeys.Concat(EffectiveSetupDataTypes.Where(row => NeedsSource(row.dataType)).SelectMany(row =>
        sources().FirstOrDefault(p => p.Id == row.sourceProjectId)?.Settings.resultsDataTypes?.FirstOrDefault(output => SameType(output.dataType, row.dataType))?.queryParameterKeys ?? [])).Distinct(StringComparer.Ordinal).ToArray();
    public string ResultsDatabaseError => ResultsDatabaseOverride.Error;
    public string ResultsFunctionError => FirstError(ResultsFunctionOverride.Error, ResultsFunctionName.Length > 0 && !HasResultsContract ? $"Unknown results function ‘{ResultsFunctionName}’. Choose a registered function on the module." : "", FunctionCompatibilityError);
    public string SetupDatabaseError => SetupDatabaseOverride.Error;
    public string SetupFunctionError => FirstError(SetupFunctionOverride.Error, SetupFunctionName.Length > 0 && !HasSetupContract ? $"Unknown setup function ‘{SetupFunctionName}’. Choose a registered function on the module." : "");
    private static string KeysError(string[] values) => values.Any(string.IsNullOrWhiteSpace) ? "Enter a name for each query key, or remove its row."
        : values.Distinct(StringComparer.Ordinal).Count() != values.Length ? "Query keys must be unique." : "";
    private static string[] MissingKeys(IEnumerable<WorkspaceParameterSpan> spans, IEnumerable<string>? keys) =>
        keys?.Where(key => !string.IsNullOrWhiteSpace(key) && !spans.Any(span => span.Key == key)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private string[] MissingInputKeys(bool setup) => MissingKeys(setup ? SetupParameters : ResultsParameters,
        QueryKeys(setup).Concat((Requirements(setup)?.Select(key => key.Name) ?? (setup ? RequiredSetupKeys : EffectiveOutputKeys))
            .Except((setup ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(value => value.keyName), StringComparer.Ordinal)));
    private string[] MissingOutputKeys => MissingInputKeys(false);
    private string[] MissingSetupKeys => MissingInputKeys(true);
    private string ParametersError(bool setup)
    {
        var rows = ActiveParameters(setup).ToArray();
        var defaults = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var required = Requirements(setup)?.Select(key => key.Name) ?? QueryKeys(setup).Concat(defaults.Select(value => value.keyName)).Concat(rows.Select(row => row.Key));
        return rows.Where(row => row.Error.Length > 0).Select(row => $"Parameter ‘{(string.IsNullOrWhiteSpace(row.Key) ? "unnamed" : row.Key)}’: {row.Error}").FirstOrDefault()
            ?? (rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length ? "Parameter keys must be unique."
            : FirstError(ParameterSets.inputKeysError(ListModule.OfSeq(required), ListModule.OfArray(QueryKeys(setup)),
                ListModule.OfSeq(defaults.Select(value => value.keyName)), ListModule.OfSeq(rows.Select(row => row.Key))), FunctionInputsError(setup, true)));
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
    public bool HasMissingResultsParameters => MissingOutputKeys.Length > 0;
    public bool HasMissingSetupParameters => MissingSetupKeys.Length > 0;
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
                ("Setup parameter spans", IsRun && IsBranch ? SetupParametersError : "") };
            return errors.Where(error => error.Message.Length > 0);
        }
    }
    public string ValidationError => ValidationIssues.Select(issue => $"{issue.Section}: {issue.Message}").FirstOrDefault() ?? "";
    internal event Action? Changed;
    private void ListChanged(WorkspaceListOverride local)
    {
        if (loading) return;
        if (ReferenceEquals(local, ResultsParameterDefaultsOverride)) excludedResultsDefaults.ExceptWith(ResultsParameterDefaults.Select(row => row.Key));
        if (ReferenceEquals(local, SetupParameterDefaultsOverride)) excludedSetupDefaults.ExceptWith(SetupParameterDefaults.Select(row => row.Key));
        local.MarkLocal(); NotifyChanged();
    }
    private void NotifyChanged() { if (!loading) { RefreshInherited(); Changed?.Invoke(); } }
    internal void RefreshInherited()
    {
        resultsPreview = setupPreview = null;
        RefreshContractRows();
        foreach (var field in properties.Values) field.RefreshInherited();
        foreach (var span in ResultsParameters.Concat(SetupParameters)) span.RefreshInherited();
        foreach (var row in SetupDataTypes) row.RefreshSources();
        foreach (var row in ResultsParameterDefaults.Concat(SetupParameterDefaults)) row.RefreshSource();
        OnPropertyChanged("");
        AddMissingResultsParametersCommand?.RaiseCanExecuteChanged(); AddMissingSetupParametersCommand?.RaiseCanExecuteChanged();
        AddRequiredResultsDefaultsCommand?.RaiseCanExecuteChanged(); AddRequiredSetupDefaultsCommand?.RaiseCanExecuteChanged();
        AddResultsParameterDefaultCommand?.RaiseCanExecuteChanged(); AddSetupParameterDefaultCommand?.RaiseCanExecuteChanged();
    }
    private void Quietly(Action action) { var wasLoading = loading; loading = true; try { action(); } finally { loading = wasLoading; } }
    private void AddSpan(ObservableCollection<WorkspaceParameterSpan> rows, string key, IEnumerable<string> values) =>
        rows.Add(new(key, values, item => rows.Remove(item), name => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults : EffectiveOutputDefaults).FirstOrDefault(value => value.keyName == name),
            () => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults.Select(value => value.keyName).Concat(RequiredSetupKeys)
                : EffectiveOutputDefaults.Select(value => value.keyName).Concat(EffectiveOutputKeys)).Concat(rows.Select(row => row.Key)),
            () => Requirements(ReferenceEquals(rows, SetupParameters))));
    private void AddMissing(ObservableCollection<WorkspaceParameterSpan> rows, IEnumerable<string>? keys)
    { foreach (var key in MissingKeys(rows, keys)) AddSpan(rows, key, []); }
    private void AddDefault(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto value) =>
        rows.Add(new(value.keyName, (parameterValueType)value.valueType, value.value, item => rows.Remove(item), key => LocalParameterSource(key, ReferenceEquals(rows, SetupParameterDefaults)),
            () => (ReferenceEquals(rows, SetupParameterDefaults) ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(binding => binding.keyName),
            key => (ReferenceEquals(rows, SetupParameterDefaults) ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults)?.FirstOrDefault(binding => binding.keyName == key),
            () => Requirements(ReferenceEquals(rows, SetupParameterDefaults)),
            () => IsProject ? childEditors().SelectMany(child => child.ActiveParameters(ReferenceEquals(rows, SetupParameterDefaults))).Select(span => span.Key) : []));
    private void LoadDefaults(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto[] values)
    { rows.Clear(); foreach (var value in values) AddDefault(rows, value); }
    public void LoadProject(projectSettingsDto value)
    {
        if (IsModule) Quietly(() => { ResultsFunctionOverride.Load(value.resultsFunctionName); SetupFunctionOverride.Load(IsBranch ? value.setupFunctionName : null); });
        LoadOverrides(new projectOverridesDto {
        description = value.description, resultsDatabaseName = value.resultsDatabaseName, resultsParameterDefaults = value.resultsParameterDefaults,
        resultsQueries = IsProject ? value.resultsDataTypes : null, setupQueries = IsProject && IsBranch ? (value.setupDataTypes ?? []).Select(row => new resultsDataTypeConfigDto { dataType = row.dataType, queryParameterKeys = row.queryParameterKeys }).ToArray() : null,
        setupDatabaseName = IsBranch ? value.setupDatabaseName : null, setupParameterDefaults = IsBranch ? value.setupParameterDefaults : null,
        setupSources = IsBranch ? (value.setupDataTypes ?? []).Where(row => row.sourceProjectId != Guid.Empty).Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId }).ToArray() : null
        });
    }
    public void LoadModuleOverrides(moduleOverridesDto value)
    {
        Quietly(() => { ResultsFunctionOverride.Load(value.resultsFunctionName); SetupFunctionOverride.Load(IsBranch ? value.setupFunctionName : null); });
        LoadOverrides(ModuleOverridesDto.toProjectOverrides(value));
    }
    public void LoadOverrides(projectOverridesDto value)
    {
        LoadParameterPolicy(value);
        if (IsReadOnly) throw new InvalidOperationException("Create a new project version to change sealed settings.");
        if (!IsDefaults) throw new InvalidOperationException("Defaults belong to a module or project.");
        Quietly(() =>
        {
            DescriptionOverride.Load(value.description); ResultsDatabaseOverride.Load(IsProject ? value.resultsDatabaseName ?? "Results" : null);
            SetupDatabaseOverride.Load(IsProject && IsBranch ? value.setupDatabaseName ?? "Setup" : null);
            LoadSetupDataTypes((value.setupSources ?? []).Select(row => new setupDataTypeConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId, queryParameterKeys = [] }).ToArray());
            LoadDefaults(ResultsParameterDefaults, value.resultsParameterDefaults ?? []); LoadDefaults(SetupParameterDefaults, value.setupParameterDefaults ?? []);
            ResultsParameterDefaultsOverride.Load(value.resultsParameterDefaults is not null); SetupParameterDefaultsOverride.Load(IsBranch && value.setupParameterDefaults is not null);
            SetupDataTypesOverride.Load(IsBranch && value.setupSources is not null);
        });
        NotifyChanged();
    }
    private void LoadSetupDataTypes(setupDataTypeConfigDto[] values)
    {
        SetupDataTypes.Clear(); foreach (var value in values) SetupDataTypes.Add(new(value, sources));
    }
    public void LoadRun(runDto value)
    {
        if (!IsRun) throw new InvalidOperationException("Run inputs belong to a run.");
        if (value.runKind != (IsBranch ? "branch" : "root")) throw new ArgumentException("A project can only contain runs of its own type.");
        Quietly(() =>
        {
            DescriptionOverride.Load(value.descriptionOverride);
            ResultsParameters.Clear(); foreach (var span in value.resultsParameterSpans) AddSpan(ResultsParameters, span.keyName, span.values);
            SetupParameters.Clear(); foreach (var span in value.setupParameterSpans) AddSpan(SetupParameters, span.keyName, span.values);
        });
        NotifyChanged();
    }
    internal projectSettingsDto ToProjectSettings() => new()
    {
        description = Description, resultsDatabaseName = ResultsDatabaseName, resultsFunctionName = ResultsFunctionName, resultsFunctionVersion = ResultsFunctionVersion,
        resultsDataTypes = EffectiveResultsDataTypes, resultsSortopiaDataTypes = EffectiveOutputs, resultsQueryParameterKeys = EffectiveOutputKeys, resultsParameterDefaults = EffectiveOutputDefaults, setupParameterDefaults = IsBranch ? EffectiveSetupDefaults : [],
        setupDatabaseName = IsBranch ? SetupDatabaseName : "", setupFunctionName = IsBranch ? SetupFunctionName : "", setupFunctionVersion = IsBranch ? SetupFunctionVersion : "",
        setupDataTypes = IsBranch ? EffectiveSetupDataTypes : []
    };
    internal projectOverridesDto ToLocalOverrides() => new()
    {
        description = DescriptionOverride.OverrideValue, resultsDatabaseName = IsProject ? ResultsDatabaseName : null,
        resultsQueries = IsProject ? EffectiveResultsDataTypes : null, setupQueries = IsProject && IsBranch ? EffectiveSetupDataTypes.Select(row => new resultsDataTypeConfigDto { dataType = row.dataType, queryParameterKeys = row.queryParameterKeys }).ToArray() : null,
        excludedResultsDefaults = excludedResultsDefaults.ToArray(), excludedSetupDefaults = IsBranch ? excludedSetupDefaults.ToArray() : [],
        resultsParameterDefaults = ResultsParameterDefaultsOverride.IsOverridden ? LocalResultsParameterDefaults.Select(row => row.ToDto()).ToArray() : null,
        setupParameterDefaults = IsBranch && SetupParameterDefaultsOverride.IsOverridden ? LocalSetupParameterDefaults.Select(row => row.ToDto()).ToArray() : null,
        setupDatabaseName = IsProject && IsBranch ? SetupDatabaseName : null,
        setupSources = IsBranch && SetupDataTypesOverride.IsOverridden ? EffectiveSetupDataTypes.Where(row => NeedsSource(row.dataType)).Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId }).ToArray() : null
    };
    internal moduleOverridesDto ToModuleOverrides()
    {
        var value = ModuleOverridesDto.fromProjectOverrides(ToLocalOverrides());
        value.resultsFunctionName = ResultsFunctionOverride.OverrideValue;
        value.setupFunctionName = IsBranch ? SetupFunctionOverride.OverrideValue : null;
        return value;
    }
    private bool PhaseVersionCurrent(bool setup) => setup
        ? !FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var definition) || definition.Version == SetupFunctionVersion
        : !FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var results) || results.Version == ResultsFunctionVersion;
    private bool FunctionVersionsCurrent => PhaseVersionCurrent(false) && (!IsBranch || PhaseVersionCurrent(true));
    private string ResultsFunctionVersion => frozenSettings?.resultsFunctionVersion ?? (!IsModule ? Parent?.ResultsFunctionVersion : null)
        ?? (FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var definition) ? definition.Version : "");
    private string SetupFunctionVersion => frozenSettings?.setupFunctionVersion ?? (!IsModule ? Parent?.SetupFunctionVersion : null)
        ?? (FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var definition) ? definition.Version : "");
    internal runDto ToRunInputs(Guid id, string name, Guid parentProjectId) => new()
    {
        schemaVersion = 14, id = id, parentProjectId = parentProjectId, runName = name, runKind = IsBranch ? "branch" : "root",
        descriptionOverride = DescriptionOverride.OverrideValue, resultsParameterSpans = ActiveResultsParameters.Select(span => span.ToDto()).ToArray(),
        setupParameterSpans = IsBranch ? ActiveSetupParameters.Select(span => span.ToDto()).ToArray() : []
    };
}
