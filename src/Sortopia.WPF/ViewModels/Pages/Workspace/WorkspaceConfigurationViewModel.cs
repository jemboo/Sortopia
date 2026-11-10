using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
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

public sealed class WorkspaceTextEntry : WorkspaceFormItem
{
    private string text;
    private readonly Func<IEnumerable<string>> suggestedKeys;
    private string[] keyChoices = [];
    public WorkspaceTextEntry(string text, Action<WorkspaceTextEntry> remove, Func<IEnumerable<string>>? suggestedKeys = null)
    {
        this.text = text; this.suggestedKeys = suggestedKeys ?? (() => []);
        RemoveCommand = new RelayCommand(() => remove(this)); RefreshKeyChoices();
    }
    public string Text { get => text; set { if (SetProperty(ref text, value)) { RefreshKeyChoices(); NotifyChanged(); } } }
    public string[] KeyChoices => keyChoices;
    internal void RefreshKeyChoices()
    {
        var choices = WorkspaceParameterBinding.KnownKeys.Concat(suggestedKeys()).Append(Text)
            .Where(key => !string.IsNullOrWhiteSpace(key)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (keyChoices.SequenceEqual(choices)) return;
        keyChoices = choices; OnPropertyChanged(nameof(KeyChoices));
    }
    public RelayCommand RemoveCommand { get; }
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
    public string Key { get => key; set { if (SetProperty(ref key, value)) ValuesChanged(); } }
    public ObservableCollection<WorkspaceTypedValue> Values { get; } = [];
    public RelayCommand AddValueCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public string Error => string.IsNullOrWhiteSpace(Key) ? "Enter a parameter key."
        : requirements() is { } required && !required.Any(value => value.Name == Key) ? $"‘{Key}’ is not required by the selected function. Remove this span or choose one of its required keys."
        : Values.Count == 0 ? "Add at least one value. Blank values are allowed for text parameters."
        : Values.Select(value => value.Error).FirstOrDefault(error => error.Length > 0) ?? "";
    public parameterValueType ValueType => requirements()?.FirstOrDefault(value => value.Name == Key)?.ValueType
        ?? (defaultValue(Key) is { } value ? (parameterValueType)value.valueType : ParamKeys.valueType(Key));
    public string TypeLabel => ParameterValue.label(ValueType);
    public string ValueHint => WorkspaceParameterRules.Hint(Key, ValueType);
    public string DefaultHint => defaultValue(Key) is { } value ? $"Overrides default: {value.value}"
        : "Additional parameter; its values are included in the combinations.";
    internal void RefreshInherited()
    {
        var choices = (requirements() is { } required ? required.Select(value => value.Name) : WorkspaceParameterBinding.KnownKeys.Concat(suggestedKeys()).Append(Key))
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (!keyChoices.SequenceEqual(choices)) keyChoices = choices;
        foreach (var value in Values) value.RefreshType();
        OnPropertyChanged("");
    }
    internal parameterSpanDto ToDto() => new() { keyName = Key, values = Values.Select(value => value.Text).ToArray() };
}

public sealed class WorkspaceDataTypeEntry : WorkspaceFormItem
{
    private string kind, text;
    public WorkspaceDataTypeEntry(string kind, string text, Action<WorkspaceDataTypeEntry> remove)
    { this.kind = kind; this.text = text; RemoveCommand = new RelayCommand(() => remove(this)); }
    public static string[] Kinds { get; } = SortopiaDataType.kinds.ToArray();
    public string Kind { get => kind; set { if (SetProperty(ref kind, value)) Edited(); } }
    public string Text { get => text; set { if (SetProperty(ref text, value)) Edited(); } }
    public string TextSummary => string.IsNullOrEmpty(Text) ? "Text: (empty)" : $"Text: {Text}";
    public sortopiaDataType Value => SortopiaDataType.create(Kind, Text);
    public string Error => !Kinds.Contains(Kind, StringComparer.Ordinal) ? "Choose a data type." : Text is null ? "Enter text; an empty string is allowed." : "";
    public RelayCommand RemoveCommand { get; }
    private void Edited() { OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(TextSummary)); NotifyChanged(); }
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
    { this.required = required; this.requireInherited = requireInherited; this.inherited = inherited; Choices = choices ?? []; }
    public string[] Choices { get; }
    public bool HasChoices => Choices.Length > 0;
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
            var section = Title.StartsWith("Setup", StringComparison.Ordinal) ? "Setup settings" : "Results settings";
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
    public WorkspaceEditorSections Sections { get; } = new();
    private readonly Func<IEnumerable<WorkspaceSourceProject>> sources;
    public bool IsReadOnly { get; private set; }
    public bool IsEditable => !IsReadOnly;
    internal void Freeze(projectSettingsDto? snapshot = null) { frozenSettings = snapshot ?? ToProjectSettings(); ResultsFunctionOverride.Load(frozenSettings.resultsFunctionName); SetupFunctionOverride.Load(IsBranch ? frozenSettings.setupFunctionName : null); IsReadOnly = true; foreach (var state in properties.Values) state.IsReadOnly = true; RefreshInherited(); }
    private readonly Dictionary<string, WorkspaceOverrideState> properties = [];
    public WorkspaceConfigurationViewModel(WorkspaceEntityKind kind, bool isBranch, Func<WorkspaceConfigurationViewModel?> parent, Func<string> name, Func<IEnumerable<WorkspaceSourceProject>>? sources = null)
    {
        this.sources = sources ?? (() => []);
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
        WorkspaceListOverride List(string key, string title, Action seed)
        {
            var field = new WorkspaceListOverride(title, owner, () => key switch {
                nameof(OutputTypes) or nameof(ResultsQueryKeys) when HasResultsContract => ValueSource(nameof(ResultsFunctionName)),
                nameof(SetupDataTypes) when SetupGeneratesAllOutputs => ValueSource(nameof(SetupFunctionName)),
                _ => Parent?.ValueSource(key)
            }, () => Quietly(seed), IsDefaults);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        OutputTypesOverride = List(nameof(OutputTypes), "Results data types", () => LoadDataTypes(OutputTypes, Parent?.EffectiveOutputs ?? []));
        SetupDataTypesOverride = List(nameof(SetupDataTypes), "Setup outputs and source projects", () => LoadSetupDataTypes(Parent?.EffectiveSetupDataTypes ?? []));
        ResultsQueryKeysOverride = List(nameof(ResultsQueryKeys), "Results query keys", () => LoadKeys(ResultsQueryKeys, Parent?.EffectiveOutputKeys ?? []));
        OutputTypesOverride.FixedByFunction = () => HasResultsContract;
        ResultsQueryKeysOverride.FixedByFunction = () => HasResultsContract;
        SetupDataTypesOverride.FixedByFunction = () => SetupGeneratesAllOutputs;
        ResultsParameterDefaultsOverride = List(nameof(ResultsParameterDefaults), "Results parameter defaults", () => { });
        SetupParameterDefaultsOverride = List(nameof(SetupParameterDefaults), "Setup parameter defaults", () => { });
        WorkspaceFormItem.Watch(ResultsParameterDefaults, () => ListChanged(ResultsParameterDefaultsOverride));
        WorkspaceFormItem.Watch(SetupParameterDefaults, () => ListChanged(SetupParameterDefaultsOverride));
        AddResultsParameterDefaultCommand = new RelayCommand(() => { ResultsParameterDefaultsOverride.IsOverridden = true; AddDefault(ResultsParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults);
        AddSetupParameterDefaultCommand = new RelayCommand(() => { SetupParameterDefaultsOverride.IsOverridden = true; AddDefault(SetupParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); }, () => CanEditDefaults && IsBranch);
        AddRequiredResultsDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(false), () => CanAddRequiredDefaults(false));
        AddRequiredSetupDefaultsCommand = new RelayCommand(() => AddRequiredDefaults(true), () => CanAddRequiredDefaults(true));
        WorkspaceFormItem.Watch(OutputTypes, () => ListChanged(OutputTypesOverride));
        WorkspaceFormItem.Watch(SetupDataTypes, () => ListChanged(SetupDataTypesOverride));
        AddOutputTypeCommand = new RelayCommand(() => { OutputTypesOverride.IsOverridden = true; AddDataType(OutputTypes, new() { kind = "SorterSetEval", value = "" }); });
        AddSetupDataTypeCommand = new RelayCommand(() => { SetupDataTypesOverride.IsOverridden = true; AddSetupDataType(new() { dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = Guid.Empty, queryParameterKeys = [] }); }, () => IsEditable);
        WorkspaceFormItem.Watch(ResultsQueryKeys, () => ListChanged(ResultsQueryKeysOverride));
        WorkspaceFormItem.Watch(ResultsParameters, NotifyChanged); WorkspaceFormItem.Watch(SetupParameters, NotifyChanged);
        AddResultsQueryKeyCommand = new RelayCommand(() => { ResultsQueryKeysOverride.IsOverridden = true; AddKey(ResultsQueryKeys, ""); });
        AddResultsParameterCommand = new RelayCommand(() => AddSpan(ResultsParameters, "", [""]));
        AddSetupParameterCommand = new RelayCommand(() => AddSpan(SetupParameters, "", [""]));
        AddMissingResultsParametersCommand = new RelayCommand(() => AddMissing(ResultsParameters, MissingOutputKeys), () => MissingOutputKeys.Length > 0);
        AddMissingSetupParametersCommand = new RelayCommand(() => AddMissing(SetupParameters, MissingSetupKeys), () => MissingSetupKeys.Length > 0);
    }
    private WorkspaceConfigurationViewModel? Parent => parent();
    internal string Name => name();
    private WorkspaceConfigurationViewModel? ValueSource(string property)
    {
        if (HasResultsContract && property is nameof(OutputTypes) or nameof(ResultsQueryKeys)) property = nameof(ResultsFunctionName);
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

    private paramKeyDescriptor[]? Requirements(bool setup) => setup
        ? FunctionCatalogs.setupFunctionCatalog.TryGetValue(SetupFunctionName, out var setupDefinition) ? setupDefinition.RequiredParameters.ToArray() : null
        : FunctionCatalogs.resultsFunctionCatalog.TryGetValue(ResultsFunctionName, out var resultsDefinition) ? resultsDefinition.RequiredParameters.ToArray() : null;
    public string ResultsRequirementsHint => RequirementsHint(false);
    public string SetupRequirementsHint => RequirementsHint(true);
    private string RequirementsHint(bool setup) => Requirements(setup) is { } required
        ? "Required parameters: " + string.Join(", ", required.Select(key => $"{key.Name} ({ParameterValue.label(key.ValueType)})")) + ". Supply each through a default or a run span."
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
    public WorkspaceListOverride ResultsQueryKeysOverride { get; }
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
        return Requirements(setup)?.Where(key => !existing.Contains(key.Name)).ToArray() ?? [];
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
    public IEnumerable<WorkspaceParameterBinding> LocalResultsParameterDefaults => ResultsParameterDefaultsOverride.IsOverridden ? ResultsParameterDefaults : [];
    public IEnumerable<WorkspaceParameterBinding> LocalSetupParameterDefaults => SetupParameterDefaultsOverride.IsOverridden ? SetupParameterDefaults : [];
    private static parameterBindingDto[] MergeDefaults(parameterBindingDto[] inherited, IEnumerable<WorkspaceParameterBinding> rows)
    {
        var local = rows.Select(row => row.ToDto()).ToArray();
        var inheritedKeys = inherited.Select(value => value.keyName).ToHashSet(StringComparer.Ordinal);
        return inherited.Select(value => local.FirstOrDefault(binding => binding.keyName == value.keyName) ?? value)
            .Concat(local.Where(value => !inheritedKeys.Contains(value.keyName))).ToArray();
    }
    private parameterBindingDto[] EffectiveOutputDefaults => frozenSettings?.resultsParameterDefaults ?? MergeDefaults(InheritedDefaults(false), LocalResultsParameterDefaults);
    private parameterBindingDto[] EffectiveSetupDefaults => frozenSettings?.setupParameterDefaults ?? MergeDefaults(InheritedDefaults(true), LocalSetupParameterDefaults);
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
        var localError = IsReadOnly ? "" : DefaultsError(local.Select(row => row.ToDto()).ToArray());
        var values = setup ? EffectiveSetupDefaults : EffectiveOutputDefaults;
        var required = Requirements(setup);
        var outside = required is null ? null : values.FirstOrDefault(value => !required.Any(key => key.Name == value.keyName));
        return localError.Length > 0 ? localError : outside is not null
            ? $"Parameter ‘{outside.keyName}’ is not required by function ‘{(setup ? SetupFunctionName : ResultsFunctionName)}’. Remove its local default or choose a required key."
            : DefaultsError(values);
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
                IsRun ? spans.Any(span => span.Key == value.keyName) ? "Overridden by run span" : "Used in every parameter set" : "",
                origin, CanEditDefaults ? new RelayCommand(() => OverrideParameter(value.keyName, setup)) : null);
        }
    }
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedOutputDefaults => Preview(false);
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedSetupDefaults => Preview(true);
    public bool HasInheritedOutputDefaults => InheritedOutputDefaults.Any();
    public bool HasInheritedSetupDefaults => InheritedSetupDefaults.Any();
    public string ParameterDefaultsPreviewTitle => IsReadOnly ? "Fixed parameter defaults" : IsRun ? "Inherited project defaults" : "Inherited defaults";
    private static string SetCount(IEnumerable<WorkspaceParameterSpan> spans) => spans.Aggregate(BigInteger.One, (count, span) => count * span.Values.Count).ToString("N0", CultureInfo.InvariantCulture);
    public string ResultsParameterSetsSummary => $"{SetCount(ResultsParameters)} execution parameter set(s). Spans replace matching defaults; other defaults stay fixed.";
    public string SetupParameterSetsSummary => $"{SetCount(SetupParameters)} setup parameter set(s). Spans replace matching defaults; other defaults stay fixed.";
    public WorkspaceParameterSetsPreview ResultsParameterSetsPreview => !IsRun ? WorkspaceParameterSetsPreview.Empty
        : resultsPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveOutputDefaults, ResultsParameters,
            FirstError(ResultsParameterDefaultsError, ResultsParametersError));
    public WorkspaceParameterSetsPreview SetupParameterSetsPreview => !IsRun || !IsBranch ? WorkspaceParameterSetsPreview.Empty
        : setupPreview ??= WorkspaceParameterSetsPreview.Create(EffectiveSetupDefaults, SetupParameters,
            FirstError(SetupParameterDefaultsError, SetupParametersError));
    private static string FirstError(params string[] errors) => errors.FirstOrDefault(error => error.Length > 0) ?? "";
    public string ResultsSettingsError => FirstError(ResultsDatabaseError, ResultsFunctionError, OutputTypesError, ResultsQueryKeysError);
    public string SetupSettingsError => FirstError(SetupDatabaseError, SetupFunctionError, SetupDataTypesError, SetupQueryKeysError);
    public ObservableCollection<WorkspaceDataTypeEntry> OutputTypes { get; } = [];
    public ObservableCollection<WorkspaceSetupDataTypeEntry> SetupDataTypes { get; } = [];
    public RelayCommand AddOutputTypeCommand { get; }
    public RelayCommand AddSetupDataTypeCommand { get; }
    public ObservableCollection<WorkspaceTextEntry> ResultsQueryKeys { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> ResultsParameters { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> SetupParameters { get; } = [];
    public RelayCommand AddResultsQueryKeyCommand { get; }
    public RelayCommand AddResultsParameterCommand { get; }
    public RelayCommand AddSetupParameterCommand { get; }
    public RelayCommand AddMissingResultsParametersCommand { get; }
    public RelayCommand AddMissingSetupParametersCommand { get; }
    private sortopiaDataTypeDto[] EffectiveOutputs => frozenSettings?.resultsSortopiaDataTypes ?? DeclaredResults?.Select(output => output.dataType).ToArray() ?? (OutputTypesOverride.IsOverridden ? OutputTypes.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveOutputs ?? []);
    private setupDataTypeConfigDto[] EffectiveSetupDataTypes => frozenSettings?.setupDataTypes ?? ResolveSetupTypes(SetupDataTypesOverride.IsOverridden ? SetupDataTypes.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveSetupDataTypes ?? []);
    public IEnumerable<WorkspaceSetupDataTypeEntry> InheritedSetupDataTypes => EffectiveSetupDataTypes.Select(value =>
    {
        var row = new WorkspaceSetupDataTypeEntry(value, sources, _ => { });
        if (HasSetupContract) row.SetContract(true, NeedsSource(value.dataType), IsModule, value.queryParameterKeys);
        return row;
    });
    private sortopiaDataTypeDto[] EffectiveSetupOutputs => EffectiveSetupDataTypes.Select(value => value.dataType).ToArray();
    public sortopiaDataTypeDto[] InheritedOutputTypes => EffectiveOutputs;
    public sortopiaDataTypeDto[] InheritedSetupOutputTypes => EffectiveSetupOutputs;
    private static string DataTypesError(sortopiaDataTypeDto[] values) => values.Any(value => !WorkspaceDataTypeEntry.Kinds.Contains(value.kind, StringComparer.Ordinal))
        ? "Choose a data type for each row." : values.Any(value => value.value is null) ? "Data type text cannot be null; an empty string is allowed."
        : values.Select(value => (value.kind, value.value)).Distinct().Count() != values.Length ? "Each data type and text pair must be unique." : "";
    public string OutputTypesError => DataTypesError(EffectiveOutputs);
    public string SetupDataTypesError => DataTypesError(EffectiveSetupOutputs) is { Length: > 0 } error ? error
        : InheritedSetupDataTypes.Select(row => row.Error).FirstOrDefault(e => e.Length > 0) ?? "";
    private string[] EffectiveOutputKeys => frozenSettings?.resultsQueryParameterKeys ?? DeclaredResults?.SelectMany(output => output.queryParameterKeys).Distinct(StringComparer.Ordinal).ToArray() ?? (ResultsQueryKeysOverride.IsOverridden ? ResultsQueryKeys.Select(key => key.Text).ToArray() : Parent?.EffectiveOutputKeys ?? []);
    private string[] EffectiveSetupKeys => EffectiveSetupDataTypes.SelectMany(row => row.queryParameterKeys).Distinct(StringComparer.Ordinal).ToArray();
    private string[] RequiredSetupKeys => EffectiveSetupKeys.Concat(EffectiveSetupDataTypes.Where(row => NeedsSource(row.dataType)).SelectMany(row =>
        sources().FirstOrDefault(p => p.Id == row.sourceProjectId)?.Settings.resultsDataTypes?.FirstOrDefault(output => SameType(output.dataType, row.dataType))?.queryParameterKeys ?? [])).Distinct(StringComparer.Ordinal).ToArray();
    public string ResultsDatabaseError => ResultsDatabaseOverride.Error;
    public string ResultsFunctionError => FirstError(ResultsFunctionOverride.Error, FunctionCompatibilityError);
    public string SetupDatabaseError => SetupDatabaseOverride.Error;
    public string SetupFunctionError => SetupFunctionOverride.Error;
    private static string KeysError(string[] values) => values.Any(string.IsNullOrWhiteSpace) ? "Enter a name for each query key, or remove its row."
        : values.Distinct(StringComparer.Ordinal).Count() != values.Length ? "Query keys must be unique." : "";
    private static string[] MissingKeys(IEnumerable<WorkspaceParameterSpan> spans, IEnumerable<string>? keys) =>
        keys?.Where(key => !string.IsNullOrWhiteSpace(key) && !spans.Any(span => span.Key == key)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private string[] MissingOutputKeys => MissingKeys(ResultsParameters, Requirements(false)?.Select(key => key.Name) ?? Parent?.EffectiveOutputKeys).Except(EffectiveOutputDefaults.Select(value => value.keyName), StringComparer.Ordinal).ToArray();
    private string[] MissingSetupKeys => MissingKeys(SetupParameters, Requirements(true)?.Select(key => key.Name) ?? Parent?.RequiredSetupKeys).Except(EffectiveSetupDefaults.Select(value => value.keyName), StringComparer.Ordinal).ToArray();
    private static string ParametersError(IEnumerable<WorkspaceParameterSpan> spans, string[] missing)
    {
        var rows = spans.ToArray();
        return rows.Where(row => row.Error.Length > 0).Select(row => $"Parameter ‘{(string.IsNullOrWhiteSpace(row.Key) ? "unnamed" : row.Key)}’: {row.Error}").FirstOrDefault()
            ?? (rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length ? "Parameter keys must be unique."
            : missing.Length > 0 ? $"Missing values for required parameters: {string.Join(", ", missing)}. Choose ‘Add required parameters’, then enter at least one value for each key; or define defaults on the project or a module." : "");
    }
    private string QueryContractError(bool setup, string[] keys)
    {
        var outside = Requirements(setup) is { } required ? keys.Where(key => !required.Any(value => value.Name == key)).ToArray() : [];
        if (setup && HasSetupContract && outside.Length > 0)
            return $"The source project requires parameters outside the setup function’s contract: {string.Join(", ", outside)}. Choose a compatible source project or change the setup function on the module.";
        return outside.Length == 0 ? "" : $"Query keys must be required inputs of the {(setup ? "setup" : "results")} function. Unsupported keys: {string.Join(", ", outside)}. Update the query keys or select a compatible function on the module.";
    }
    public string ResultsQueryKeysError => FirstError(KeysError(EffectiveOutputKeys), QueryContractError(false, EffectiveOutputKeys));
    public string SetupQueryKeysError => FirstError(KeysError(EffectiveSetupKeys), QueryContractError(true, RequiredSetupKeys));
    public string ResultsParametersError => ParametersError(ResultsParameters, MissingOutputKeys);
    public string SetupParametersError => ParametersError(SetupParameters, MissingSetupKeys);
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
    private void ListChanged(WorkspaceListOverride local) { if (!loading) { local.MarkLocal(); NotifyChanged(); } }
    private void NotifyChanged() { if (!loading) { RefreshInherited(); Changed?.Invoke(); } }
    internal void RefreshInherited()
    {
        resultsPreview = setupPreview = null;
        RefreshContractRows();
        foreach (var field in properties.Values) field.RefreshInherited();
        foreach (var span in ResultsParameters.Concat(SetupParameters)) span.RefreshInherited();
        foreach (var key in ResultsQueryKeys) key.RefreshKeyChoices();
        foreach (var row in SetupDataTypes) row.RefreshSources();
        foreach (var row in ResultsParameterDefaults.Concat(SetupParameterDefaults)) row.RefreshSource();
        OnPropertyChanged("");
        AddMissingResultsParametersCommand?.RaiseCanExecuteChanged(); AddMissingSetupParametersCommand?.RaiseCanExecuteChanged();
        AddRequiredResultsDefaultsCommand?.RaiseCanExecuteChanged(); AddRequiredSetupDefaultsCommand?.RaiseCanExecuteChanged();
    }
    private void Quietly(Action action) { var wasLoading = loading; loading = true; try { action(); } finally { loading = wasLoading; } }
    private void AddKey(ObservableCollection<WorkspaceTextEntry> rows, string value) =>
        rows.Add(new(value, item => rows.Remove(item), () => EffectiveOutputDefaults.Select(binding => binding.keyName)));
    private void AddSpan(ObservableCollection<WorkspaceParameterSpan> rows, string key, IEnumerable<string> values) =>
        rows.Add(new(key, values, item => rows.Remove(item), name => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults : EffectiveOutputDefaults).FirstOrDefault(value => value.keyName == name),
            () => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults.Select(value => value.keyName).Concat(RequiredSetupKeys)
                : EffectiveOutputDefaults.Select(value => value.keyName).Concat(EffectiveOutputKeys)).Concat(rows.Select(row => row.Key)),
            () => Requirements(ReferenceEquals(rows, SetupParameters))));
    private void AddMissing(ObservableCollection<WorkspaceParameterSpan> rows, IEnumerable<string>? keys)
    { foreach (var key in MissingKeys(rows, keys)) AddSpan(rows, key, []); }
    private void LoadKeys(ObservableCollection<WorkspaceTextEntry> rows, string[] values)
    { rows.Clear(); foreach (var key in values) AddKey(rows, key); }
    private void AddDefault(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto value) =>
        rows.Add(new(value.keyName, (parameterValueType)value.valueType, value.value, item => rows.Remove(item), key => LocalParameterSource(key, ReferenceEquals(rows, SetupParameterDefaults)),
            () => (ReferenceEquals(rows, SetupParameterDefaults) ? EffectiveSetupDefaults : EffectiveOutputDefaults).Select(binding => binding.keyName),
            key => (ReferenceEquals(rows, SetupParameterDefaults) ? Parent?.EffectiveSetupDefaults : Parent?.EffectiveOutputDefaults)?.FirstOrDefault(binding => binding.keyName == key),
            () => Requirements(ReferenceEquals(rows, SetupParameterDefaults))));
    private void LoadDefaults(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto[] values)
    { rows.Clear(); foreach (var value in values) AddDefault(rows, value); }
    public void LoadProject(projectSettingsDto value) => LoadOverrides(value);
    public void LoadOverrides(projectSettingsDto value)
    {
        if (IsReadOnly) throw new InvalidOperationException("Create a new project version to change sealed settings.");
        if (!IsDefaults) throw new InvalidOperationException("Defaults belong to a module or project.");
        Quietly(() =>
        {
            DescriptionOverride.Load(value.description); ResultsDatabaseOverride.Load(IsProject ? value.resultsDatabaseName ?? "Results" : null); ResultsFunctionOverride.Load(IsModule ? value.resultsFunctionName : null);
            SetupDatabaseOverride.Load(IsProject && IsBranch ? value.setupDatabaseName ?? "Setup" : null); SetupFunctionOverride.Load(IsModule && IsBranch ? value.setupFunctionName : null);
            LoadDataTypes(OutputTypes, value.resultsSortopiaDataTypes ?? []); LoadSetupDataTypes(value.setupDataTypes ?? []);
            LoadKeys(ResultsQueryKeys, value.resultsQueryParameterKeys ?? []);
            LoadDefaults(ResultsParameterDefaults, value.resultsParameterDefaults ?? []); LoadDefaults(SetupParameterDefaults, value.setupParameterDefaults ?? []);
            ResultsParameterDefaultsOverride.Load(value.resultsParameterDefaults is not null); SetupParameterDefaultsOverride.Load(IsBranch && value.setupParameterDefaults is not null);
            OutputTypesOverride.Load(value.resultsSortopiaDataTypes is not null); ResultsQueryKeysOverride.Load(value.resultsQueryParameterKeys is not null);
            SetupDataTypesOverride.Load(IsBranch && value.setupDataTypes is not null);
        });
        NotifyChanged();
    }
    private void AddSetupDataType(setupDataTypeConfigDto value) => SetupDataTypes.Add(new(value, sources, row => SetupDataTypes.Remove(row),
        () => EffectiveSetupDefaults.Select(binding => binding.keyName)));
    private void LoadSetupDataTypes(setupDataTypeConfigDto[] values) { SetupDataTypes.Clear(); foreach (var value in values) AddSetupDataType(value); }
    private static void AddDataType(ObservableCollection<WorkspaceDataTypeEntry> rows, sortopiaDataTypeDto value)
    { SortopiaDataTypeDto.toDomain(value); rows.Add(new(value.kind, value.value, row => rows.Remove(row))); }
    private static void LoadDataTypes(ObservableCollection<WorkspaceDataTypeEntry> rows, sortopiaDataTypeDto[] values)
    { rows.Clear(); foreach (var value in values) AddDataType(rows, value); }
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
        description = Description, resultsDatabaseName = ResultsDatabaseName, resultsFunctionName = ResultsFunctionName,
        resultsDataTypes = EffectiveResultsDataTypes, resultsSortopiaDataTypes = EffectiveOutputs, resultsQueryParameterKeys = EffectiveOutputKeys, resultsParameterDefaults = EffectiveOutputDefaults, setupParameterDefaults = IsBranch ? EffectiveSetupDefaults : [],
        setupDatabaseName = IsBranch ? SetupDatabaseName : "", setupFunctionName = IsBranch ? SetupFunctionName : "",
        setupDataTypes = IsBranch ? EffectiveSetupDataTypes : []
    };
    internal projectSettingsDto ToLocalOverrides() => new()
    {
        description = DescriptionOverride.OverrideValue, resultsDatabaseName = IsProject ? ResultsDatabaseName : null, resultsFunctionName = IsModule ? ResultsFunctionOverride.OverrideValue : null,
        resultsParameterDefaults = ResultsParameterDefaultsOverride.IsOverridden ? ResultsParameterDefaults.Select(row => row.ToDto()).ToArray() : null,
        setupParameterDefaults = IsBranch && SetupParameterDefaultsOverride.IsOverridden ? SetupParameterDefaults.Select(row => row.ToDto()).ToArray() : null,
        resultsSortopiaDataTypes = !HasResultsContract && OutputTypesOverride.IsOverridden ? EffectiveOutputs : null,
        resultsQueryParameterKeys = !HasResultsContract && ResultsQueryKeysOverride.IsOverridden ? ResultsQueryKeys.Select(key => key.Text).ToArray() : null,
        setupDatabaseName = IsProject && IsBranch ? SetupDatabaseName : null, setupFunctionName = IsModule && IsBranch ? SetupFunctionOverride.OverrideValue : null,
        setupDataTypes = IsBranch && SetupDataTypesOverride.IsOverridden ? EffectiveSetupDataTypes : null
    };
    internal runDto ToRunInputs(Guid id, string name, Guid parentProjectId) => new()
    {
        schemaVersion = 13, id = id, parentProjectId = parentProjectId, runName = name, runKind = IsBranch ? "branch" : "root",
        descriptionOverride = DescriptionOverride.OverrideValue, resultsParameterSpans = ResultsParameters.Select(span => span.ToDto()).ToArray(),
        setupParameterSpans = IsBranch ? SetupParameters.Select(span => span.ToDto()).ToArray() : []
    };
}
