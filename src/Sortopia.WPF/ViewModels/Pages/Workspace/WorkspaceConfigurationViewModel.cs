using System.Collections.ObjectModel;
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
    public WorkspaceParameterSpan(string key, IEnumerable<string> values, Action<WorkspaceParameterSpan> remove)
    {
        this.key = key;
        Watch(Values, ValuesChanged);
        foreach (var value in values) AddValue(value);
        AddValueCommand = new RelayCommand(() => AddValue(""));
        RemoveCommand = new RelayCommand(() => remove(this));
    }
    private void ValuesChanged() { OnPropertyChanged(nameof(Error)); NotifyChanged(); }
    private void AddValue(string value) => Values.Add(new(value, item => Values.Remove(item)));
    public string Key { get => key; set { if (SetProperty(ref key, value)) ValuesChanged(); } }
    public ObservableCollection<WorkspaceTextEntry> Values { get; } = [];
    public RelayCommand AddValueCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public string Error => string.IsNullOrWhiteSpace(Key) ? "Enter a parameter key."
        : Values.Count == 0 ? "Add at least one value. A blank value is allowed." : "";
    internal parameterSpanDto ToDto() => new() { keyName = Key, values = Values.Select(value => value.Text).ToArray() };
}

public sealed class WorkspaceOutputChoice : WorkspaceFormItem
{
    private bool isSelected;
    public WorkspaceOutputChoice(outputDataType value, string label) { Value = value; Label = label; }
    public outputDataType Value { get; }
    public string Label { get; }
    public bool IsSelected { get => isSelected; set { if (SetProperty(ref isSelected, value)) NotifyChanged(); } }
}

public sealed class WorkspaceOverrideField : WorkspaceFormItem
{
    private readonly Func<string> inherited;
    private readonly Func<string> parentName;
    private readonly bool required;
    private bool isOverridden;
    private bool hasDraft;
    private string value = "";
    public WorkspaceOverrideField(string title, bool required, Func<string> inherited, Func<string> parentName)
    { Title = title; this.required = required; this.inherited = inherited; this.parentName = parentName; }
    public string Title { get; }
    public bool IsMultiline => !required;
    public bool IsOverridden
    {
        get => isOverridden;
        set
        {
            if (isOverridden == value) return;
            if (value && !hasDraft) { this.value = inherited(); hasDraft = true; }
            isOverridden = value;
            RefreshInherited();
            NotifyChanged();
        }
    }
    public bool IsInherited => !IsOverridden;
    public string EffectiveValue
    {
        get => IsOverridden ? value : inherited();
        set
        {
            if (!IsOverridden || this.value == value) return;
            this.value = value;
            hasDraft = true;
            RefreshInherited();
            NotifyChanged();
        }
    }
    public string SourceLabel => IsOverridden ? "Overridden for this run" : $"Inherited from {parentName()}";
    public string Error => required && string.IsNullOrWhiteSpace(EffectiveValue)
        ? IsOverridden ? "Enter a value or turn off the override to use the project default." : "Set this value on the parent project." : "";
    public string? OverrideValue => IsOverridden ? value : null;
    internal void Load(string? input) { isOverridden = input is not null; hasDraft = isOverridden; value = input ?? ""; RefreshInherited(); }
    internal void RefreshInherited()
    {
        foreach (var property in new[] { nameof(IsOverridden), nameof(IsInherited), nameof(EffectiveValue), nameof(SourceLabel), nameof(Error) }) OnPropertyChanged(property);
    }
}

/// <summary>Typed draft fields; inheritance is stored separately from the displayed effective value.</summary>
public sealed class WorkspaceConfigurationViewModel : ViewModelBase
{
    private readonly Func<WorkspaceConfigurationViewModel?> parent;
    private readonly Func<string> parentName;
    private bool loading;
    private string description = "", resultsDatabaseName = "Results", executeFunctionName = "", setupDatabaseName = "Setup", setupFunctionName = "";
    private readonly List<int> outputOrder = [], setupOutputOrder = [];
    public WorkspaceConfigurationViewModel(bool isProject, bool isBranch, Func<WorkspaceConfigurationViewModel?> parent, Func<string> parentName)
    {
        IsProject = isProject; IsBranch = isBranch; this.parent = parent; this.parentName = parentName;
        DescriptionOverride = new("Description", false, () => Parent?.Description ?? "", parentName);
        SetupDatabaseOverride = new("Setup database", true, () => Parent?.SetupDatabaseName ?? "", parentName);
        SetupFunctionOverride = new("Setup function", true, () => Parent?.SetupFunctionName ?? "", parentName);
        foreach (var field in new[] { DescriptionOverride, SetupDatabaseOverride, SetupFunctionOverride }) field.Changed += NotifyChanged;
        OutputTypes = Choices(outputOrder);
        SetupOutputTypes = Choices(setupOutputOrder);
        WorkspaceFormItem.Watch(OutputQueryKeys, NotifyChanged);
        WorkspaceFormItem.Watch(SetupQueryKeys, NotifyChanged);
        WorkspaceFormItem.Watch(OutputParameters, NotifyChanged);
        WorkspaceFormItem.Watch(SetupParameters, NotifyChanged);
        AddOutputQueryKeyCommand = new RelayCommand(() => AddKey(OutputQueryKeys, ""));
        AddSetupQueryKeyCommand = new RelayCommand(() => AddKey(SetupQueryKeys, ""));
        AddOutputParameterCommand = new RelayCommand(() => AddSpan(OutputParameters, "", [""]));
        AddSetupParameterCommand = new RelayCommand(() => AddSpan(SetupParameters, "", [""]));
        AddMissingOutputParametersCommand = new RelayCommand(() => AddMissing(OutputParameters, Parent?.OutputQueryKeys), () => MissingOutputKeys.Length > 0);
        AddMissingSetupParametersCommand = new RelayCommand(() => AddMissing(SetupParameters, Parent?.SetupQueryKeys), () => MissingSetupKeys.Length > 0);
    }
    private WorkspaceConfigurationViewModel? Parent => parent();
    public bool IsProject { get; }
    public bool IsRun => !IsProject;
    public bool IsBranch { get; }
    public string Title => IsProject ? "Project defaults" : "Run inputs";
    public string Hint => IsProject ? "Shared settings for every run in this project."
        : "Inherited values follow the project. Turn on an override to keep a value specific to this run.";
    public string InheritedLabel => $"Inherited from {parentName()}";
    public string Description { get => description; set { if (SetProperty(ref description, value)) NotifyChanged(); } }
    public string ResultsDatabaseName { get => IsProject ? resultsDatabaseName : Parent?.ResultsDatabaseName ?? ""; set { if (IsProject && SetProperty(ref resultsDatabaseName, value)) NotifyChanged(); } }
    public string ExecuteFunctionName { get => IsProject ? executeFunctionName : Parent?.ExecuteFunctionName ?? ""; set { if (IsProject && SetProperty(ref executeFunctionName, value)) NotifyChanged(); } }
    public string SetupDatabaseName { get => setupDatabaseName; set { if (SetProperty(ref setupDatabaseName, value)) NotifyChanged(); } }
    public string SetupFunctionName { get => setupFunctionName; set { if (SetProperty(ref setupFunctionName, value)) NotifyChanged(); } }
    public WorkspaceOverrideField DescriptionOverride { get; }
    public WorkspaceOverrideField SetupDatabaseOverride { get; }
    public WorkspaceOverrideField SetupFunctionOverride { get; }
    public WorkspaceOutputChoice[] OutputTypes { get; }
    public WorkspaceOutputChoice[] SetupOutputTypes { get; }
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
    public string ResultsDatabaseError => string.IsNullOrWhiteSpace(ResultsDatabaseName) ? "Enter a results database on the project." : "";
    public string ExecuteFunctionError => string.IsNullOrWhiteSpace(ExecuteFunctionName) ? "Enter an execution function on the project." : "";
    public string SetupDatabaseError => string.IsNullOrWhiteSpace(SetupDatabaseName) ? "Enter a setup database." : "";
    public string SetupFunctionError => string.IsNullOrWhiteSpace(SetupFunctionName) ? "Enter a setup function." : "";
    private static string KeysError(IEnumerable<WorkspaceTextEntry> keys)
    {
        var values = keys.Select(key => key.Text).ToArray();
        return values.Any(string.IsNullOrWhiteSpace) ? "Enter a name for each query key, or remove its row."
            : values.Distinct(StringComparer.Ordinal).Count() != values.Length ? "Query keys must be unique." : "";
    }
    private static string[] MissingKeys(IEnumerable<WorkspaceParameterSpan> spans, IEnumerable<WorkspaceTextEntry>? keys) =>
        keys?.Select(key => key.Text).Where(key => !string.IsNullOrWhiteSpace(key) && !spans.Any(span => span.Key == key)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
    private string[] MissingOutputKeys => MissingKeys(OutputParameters, Parent?.OutputQueryKeys);
    private string[] MissingSetupKeys => MissingKeys(SetupParameters, Parent?.SetupQueryKeys);
    private static string ParametersError(IEnumerable<WorkspaceParameterSpan> spans, string[] missing)
    {
        var rows = spans.ToArray();
        return rows.Select(row => row.Error).FirstOrDefault(error => error.Length > 0)
            ?? (rows.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count() != rows.Length ? "Parameter keys must be unique."
            : missing.Length > 0 ? $"Add parameters required by the project: {string.Join(", ", missing)}." : "");
    }
    public string OutputQueryKeysError => KeysError(OutputQueryKeys);
    public string SetupQueryKeysError => KeysError(SetupQueryKeys);
    public string OutputParametersError => ParametersError(OutputParameters, MissingOutputKeys);
    public string SetupParametersError => ParametersError(SetupParameters, MissingSetupKeys);
    public bool HasMissingOutputParameters => MissingOutputKeys.Length > 0;
    public bool HasMissingSetupParameters => MissingSetupKeys.Length > 0;
    public string OutputTypesSummary => Summary((IsProject ? this : Parent)?.OutputTypes.Where(choice => choice.IsSelected).Select(choice => choice.Label));
    public string SetupOutputTypesSummary => Summary((IsProject ? this : Parent)?.SetupOutputTypes.Where(choice => choice.IsSelected).Select(choice => choice.Label));
    public string OutputQueryKeysSummary => Summary((IsProject ? this : Parent)?.OutputQueryKeys.Select(key => key.Text));
    public string SetupQueryKeysSummary => Summary((IsProject ? this : Parent)?.SetupQueryKeys.Select(key => key.Text));
    private static string Summary(IEnumerable<string>? values) { var items = values?.ToArray() ?? []; return items.Length == 0 ? "None" : string.Join(", ", items); }
    public string ValidationError
    {
        get
        {
            var errors = IsProject ? new[] { ResultsDatabaseError, ExecuteFunctionError, OutputQueryKeysError,
                IsBranch ? SetupDatabaseError : "", IsBranch ? SetupFunctionError : "", IsBranch ? SetupQueryKeysError : "" }
                : new[] { ResultsDatabaseError, ExecuteFunctionError, OutputParametersError, IsBranch ? SetupParametersError : "",
                    IsBranch ? SetupDatabaseOverride.Error : "", IsBranch ? SetupFunctionOverride.Error : "" };
            return errors.FirstOrDefault(error => error.Length > 0) ?? "";
        }
    }
    internal event Action? Changed;
    private WorkspaceOutputChoice[] Choices(List<int> order)
    {
        WorkspaceOutputChoice[] choices = [new(outputDataType.SorterModelSet, "Sorter model sets"), new(outputDataType.SorterSetEval, "Sorter set evaluations")];
        foreach (var choice in choices) choice.Changed += () => { if (choice.IsSelected) order.Add((int)choice.Value); else order.Remove((int)choice.Value); NotifyChanged(); };
        return choices;
    }
    private void NotifyChanged()
    {
        if (loading) return;
        RefreshValidation();
        Changed?.Invoke();
    }
    private void RefreshValidation()
    {
        foreach (var property in new[] { nameof(ResultsDatabaseError), nameof(ExecuteFunctionError), nameof(SetupDatabaseError), nameof(SetupFunctionError),
            nameof(OutputQueryKeysError), nameof(SetupQueryKeysError), nameof(OutputParametersError), nameof(SetupParametersError), nameof(ValidationError),
            nameof(OutputTypesSummary), nameof(SetupOutputTypesSummary), nameof(OutputQueryKeysSummary), nameof(SetupQueryKeysSummary), nameof(HasMissingOutputParameters), nameof(HasMissingSetupParameters) }) OnPropertyChanged(property);
        AddMissingOutputParametersCommand?.RaiseCanExecuteChanged();
        AddMissingSetupParametersCommand?.RaiseCanExecuteChanged();
    }
    internal void RefreshInherited()
    {
        foreach (var field in new[] { DescriptionOverride, SetupDatabaseOverride, SetupFunctionOverride }) field.RefreshInherited();
        foreach (var property in new[] { nameof(InheritedLabel), nameof(ResultsDatabaseName), nameof(ExecuteFunctionName) }) OnPropertyChanged(property);
        RefreshValidation();
    }
    private static void AddKey(ObservableCollection<WorkspaceTextEntry> rows, string value) => rows.Add(new(value, item => rows.Remove(item)));
    private static void AddSpan(ObservableCollection<WorkspaceParameterSpan> rows, string key, IEnumerable<string> values) => rows.Add(new(key, values, item => rows.Remove(item)));
    private static void AddMissing(ObservableCollection<WorkspaceParameterSpan> rows, IEnumerable<WorkspaceTextEntry>? keys)
    { foreach (var key in MissingKeys(rows, keys)) AddSpan(rows, key, []); }
    public void LoadProject(projectSettingsDto value)
    {
        if (!IsProject) throw new InvalidOperationException("Project fields belong to a project.");
        loading = true;
        try
        {
            description = value.description; resultsDatabaseName = value.resultsDatabaseName; executeFunctionName = value.executeFunctionName;
            setupDatabaseName = value.setupDatabaseName; setupFunctionName = value.setupFunctionName;
            LoadChoices(OutputTypes, value.outputDataTypes); LoadChoices(SetupOutputTypes, value.setupOutputDataTypes);
            OutputQueryKeys.Clear(); foreach (var key in value.outputQueryParameterKeys) AddKey(OutputQueryKeys, key);
            SetupQueryKeys.Clear(); foreach (var key in value.setupQueryParameterKeys) AddKey(SetupQueryKeys, key);
        }
        finally { loading = false; }
        OnPropertyChanged(""); NotifyChanged();
    }
    private static void LoadChoices(WorkspaceOutputChoice[] choices, int[] selected)
    {
        foreach (var choice in choices) choice.IsSelected = false;
        foreach (var value in selected) (choices.SingleOrDefault(choice => (int)choice.Value == value) ?? throw new ArgumentException("Unknown output type.")).IsSelected = true;
    }
    public void LoadRun(runDto value)
    {
        if (!IsRun) throw new InvalidOperationException("Run inputs belong to a run.");
        if (value.runKind != (IsBranch ? "branch" : "root")) throw new ArgumentException("A project can only contain runs of its own type.");
        loading = true;
        try
        {
            DescriptionOverride.Load(value.description); SetupDatabaseOverride.Load(value.setupDatabaseName); SetupFunctionOverride.Load(value.setupFunctionName);
            OutputParameters.Clear(); foreach (var span in value.outputParameterSpans) AddSpan(OutputParameters, span.keyName, span.values);
            SetupParameters.Clear(); foreach (var span in value.setupParameterSpans) AddSpan(SetupParameters, span.keyName, span.values);
        }
        finally { loading = false; }
        RefreshInherited(); NotifyChanged();
    }
    internal projectSettingsDto ToProjectSettings() => new()
    {
        description = Description, resultsDatabaseName = ResultsDatabaseName, executeFunctionName = ExecuteFunctionName,
        outputDataTypes = outputOrder.ToArray(), outputQueryParameterKeys = OutputQueryKeys.Select(key => key.Text).ToArray(),
        setupDatabaseName = IsBranch ? SetupDatabaseName : "", setupFunctionName = IsBranch ? SetupFunctionName : "",
        setupOutputDataTypes = IsBranch ? setupOutputOrder.ToArray() : [], setupQueryParameterKeys = IsBranch ? SetupQueryKeys.Select(key => key.Text).ToArray() : []
    };
    internal runDto ToRunInputs(Guid id, string name, string projectName) => new()
    {
        schemaVersion = 5, id = id, projectName = projectName, runName = name, runKind = IsBranch ? "branch" : "root",
        description = DescriptionOverride.OverrideValue, outputParameterSpans = OutputParameters.Select(span => span.ToDto()).ToArray(),
        setupDatabaseName = IsBranch ? SetupDatabaseOverride.OverrideValue : null, setupFunctionName = IsBranch ? SetupFunctionOverride.OverrideValue : null,
        setupParameterSpans = IsBranch ? SetupParameters.Select(span => span.ToDto()).ToArray() : []
    };
}
