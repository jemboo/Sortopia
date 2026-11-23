namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>A project owns this value; runs display it from their project.</summary>
public sealed class WorkspaceProjectField : WorkspaceFormItem
{
    private readonly WorkspaceConfigurationViewModel owner;
    private readonly Func<string> inherited;
    private readonly Func<WorkspaceConfigurationViewModel?> source;
    private string value;
    internal WorkspaceProjectField(string title, string initial, WorkspaceConfigurationViewModel owner,
        Func<string> inherited, Func<WorkspaceConfigurationViewModel?> source)
    { Title = title; value = initial; this.owner = owner; this.inherited = inherited; this.source = source; }
    public string Title { get; }
    public bool IsLocal => owner.IsProject;
    public bool CanEditValue => owner.IsProject && owner.IsEditable;
    public bool ShowPreview => !CanEditValue;
    public WorkspaceConfigurationViewModel? InheritedSource => owner.IsRun ? source() : null;
    public bool HasInheritedSource => InheritedSource is not null;
    public string SourceLabel => owner.IsReadOnly ? "Locked while generated run parameter files exist"
        : InheritedSource is { } parent ? $"Inherited from {parent.Name}" : "Defined on this project";
    public string EffectiveValue
    {
        get => owner.IsRun ? inherited() : value;
        set { if (CanEditValue && this.value != value) { this.value = value; RefreshInherited(); NotifyChanged(); } }
    }
    public string Error => owner.IsModule || !string.IsNullOrWhiteSpace(EffectiveValue) ? ""
        : $"Enter a {Title.ToLowerInvariant()} name in {(Title.StartsWith("Setup", StringComparison.Ordinal) ? "Setup" : "Results")} settings. Database names are defined on this project.";
    internal void Load(string? input) { value = input ?? ""; RefreshInherited(); }
    internal void RefreshInherited() => OnPropertyChanged("");
}
