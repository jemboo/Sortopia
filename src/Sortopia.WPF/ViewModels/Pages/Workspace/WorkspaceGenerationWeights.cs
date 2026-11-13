using System.Collections.ObjectModel;
using Sortopia.Runs;
using Sortopia.WPF.Commands;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

public sealed class WorkspaceModeWeight(string name, string text, Action changed) : ViewModelBase
{
    private string text = text;
    public string Name => name;
    public string Text { get => text; set { if (SetProperty(ref text, value)) changed(); } }
}

public sealed record WorkspaceWeightDistribution(string Name, WorkspaceModeWeight[] Modes);

/// <summary>Named mode fields for defaults and for each whole value in a span.</summary>
public sealed class WorkspaceGenerationWeights : ViewModelBase
{
    private readonly WorkspaceTypedValue owner;
    private string? lastText;
    private parameterValueType lastType;
    private bool editingFields;
    private static readonly string[] opsModes = ["Ortho", "Para", "SelfReflection"];
    private static readonly string[] seedModes = ["Ortho1", "Ortho2", "Para1", "Para2", "Para3", "Para4", "SelfReflection"];
    internal WorkspaceGenerationWeights(WorkspaceTypedValue owner)
    {
        this.owner = owner;
        AddLevelCommand = new(() => { Rows.Add(Row($"Unfolding level {Rows.Count + 1}", "1,1,1", opsModes)); FieldsChanged(); });
        RemoveLevelCommand = new(() => { Rows.RemoveAt(Rows.Count - 1); FieldsChanged(); }, () => Rows.Count > 0);
        Refresh();
    }
    public ObservableCollection<WorkspaceWeightDistribution> Rows { get; } = [];
    public bool IsUnfolding => owner.ValueType == parameterValueType.UnfoldingWeights;
    public RelayCommand AddLevelCommand { get; }
    public RelayCommand RemoveLevelCommand { get; }
    public string Hint => IsUnfolding
        ? "Levels are ordered from the seed outwards, one per doubling of the width. Each level's weights are reused in every stage. Use no levels at the seed width."
        : "Relative weights, normalized within this distribution. Zero excludes a mode. These weights are shared by every stage.";
    private WorkspaceWeightDistribution Row(string name, string text, string[] names)
    {
        var parts = text.Split(',');
        return new(name, names.Select((mode, index) => new WorkspaceModeWeight(mode, index < parts.Length ? parts[index] : "", FieldsChanged)).ToArray());
    }
    internal void Refresh()
    {
        if (editingFields || (lastText == owner.Text && lastType == owner.ValueType)) return;
        lastText = owner.Text; lastType = owner.ValueType;
        Rows.Clear();
        if (owner.ValueType == parameterValueType.OpsWeights) Rows.Add(Row("Mode weights", owner.Text, opsModes));
        if (owner.ValueType == parameterValueType.Seed6Weights) Rows.Add(Row("Seed weights", owner.Text, seedModes));
        if (IsUnfolding && !string.IsNullOrWhiteSpace(owner.Text))
            foreach (var text in owner.Text.Split(';')) Rows.Add(Row($"Unfolding level {Rows.Count + 1}", text, opsModes));
        RemoveLevelCommand.RaiseCanExecuteChanged(); OnPropertyChanged("");
    }
    private void FieldsChanged()
    {
        editingFields = true;
        try { owner.Text = string.Join(";", Rows.Select(row => string.Join(",", row.Modes.Select(mode => mode.Text)))); lastText = owner.Text; }
        finally { editingFields = false; }
        RemoveLevelCommand.RaiseCanExecuteChanged();
    }
}
