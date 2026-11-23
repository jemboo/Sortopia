using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed record WorkspaceParameterTypeChoice(parameterValueType Type, string Label);

public sealed class WorkspaceTypedValue : WorkspaceFormItem
{
    private readonly Func<bool> canEdit;
    private readonly Func<parameterValueType> valueType;
    private readonly Func<string>? key;
    private readonly Func<functionParameterSupport?>? support;
    private string text;
    public WorkspaceTypedValue(string text, Func<parameterValueType> valueType, Action<WorkspaceTypedValue>? remove = null, Func<string>? key = null, Func<bool>? canEdit = null,
        Func<functionParameterSupport?>? support = null)
    {
        this.canEdit = canEdit ?? (() => true);
        this.text = text; this.valueType = valueType; this.key = key;
        this.support = support;
        LibraryId = new(this); Weights = new(this); RemoveCommand = new RelayCommand(() => remove?.Invoke(this));
    }
    public string Text
    {
        get => text;
        set { if (canEdit() && SetProperty(ref text, value)) { RefreshType(); NotifyChanged(); } }
    }
    public parameterValueType ValueType => valueType();
    public bool IsBoolean => ValueType == parameterValueType.Boolean;
    public string[] Choices => WorkspaceParameterRules.Choices(support?.Invoke(), key?.Invoke(), ValueType);
    public bool HasChoices => Choices.Length > 0;
    public bool IsLibraryId => ValueType is parameterValueType.SorterLibId or parameterValueType.PrefixLibId or parameterValueType.MergeLibId;
    public WorkspaceLibraryIdValue LibraryId { get; }
    public bool IsWeights => ValueType is parameterValueType.OpsWeights or parameterValueType.Seed6Weights or parameterValueType.UnfoldingWeights;
    public WorkspaceGenerationWeights Weights { get; }
    public bool IsTextEditor => !IsBoolean && !HasChoices && !IsLibraryId && !IsWeights;
    public bool IsMultiline => ValueType == parameterValueType.Text;
    public bool IsNumeric => ValueType is parameterValueType.Integer or parameterValueType.Unsigned64 or parameterValueType.Floating;
    public bool[] BooleanChoices { get; } = [true, false];
    public bool? BooleanValue
    {
        get => bool.TryParse(Text, out var value) ? value : null;
        set { if (value is { } choice) Text = choice.ToString(); }
    }
    public string TypeLabel => ParameterValue.label(ValueType);
    public string Hint => WorkspaceParameterRules.Hint(support?.Invoke(), key?.Invoke(), ValueType);
    public string Error => IsLibraryId && LibraryId.Error is { Length: > 0 } error ? error
        : WorkspaceParameterRules.Error(support?.Invoke(), key?.Invoke(), ValueType, Text);
    public RelayCommand RemoveCommand { get; }
    internal void RefreshType() { LibraryId.Refresh(); Weights.Refresh(); OnPropertyChanged(""); }
}

public sealed class WorkspaceParameterBinding : WorkspaceFormItem
{
    private readonly Func<bool> canEdit;
    private readonly Func<string, WorkspaceParameterValueSource>? source;
    private readonly Func<IEnumerable<string>> suggestedKeys;
    private readonly Func<string, parameterBindingDto?> inheritedValue;
    private readonly Func<paramKeyDescriptor[]?> requirements;
    private readonly Func<IEnumerable<string>> queryKeys;
    private string[] keyChoices = [];
    private string key;
    private parameterValueType customType;
    public static string[] KnownKeys { get; } = ParamKeys.all.Select(key => key.Name).Order(StringComparer.Ordinal).ToArray();
    public static WorkspaceParameterTypeChoice[] ValueTypes { get; } = Enum.GetValues<parameterValueType>()
        .Select(type => new WorkspaceParameterTypeChoice(type, ParameterValue.label(type))).ToArray();
    public WorkspaceParameterBinding(string key, parameterValueType type, string value, Action<WorkspaceParameterBinding> remove,
        Func<string, WorkspaceParameterValueSource>? source = null, Func<IEnumerable<string>>? suggestedKeys = null,
        Func<string, parameterBindingDto?>? inheritedValue = null, Func<paramKeyDescriptor[]?>? requirements = null, Func<IEnumerable<string>>? queryKeys = null, Func<bool>? canEdit = null,
        Func<functionParameterSupport?>? support = null)
    {
        this.canEdit = canEdit ?? (() => true);
        this.key = key; customType = type; this.source = source;
        this.suggestedKeys = suggestedKeys ?? (() => []); this.inheritedValue = inheritedValue ?? (_ => null);
        this.requirements = requirements ?? (() => null);
        this.queryKeys = queryKeys ?? (() => []);
        Value = new(value, () => ValueType, key: () => Key, canEdit: this.canEdit, support: support);
        Value.Changed += RowChanged;
        RemoveCommand = new RelayCommand(() => { if (this.canEdit()) remove(this); }, this.canEdit);
        RefreshKeyChoices();
    }
    private paramKeyDescriptor? KnownKey => requirements()?.FirstOrDefault(value => value.Name == Key) ?? ParamKeys.tryFind(Key)?.Value;
    public string Key
    {
        get => key;
        set
        {
            if (!canEdit() || !SetProperty(ref key, value)) return;
            if (IsCustomKey && inheritedValue(value) is { } inherited) customType = (parameterValueType)inherited.valueType;
            RowChanged();
        }
    }
    public string[] KeyChoices => keyChoices;
    public bool HasRestrictedKeys => requirements() is not null;
    public string? SelectedKey
    {
        get => KeyChoices.Contains(Key, StringComparer.Ordinal) ? Key : null;
        set { if (value is not null && KeyChoices.Contains(value, StringComparer.Ordinal)) Key = value; }
    }

    private void RefreshKeyChoices()
    {
        var choices = (requirements() is { } required ? required.Select(value => value.Name) : KnownKeys.Concat(suggestedKeys()).Append(Key)).Where(value => !string.IsNullOrWhiteSpace(value))
            .Except(queryKeys(), StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (keyChoices.SequenceEqual(choices)) return;
        keyChoices = choices; OnPropertyChanged(nameof(KeyChoices));
    }
    public bool IsCustomKey => KnownKey is null;
    public parameterValueType ValueType
    {
        get => KnownKey?.ValueType ?? customType;
        set { if (canEdit() && IsCustomKey && SetProperty(ref customType, value)) RowChanged(); }
    }
    public string TypeHint => IsCustomKey ? "Choose the type for this custom key." : "Type defined by this parameter key.";
    public WorkspaceTypedValue Value { get; }
    public WorkspaceParameterValueSource? Source => source?.Invoke(Key);
    public RelayCommand RemoveCommand { get; }
    public string KeyError => string.IsNullOrWhiteSpace(Key) ? "Choose a required parameter key."
        : requirements() is { } required && !required.Any(value => value.Name == Key) ? $"‘{Key}’ is not required by the selected function. Remove this default or choose one of its required keys."
        : queryKeys().Contains(Key, StringComparer.Ordinal) ? $"‘{Key}’ is a project query key and must be supplied by a run span. Defaults and spans must use different keys." : "";
    public string Error => KeyError.Length > 0 ? KeyError : Value.Error;
    private void RowChanged() { RefreshKeyChoices(); Value.RefreshType(); OnPropertyChanged(""); NotifyChanged(); }
    internal void RefreshSource() { RefreshKeyChoices(); Value.RefreshType(); RemoveCommand.RaiseCanExecuteChanged(); OnPropertyChanged(""); }
    internal parameterBindingDto ToDto() => new() { keyName = Key, valueType = (int)ValueType, value = Value.Text };
}

public sealed record WorkspaceParameterValueSource(string SourceLabel, WorkspaceConfigurationViewModel? InheritedSource, bool IsOverridden = false)
{
    public bool IsLocal => IsOverridden;
    public bool HasInheritedSource => InheritedSource is not null;
}

public sealed record WorkspaceParameterBindingPreview(string Key, string TypeLabel, string Value, string Usage,
    WorkspaceParameterValueSource Source, RelayCommand? OverrideCommand, RelayCommand? ExcludeCommand = null)
{
    public bool CanOverride => OverrideCommand is not null;
    public bool CanExclude => ExcludeCommand is not null;
}
