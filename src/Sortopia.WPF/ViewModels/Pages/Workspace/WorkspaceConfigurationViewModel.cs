using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
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
    public WorkspaceTextEntry(string text, Action<WorkspaceTextEntry> remove)
    { this.text = text; RemoveCommand = new RelayCommand(() => remove(this)); }
    public string Text { get => text; set { if (SetProperty(ref text, value)) NotifyChanged(); } }
    public RelayCommand RemoveCommand { get; }
}

public sealed class WorkspaceParameterSpan : WorkspaceFormItem
{
    private string key;
    private readonly Func<string, parameterBindingDto?> defaultValue;
    public WorkspaceParameterSpan(string key, IEnumerable<string> values, Action<WorkspaceParameterSpan> remove, Func<string, parameterBindingDto?> defaultValue)
    {
        this.key = key; this.defaultValue = defaultValue;
        Watch(Values, ValuesChanged);
        foreach (var value in values) AddValue(value);
        AddValueCommand = new RelayCommand(() => AddValue(""));
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    private void ValuesChanged() { RefreshInherited(); NotifyChanged(); }
    private void AddValue(string value) => Values.Add(new(value, () => ValueType, item => Values.Remove(item)));
    public string Key { get => key; set { if (SetProperty(ref key, value)) ValuesChanged(); } }
    public ObservableCollection<WorkspaceTypedValue> Values { get; } = [];
    public RelayCommand AddValueCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public string Error => string.IsNullOrWhiteSpace(Key) ? "Enter a parameter key."
        : Values.Count == 0 ? "Add at least one value. Blank values are allowed for text parameters."
        : Values.Select(value => value.Error).FirstOrDefault(error => error.Length > 0) ?? "";
    public parameterValueType ValueType => defaultValue(Key) is { } value ? (parameterValueType)value.valueType : ParamKeys.valueType(Key);
    public string TypeLabel => ParameterValue.label(ValueType);
    public string DefaultHint => defaultValue(Key) is { } value ? $"Overrides default: {value.value}"
        : "Additional parameter; its values are included in the combinations.";
    internal void RefreshInherited() { foreach (var value in Values) value.RefreshType(); OnPropertyChanged(""); }
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
    public sortopiaDataType Value => SortopiaDataType.create(Kind, Text);
    public string Error => !Kinds.Contains(Kind, StringComparer.Ordinal) ? "Choose a data type." : Text is null ? "Enter text; an empty string is allowed." : "";
    public RelayCommand RemoveCommand { get; }
    private void Edited() { OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(Value)); NotifyChanged(); }
    internal sortopiaDataTypeDto ToDto() => new() { kind = Kind, value = Text };
}

/// <summary>A local value wins over the nearest ancestor; disabling it restores inheritance.</summary>
public abstract class WorkspaceOverrideState : WorkspaceFormItem
{
    private readonly Func<WorkspaceConfigurationViewModel?> inheritedSource;
    private readonly string ownerKind;
    private bool isOverridden, hasDraft;
    protected WorkspaceOverrideState(string title, string ownerKind, Func<WorkspaceConfigurationViewModel?> inheritedSource, bool canOverride = true)
    { Title = title; this.ownerKind = ownerKind; this.inheritedSource = inheritedSource; CanOverride = canOverride; }
    public string Title { get; }
    public bool CanOverride { get; }
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
    public WorkspaceConfigurationViewModel? InheritedSource => IsInherited ? inheritedSource() : null;
    public bool HasInheritedSource => InheritedSource is not null;
    public string SourceLabel => IsOverridden ? ownerKind == "run" ? "Overridden for this run" : $"Local override on this {ownerKind}"
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
        string ownerKind, bool canOverride = true, bool requireInherited = true) : base(title, ownerKind, source, canOverride)
    { this.required = required; this.requireInherited = requireInherited; this.inherited = inherited; }
    public bool IsMultiline => !required;
    protected override void SeedOverride() => value = inherited();
    public string EffectiveValue
    {
        get => IsOverridden ? value : inherited();
        set
        {
            if (!IsOverridden || this.value == value) return;
            this.value = value; RefreshInherited(); NotifyChanged();
        }
    }
    public string Error => required && (IsOverridden || requireInherited) && string.IsNullOrWhiteSpace(EffectiveValue)
        ? IsOverridden ? "Enter a value or turn off the override to inherit."
            : CanOverride ? "Set this value here or on an ancestor module." : "Set this value on the parent project or an ancestor module." : "";
    public string? OverrideValue => IsOverridden ? value : null;
    internal void Load(string? input) { value = input ?? ""; LoadState(input is not null); }
    internal void SetLocal(string input) { value = input; MarkLocal(); NotifyChanged(); }
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
public sealed class WorkspaceConfigurationViewModel : ViewModelBase
{
    private readonly Func<WorkspaceConfigurationViewModel?> parent;
    private readonly Func<string> name;
    private bool loading;
    private readonly Dictionary<string, WorkspaceOverrideState> properties = [];
    public WorkspaceConfigurationViewModel(WorkspaceEntityKind kind, bool isBranch, Func<WorkspaceConfigurationViewModel?> parent, Func<string> name)
    {
        IsProject = kind == WorkspaceEntityKind.Project; IsModule = kind == WorkspaceEntityKind.Module;
        IsBranch = isBranch; this.parent = parent; this.name = name;
        var owner = kind.ToString().ToLowerInvariant();
        WorkspaceOverrideField Field(string key, string title, bool required, Func<string> inherited, bool canOverride = true)
        {
            var field = new WorkspaceOverrideField(title, required, inherited, () => Parent?.ValueSource(key), owner, canOverride, !IsModule);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        DescriptionOverride = Field(nameof(Description), "Description", false, () => Parent?.Description ?? "");
        ResultsDatabaseOverride = Field(nameof(ResultsDatabaseName), "Results database", true, () => Parent?.ResultsDatabaseName ?? "Results", IsDefaults);
        ExecuteFunctionOverride = Field(nameof(ExecuteFunctionName), "Execution function", true, () => Parent?.ExecuteFunctionName ?? "", IsDefaults);
        SetupDatabaseOverride = Field(nameof(SetupDatabaseName), "Setup database", true, () => Parent?.SetupDatabaseName ?? "Setup");
        SetupFunctionOverride = Field(nameof(SetupFunctionName), "Setup function", true, () => Parent?.SetupFunctionName ?? "");
        WorkspaceListOverride List(string key, string title, Action seed)
        {
            var field = new WorkspaceListOverride(title, owner, () => Parent?.ValueSource(key), () => Quietly(seed), IsDefaults);
            properties.Add(key, field); field.Changed += NotifyChanged; return field;
        }
        OutputTypesOverride = List(nameof(OutputTypes), "Output types", () => LoadDataTypes(OutputTypes, Parent?.EffectiveOutputs ?? []));
        SetupOutputTypesOverride = List(nameof(SetupOutputTypes), "Setup output types", () => LoadDataTypes(SetupOutputTypes, Parent?.EffectiveSetupOutputs ?? []));
        OutputQueryKeysOverride = List(nameof(OutputQueryKeys), "Output query keys", () => LoadKeys(OutputQueryKeys, Parent?.EffectiveOutputKeys ?? []));
        SetupQueryKeysOverride = List(nameof(SetupQueryKeys), "Setup query keys", () => LoadKeys(SetupQueryKeys, Parent?.EffectiveSetupKeys ?? []));
        OutputParameterDefaultsOverride = List(nameof(OutputParameterDefaults), "Execution parameter defaults", () => LoadDefaults(OutputParameterDefaults, Parent?.EffectiveOutputDefaults ?? []));
        SetupParameterDefaultsOverride = List(nameof(SetupParameterDefaults), "Setup parameter defaults", () => LoadDefaults(SetupParameterDefaults, Parent?.EffectiveSetupDefaults ?? []));
        WorkspaceFormItem.Watch(OutputParameterDefaults, () => ListChanged(OutputParameterDefaultsOverride));
        WorkspaceFormItem.Watch(SetupParameterDefaults, () => ListChanged(SetupParameterDefaultsOverride));
        AddOutputParameterDefaultCommand = new RelayCommand(() => { OutputParameterDefaultsOverride.IsOverridden = true; AddDefault(OutputParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); });
        AddSetupParameterDefaultCommand = new RelayCommand(() => { SetupParameterDefaultsOverride.IsOverridden = true; AddDefault(SetupParameterDefaults, new() { keyName = "", valueType = 0, value = "" }); });
        WorkspaceFormItem.Watch(OutputTypes, () => ListChanged(OutputTypesOverride));
        WorkspaceFormItem.Watch(SetupOutputTypes, () => ListChanged(SetupOutputTypesOverride));
        AddOutputTypeCommand = new RelayCommand(() => { OutputTypesOverride.IsOverridden = true; AddDataType(OutputTypes, new() { kind = "SorterSetEval", value = "" }); });
        AddSetupOutputTypeCommand = new RelayCommand(() => { SetupOutputTypesOverride.IsOverridden = true; AddDataType(SetupOutputTypes, new() { kind = "SorterModelSet", value = "" }); });
        WorkspaceFormItem.Watch(OutputQueryKeys, () => ListChanged(OutputQueryKeysOverride));
        WorkspaceFormItem.Watch(SetupQueryKeys, () => ListChanged(SetupQueryKeysOverride));
        WorkspaceFormItem.Watch(OutputParameters, NotifyChanged); WorkspaceFormItem.Watch(SetupParameters, NotifyChanged);
        AddOutputQueryKeyCommand = new RelayCommand(() => { OutputQueryKeysOverride.IsOverridden = true; AddKey(OutputQueryKeys, ""); });
        AddSetupQueryKeyCommand = new RelayCommand(() => { SetupQueryKeysOverride.IsOverridden = true; AddKey(SetupQueryKeys, ""); });
        AddOutputParameterCommand = new RelayCommand(() => AddSpan(OutputParameters, "", [""]));
        AddSetupParameterCommand = new RelayCommand(() => AddSpan(SetupParameters, "", [""]));
        AddMissingOutputParametersCommand = new RelayCommand(() => AddMissing(OutputParameters, MissingOutputKeys), () => MissingOutputKeys.Length > 0);
        AddMissingSetupParametersCommand = new RelayCommand(() => AddMissing(SetupParameters, MissingSetupKeys), () => MissingSetupKeys.Length > 0);
    }
    private WorkspaceConfigurationViewModel? Parent => parent();
    internal string Name => name();
    private WorkspaceConfigurationViewModel? ValueSource(string property) => properties[property].IsOverridden ? this : Parent?.ValueSource(property);
    public bool IsProject { get; }
    public bool IsModule { get; }
    public bool IsDefaults => IsModule || IsProject;
    public bool IsRun => !IsDefaults;
    public bool IsBranch { get; }
    public string Title => IsModule ? "Module defaults" : IsProject ? "Project defaults" : "Run inputs";
    public string Hint => IsDefaults
        ? "Values follow the nearest ancestor override. Turn on an override to set a local value; turn it off to inherit again. List overrides replace the whole list, including an empty list."
        : "Inherited values follow the project and its modules. Description and setup values can be overridden for this run.";
    public string Description { get => DescriptionOverride.EffectiveValue; set => DescriptionOverride.SetLocal(value); }
    public string ResultsDatabaseName { get => ResultsDatabaseOverride.EffectiveValue; set { if (IsDefaults) ResultsDatabaseOverride.SetLocal(value); } }
    public string ExecuteFunctionName { get => ExecuteFunctionOverride.EffectiveValue; set { if (IsDefaults) ExecuteFunctionOverride.SetLocal(value); } }
    public string SetupDatabaseName { get => SetupDatabaseOverride.EffectiveValue; set => SetupDatabaseOverride.SetLocal(value); }
    public string SetupFunctionName { get => SetupFunctionOverride.EffectiveValue; set => SetupFunctionOverride.SetLocal(value); }
    public WorkspaceOverrideField DescriptionOverride { get; }
    public WorkspaceOverrideField ResultsDatabaseOverride { get; }
    public WorkspaceOverrideField ExecuteFunctionOverride { get; }
    public WorkspaceOverrideField SetupDatabaseOverride { get; }
    public WorkspaceOverrideField SetupFunctionOverride { get; }
    public WorkspaceListOverride OutputTypesOverride { get; }
    public WorkspaceListOverride SetupOutputTypesOverride { get; }
    public WorkspaceListOverride OutputQueryKeysOverride { get; }
    public WorkspaceListOverride SetupQueryKeysOverride { get; }
    public WorkspaceListOverride OutputParameterDefaultsOverride { get; }
    public WorkspaceListOverride SetupParameterDefaultsOverride { get; }
    public ObservableCollection<WorkspaceParameterBinding> OutputParameterDefaults { get; } = [];
    public ObservableCollection<WorkspaceParameterBinding> SetupParameterDefaults { get; } = [];
    public RelayCommand AddOutputParameterDefaultCommand { get; }
    public RelayCommand AddSetupParameterDefaultCommand { get; }
    private parameterBindingDto[] EffectiveOutputDefaults => OutputParameterDefaultsOverride.IsOverridden ? OutputParameterDefaults.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveOutputDefaults ?? [];
    private parameterBindingDto[] EffectiveSetupDefaults => SetupParameterDefaultsOverride.IsOverridden ? SetupParameterDefaults.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveSetupDefaults ?? [];
    private static string DefaultsError(parameterBindingDto[] values)
    {
        if (values.Any(value => string.IsNullOrWhiteSpace(value.keyName))) return "Enter a key for each default parameter.";
        if (values.Select(value => value.keyName).Distinct(StringComparer.Ordinal).Count() != values.Length) return "Default parameter keys must be unique.";
        return values.Select(value => ParameterValue.validationError((parameterValueType)value.valueType, value.value)).FirstOrDefault(error => error.Length > 0) ?? "";
    }
    public string OutputParameterDefaultsError => DefaultsError(EffectiveOutputDefaults);
    public string SetupParameterDefaultsError => DefaultsError(EffectiveSetupDefaults);
    public string OutputParameterDefaultsSummary => EffectiveOutputDefaults.Length == 0 ? "No parameter defaults." : "";
    public string SetupParameterDefaultsSummary => EffectiveSetupDefaults.Length == 0 ? "No parameter defaults." : "";
    private IEnumerable<WorkspaceParameterBindingPreview> Preview(parameterBindingDto[] defaults, IEnumerable<WorkspaceParameterSpan> spans) =>
        defaults.Select(value => new WorkspaceParameterBindingPreview(value.keyName, ParameterValue.label((parameterValueType)value.valueType), value.value,
            IsRun ? spans.Any(span => span.Key == value.keyName) ? "Overridden by run span" : "Used in every parameter set" : "Inherited default"));
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedOutputDefaults => Preview(EffectiveOutputDefaults, OutputParameters);
    public IEnumerable<WorkspaceParameterBindingPreview> InheritedSetupDefaults => Preview(EffectiveSetupDefaults, SetupParameters);
    private static string SetCount(IEnumerable<WorkspaceParameterSpan> spans) => spans.Aggregate(BigInteger.One, (count, span) => count * span.Values.Count).ToString("N0", CultureInfo.InvariantCulture);
    public string OutputParameterSetsSummary => $"{SetCount(OutputParameters)} execution parameter set(s). Spans replace matching defaults; other defaults stay fixed.";
    public string SetupParameterSetsSummary => $"{SetCount(SetupParameters)} setup parameter set(s). Spans replace matching defaults; other defaults stay fixed.";
    public ObservableCollection<WorkspaceDataTypeEntry> OutputTypes { get; } = [];
    public ObservableCollection<WorkspaceDataTypeEntry> SetupOutputTypes { get; } = [];
    public RelayCommand AddOutputTypeCommand { get; }
    public RelayCommand AddSetupOutputTypeCommand { get; }
    public ObservableCollection<WorkspaceTextEntry> OutputQueryKeys { get; } = [];
    public ObservableCollection<WorkspaceTextEntry> SetupQueryKeys { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> OutputParameters { get; } = [];
    public ObservableCollection<WorkspaceParameterSpan> SetupParameters { get; } = [];
    public RelayCommand AddOutputQueryKeyCommand { get; }
    public RelayCommand AddSetupQueryKeyCommand { get; }
    public RelayCommand AddOutputParameterCommand { get; }
    public RelayCommand AddSetupParameterCommand { get; }
    public RelayCommand AddMissingOutputParametersCommand { get; }
    public RelayCommand AddMissingSetupParametersCommand { get; }
    private sortopiaDataTypeDto[] EffectiveOutputs => OutputTypesOverride.IsOverridden ? OutputTypes.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveOutputs ?? [];
    private sortopiaDataTypeDto[] EffectiveSetupOutputs => SetupOutputTypesOverride.IsOverridden ? SetupOutputTypes.Select(row => row.ToDto()).ToArray() : Parent?.EffectiveSetupOutputs ?? [];
    public sortopiaDataTypeDto[] InheritedOutputTypes => EffectiveOutputs;
    public sortopiaDataTypeDto[] InheritedSetupOutputTypes => EffectiveSetupOutputs;
    private static string DataTypesError(sortopiaDataTypeDto[] values) => values.Any(value => !WorkspaceDataTypeEntry.Kinds.Contains(value.kind, StringComparer.Ordinal))
        ? "Choose a data type for each row." : values.Any(value => value.value is null) ? "Data type text cannot be null; an empty string is allowed."
        : values.Select(value => (value.kind, value.value)).Distinct().Count() != values.Length ? "Each data type and text pair must be unique." : "";
    public string OutputTypesError => DataTypesError(EffectiveOutputs);
    public string SetupOutputTypesError => DataTypesError(EffectiveSetupOutputs);
    private string[] EffectiveOutputKeys => OutputQueryKeysOverride.IsOverridden ? OutputQueryKeys.Select(key => key.Text).ToArray() : Parent?.EffectiveOutputKeys ?? [];
    private string[] EffectiveSetupKeys => SetupQueryKeysOverride.IsOverridden ? SetupQueryKeys.Select(key => key.Text).ToArray() : Parent?.EffectiveSetupKeys ?? [];
    public string ResultsDatabaseError => ResultsDatabaseOverride.Error;
    public string ExecuteFunctionError => ExecuteFunctionOverride.Error;
    public string SetupDatabaseError => SetupDatabaseOverride.Error;
    public string SetupFunctionError => SetupFunctionOverride.Error;
    private static string KeysError(string[] values) => values.Any(string.IsNullOrWhiteSpace) ? "Enter a name for each query key, or remove its row."
        : values.Distinct(StringComparer.Ordinal).Count() != values.Length ? "Query keys must be unique." : "";
    private static string[] MissingKeys(IEnumerable<WorkspaceParameterSpan> spans, IEnumerable<string>? keys) =>
        keys?.Where(key => !string.IsNullOrWhiteSpace(key) && !spans.Any(span => span.Key == key)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private string[] MissingOutputKeys => MissingKeys(OutputParameters, Parent?.EffectiveOutputKeys).Except(EffectiveOutputDefaults.Select(value => value.keyName), StringComparer.Ordinal).ToArray();
    private string[] MissingSetupKeys => MissingKeys(SetupParameters, Parent?.EffectiveSetupKeys).Except(EffectiveSetupDefaults.Select(value => value.keyName), StringComparer.Ordinal).ToArray();
    private static string ParametersError(IEnumerable<WorkspaceParameterSpan> spans, string[] missing)
    {
        var rows = spans.ToArray();
        return rows.Select(row => row.Error).FirstOrDefault(error => error.Length > 0)
            ?? (rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length ? "Parameter keys must be unique."
            : missing.Length > 0 ? $"Add parameters required by the project: {string.Join(", ", missing)}." : "");
    }
    public string OutputQueryKeysError => KeysError(EffectiveOutputKeys);
    public string SetupQueryKeysError => KeysError(EffectiveSetupKeys);
    public string OutputParametersError => ParametersError(OutputParameters, MissingOutputKeys);
    public string SetupParametersError => ParametersError(SetupParameters, MissingSetupKeys);
    public bool HasMissingOutputParameters => MissingOutputKeys.Length > 0;
    public bool HasMissingSetupParameters => MissingSetupKeys.Length > 0;
    public string OutputTypesSummary => EffectiveOutputs.Length == 0 ? "None" : "";
    public string SetupOutputTypesSummary => EffectiveSetupOutputs.Length == 0 ? "None" : "";
    public string OutputQueryKeysSummary => Summary(EffectiveOutputKeys);
    public string SetupQueryKeysSummary => Summary(EffectiveSetupKeys);
    private static string Summary(IEnumerable<string> values) { var items = values.ToArray(); return items.Length == 0 ? "None" : string.Join(", ", items); }
    public string ValidationError
    {
        get
        {
            var errors = new[] { ResultsDatabaseError, ExecuteFunctionError, OutputTypesError, IsBranch ? SetupOutputTypesError : "", OutputQueryKeysError, OutputParameterDefaultsError, IsBranch ? SetupParameterDefaultsError : "",
                IsBranch ? SetupDatabaseError : "", IsBranch ? SetupFunctionError : "", IsBranch ? SetupQueryKeysError : "",
                IsRun ? OutputParametersError : "", IsRun && IsBranch ? SetupParametersError : "" };
            return errors.FirstOrDefault(error => error.Length > 0) ?? "";
        }
    }
    internal event Action? Changed;
    private void ListChanged(WorkspaceListOverride local) { if (!loading) { local.MarkLocal(); NotifyChanged(); } }
    private void NotifyChanged() { if (!loading) { RefreshInherited(); Changed?.Invoke(); } }
    internal void RefreshInherited()
    {
        foreach (var field in properties.Values) field.RefreshInherited();
        foreach (var span in OutputParameters.Concat(SetupParameters)) span.RefreshInherited();
        OnPropertyChanged("");
        AddMissingOutputParametersCommand?.RaiseCanExecuteChanged(); AddMissingSetupParametersCommand?.RaiseCanExecuteChanged();
    }
    private void Quietly(Action action) { var wasLoading = loading; loading = true; try { action(); } finally { loading = wasLoading; } }
    private static void AddKey(ObservableCollection<WorkspaceTextEntry> rows, string value) => rows.Add(new(value, item => rows.Remove(item)));
    private void AddSpan(ObservableCollection<WorkspaceParameterSpan> rows, string key, IEnumerable<string> values) =>
        rows.Add(new(key, values, item => rows.Remove(item), name => (ReferenceEquals(rows, SetupParameters) ? EffectiveSetupDefaults : EffectiveOutputDefaults).FirstOrDefault(value => value.keyName == name)));
    private void AddMissing(ObservableCollection<WorkspaceParameterSpan> rows, IEnumerable<string>? keys)
    { foreach (var key in MissingKeys(rows, keys)) AddSpan(rows, key, []); }
    private static void LoadKeys(ObservableCollection<WorkspaceTextEntry> rows, string[] values)
    { rows.Clear(); foreach (var key in values) AddKey(rows, key); }
    private static void AddDefault(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto value) =>
        rows.Add(new(value.keyName, (parameterValueType)value.valueType, value.value, item => rows.Remove(item)));
    private static void LoadDefaults(ObservableCollection<WorkspaceParameterBinding> rows, parameterBindingDto[] values)
    { rows.Clear(); foreach (var value in values) AddDefault(rows, value); }
    public void LoadProject(projectSettingsDto value) => LoadOverrides(value);
    public void LoadOverrides(projectSettingsDto value)
    {
        if (!IsDefaults) throw new InvalidOperationException("Defaults belong to a module or project.");
        Quietly(() =>
        {
            DescriptionOverride.Load(value.description); ResultsDatabaseOverride.Load(value.resultsDatabaseName); ExecuteFunctionOverride.Load(value.executeFunctionName);
            SetupDatabaseOverride.Load(IsBranch ? value.setupDatabaseName : null); SetupFunctionOverride.Load(IsBranch ? value.setupFunctionName : null);
            LoadDataTypes(OutputTypes, value.outputDataTypes ?? []); LoadDataTypes(SetupOutputTypes, value.setupOutputDataTypes ?? []);
            LoadKeys(OutputQueryKeys, value.outputQueryParameterKeys ?? []); LoadKeys(SetupQueryKeys, value.setupQueryParameterKeys ?? []);
            LoadDefaults(OutputParameterDefaults, value.outputParameterDefaults ?? []); LoadDefaults(SetupParameterDefaults, value.setupParameterDefaults ?? []);
            OutputParameterDefaultsOverride.Load(value.outputParameterDefaults is not null); SetupParameterDefaultsOverride.Load(IsBranch && value.setupParameterDefaults is not null);
            OutputTypesOverride.Load(value.outputDataTypes is not null); OutputQueryKeysOverride.Load(value.outputQueryParameterKeys is not null);
            SetupOutputTypesOverride.Load(IsBranch && value.setupOutputDataTypes is not null); SetupQueryKeysOverride.Load(IsBranch && value.setupQueryParameterKeys is not null);
        });
        NotifyChanged();
    }
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
            DescriptionOverride.Load(value.description); SetupDatabaseOverride.Load(value.setupDatabaseName); SetupFunctionOverride.Load(value.setupFunctionName);
            OutputParameters.Clear(); foreach (var span in value.outputParameterSpans) AddSpan(OutputParameters, span.keyName, span.values);
            SetupParameters.Clear(); foreach (var span in value.setupParameterSpans) AddSpan(SetupParameters, span.keyName, span.values);
        });
        NotifyChanged();
    }
    internal projectSettingsDto ToProjectSettings() => new()
    {
        description = Description, resultsDatabaseName = ResultsDatabaseName, executeFunctionName = ExecuteFunctionName,
        outputDataTypes = EffectiveOutputs, outputQueryParameterKeys = EffectiveOutputKeys, outputParameterDefaults = EffectiveOutputDefaults, setupParameterDefaults = IsBranch ? EffectiveSetupDefaults : [],
        setupDatabaseName = IsBranch ? SetupDatabaseName : "", setupFunctionName = IsBranch ? SetupFunctionName : "",
        setupOutputDataTypes = IsBranch ? EffectiveSetupOutputs : [], setupQueryParameterKeys = IsBranch ? EffectiveSetupKeys : []
    };
    internal projectSettingsDto ToLocalOverrides() => new()
    {
        description = DescriptionOverride.OverrideValue, resultsDatabaseName = ResultsDatabaseOverride.OverrideValue, executeFunctionName = ExecuteFunctionOverride.OverrideValue,
        outputParameterDefaults = OutputParameterDefaultsOverride.IsOverridden ? EffectiveOutputDefaults : null,
        setupParameterDefaults = IsBranch && SetupParameterDefaultsOverride.IsOverridden ? EffectiveSetupDefaults : null,
        outputDataTypes = OutputTypesOverride.IsOverridden ? EffectiveOutputs : null,
        outputQueryParameterKeys = OutputQueryKeysOverride.IsOverridden ? OutputQueryKeys.Select(key => key.Text).ToArray() : null,
        setupDatabaseName = IsBranch ? SetupDatabaseOverride.OverrideValue : null, setupFunctionName = IsBranch ? SetupFunctionOverride.OverrideValue : null,
        setupOutputDataTypes = IsBranch && SetupOutputTypesOverride.IsOverridden ? EffectiveSetupOutputs : null,
        setupQueryParameterKeys = IsBranch && SetupQueryKeysOverride.IsOverridden ? SetupQueryKeys.Select(key => key.Text).ToArray() : null
    };
    internal runDto ToRunInputs(Guid id, string name, string projectName) => new()
    {
        schemaVersion = 8, id = id, projectName = projectName, runName = name, runKind = IsBranch ? "branch" : "root",
        description = DescriptionOverride.OverrideValue, outputParameterSpans = OutputParameters.Select(span => span.ToDto()).ToArray(),
        setupDatabaseName = IsBranch ? SetupDatabaseOverride.OverrideValue : null, setupFunctionName = IsBranch ? SetupFunctionOverride.OverrideValue : null,
        setupParameterSpans = IsBranch ? SetupParameters.Select(span => span.ToDto()).ToArray() : []
    };
}
