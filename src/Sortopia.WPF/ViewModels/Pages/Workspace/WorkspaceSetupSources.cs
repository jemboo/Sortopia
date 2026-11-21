using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>A single source choice, including an explicit unassigned override.</summary>
public sealed class WorkspaceSetupSourceOverride : WorkspaceOverrideState
{
    private readonly Func<Guid> inherited;
    private Guid value;
    internal WorkspaceSetupSourceOverride(string title, string owner, Func<Guid> inherited,
        Func<WorkspaceConfigurationViewModel?> source, bool canOverride)
        : base(title, owner, source, canOverride) => this.inherited = inherited;
    public Guid Value
    {
        get => IsOverridden ? value : inherited();
        set { if (CanEditValue && this.value != value) { this.value = value; RefreshInherited(); NotifyChanged(); } }
    }
    protected override void SeedOverride() => value = inherited();
    internal void Load(Guid value) { this.value = value; LoadState(true); }
}

public sealed partial class WorkspaceConfigurationViewModel
{
    public bool ShowSetupSourcePreview => !CanEditDefaults;
    private readonly Dictionary<(string Kind, string Text), WorkspaceSetupSourceOverride> setupSourceOverrides = [];
    private Guid InheritedSetupSource(sortopiaDataTypeDto type) =>
        Parent?.EffectiveSetupDataTypes.FirstOrDefault(row => SameType(row.dataType, type))?.sourceProjectId ?? Guid.Empty;
    private Guid EffectiveSetupSource(sortopiaDataTypeDto type) =>
        setupSourceOverrides.TryGetValue((type.kind, type.value), out var state) && state.IsOverridden ? state.Value : InheritedSetupSource(type);
    private WorkspaceConfigurationViewModel? DefinedSetupSourceOwner(sortopiaDataTypeDto type) =>
        IsRun ? Parent?.DefinedSetupSourceOwner(type) : frozenSettings is not null ? this
        : setupSourceOverrides.TryGetValue((type.kind, type.value), out var state) && state.IsOverridden ? this
        : Parent?.DefinedSetupSourceOwner(type);
    private WorkspaceConfigurationViewModel SetupSourceOwner(sortopiaDataTypeDto type) =>
        DefinedSetupSourceOwner(type) ?? (IsRun ? Parent : null) ?? this;

    private WorkspaceSetupSourceOverride SetupSourceOverride(sortopiaDataTypeDto type)
    {
        var key = (type.kind, type.value);
        if (setupSourceOverrides.TryGetValue(key, out var existing)) return existing;
        var state = new WorkspaceSetupSourceOverride(type.kind, IsModule ? "module" : IsProject ? "project" : "run",
            () => InheritedSetupSource(type), () => Parent?.DefinedSetupSourceOwner(type), IsDefaults);
        state.FixedByFunction = () => !NeedsSource(type);
        state.IsReadOnly = IsReadOnly;
        state.Changed += NotifyChanged;
        setupSourceOverrides.Add(key, state);
        return state;
    }
    private void LoadSetupSources(setupSourceConfigDto[]? sources)
    {
        foreach (var state in setupSourceOverrides.Values) state.Changed -= NotifyChanged;
        setupSourceOverrides.Clear();
        foreach (var source in sources ?? []) SetupSourceOverride(source.dataType).Load(source.sourceProjectId);
        SetupDataTypes.Clear();
    }
    private setupSourceConfigDto[] LocalSetupSources => (DeclaredSetup ?? [])
        .Where(row => NeedsSource(row.dataType) && setupSourceOverrides.TryGetValue((row.dataType.kind, row.dataType.value), out var state) && state.IsOverridden)
        .Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = EffectiveSetupSource(row.dataType) }).ToArray();
    internal void PrepareSetupSource(string kind, string text)
    {
        var type = new sortopiaDataTypeDto { kind = kind, value = text };
        if (CanEditDefaults && (DeclaredSetup ?? []).Any(row => SameType(row.dataType, type)) && NeedsSource(type))
            SetupSourceOverride(type).IsOverridden = true;
    }
    private string SetupSourcesStatus => IsReadOnly ? "Locked · generated parameters" : IsRun ? "Inherited from project"
        : $"{InheritedSetupDataTypes.Count(row => row.RequiresSource) - LocalSetupSources.Length} inherited · {LocalSetupSources.Length} local";
}
