using System.Globalization;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Editable field drafts for the three catalog ID parameter types.</summary>
public sealed class WorkspaceLibraryIdValue : ViewModelBase
{
    private readonly WorkspaceTypedValue owner;
    private string channelCount = "", stageLength = "", mergeDimension = "", variant = "";
    private string? lastText;
    private parameterValueType lastType;
    private bool editingFields;
    private string[] catalogChoices = [];
    private static readonly string[] sorterVariants = ["VariantA", "VariantB", "VariantC", "Batcher", "BatcherBitonic"];
    private static readonly string[] prefixVariants = ["Prefix1", "PrefixA", "PrefixB", "PrefixC"];
    private static readonly Lazy<string[]> sorterChoices = new(() => SorterLib.ids().Select(SorterLibId.toString).ToArray());
    private static readonly Lazy<string[]> prefixChoices = new(() => PrefixLib.ids().Select(PrefixLibId.toString).ToArray());

    internal WorkspaceLibraryIdValue(WorkspaceTypedValue owner) { this.owner = owner; Refresh(); }
    public bool IsPrefix => owner.ValueType == parameterValueType.PrefixLibId;
    public bool IsMerge => owner.ValueType == parameterValueType.MergeLibId;
    public string[] Variants => IsPrefix ? prefixVariants : sorterVariants;
    public string ChannelCount { get => channelCount; set { if (SetProperty(ref channelCount, value)) FieldsChanged(); } }
    public string StageLength { get => stageLength; set { if (SetProperty(ref stageLength, value)) FieldsChanged(); } }
    public string MergeDimension { get => mergeDimension; set { if (SetProperty(ref mergeDimension, value)) FieldsChanged(); } }
    public string Variant { get => variant; set { if (value is not null && SetProperty(ref variant, value)) FieldsChanged(); } }
    public string Identifier => owner.Text;
    public string[] CatalogChoices => catalogChoices;
    public string? SelectedCatalogId
    {
        get => catalogChoices.Contains(owner.Text, StringComparer.Ordinal) ? owner.Text : null;
        set { if (value is not null) owner.Text = value; }
    }
    public string CatalogHint => IsMerge ? "Enter a channel count to see available merge constructions, or edit the fields below."
        : "Choose an available catalog entry to fill its fields, or edit the fields below.";
    private static bool Positive(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    public string Error
    {
        get
        {
            if (!Positive(ChannelCount, out var channels)) return "Channel count: enter a whole number from 1 to 2147483647.";
            if (IsPrefix && !Positive(StageLength, out _)) return "Stage length: enter a whole number from 1 to 2147483647.";
            if (IsMerge)
            {
                if (!Positive(MergeDimension, out var dimension)) return "Merge dimension: enter a whole number from 1 to 2147483647.";
                if (channels % dimension != 0) return "Merge dimension must divide the channel count exactly.";
            }
            if (!Variants.Contains(Variant, StringComparer.Ordinal)) return IsPrefix ? "Choose a prefix variant." : "Choose a sorter variant.";
            return ParameterValue.validationError(owner.ValueType, owner.Text);
        }
    }
    internal void Refresh()
    {
        if (editingFields || (lastText == owner.Text && lastType == owner.ValueType)) return;
        lastText = owner.Text; lastType = owner.ValueType;
        channelCount = stageLength = mergeDimension = variant = "";
        switch (ParameterValue.tryParse(owner.ValueType, owner.Text)?.Value)
        {
            case parameterValue.SorterLibIdValue value:
                channelCount = Number(value.Item.ChannelCount); variant = SorterLibVariant.toString(value.Item.SorterVariant); break;
            case parameterValue.PrefixLibIdValue value:
                channelCount = Number(value.Item.ChannelCount); stageLength = Number(value.Item.StageLength); variant = PrefixLibVariant.toString(value.Item.PrefixLibVariant); break;
            case parameterValue.MergeLibIdValue value:
                channelCount = Number(value.Item.ChannelCount); mergeDimension = Number(value.Item.MergeDimension); variant = SorterLibVariant.toString(value.Item.SorterLibVariant); break;
        }
        RefreshCatalog(); OnPropertyChanged("");
    }
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string NumberOrDraft(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? Number(value) : text;
    private void FieldsChanged()
    {
        editingFields = true;
        try
        {
            var channels = NumberOrDraft(ChannelCount);
            owner.Text = owner.ValueType switch
            {
                parameterValueType.SorterLibId => $"SorterLibId(channelCount={channels}, Variant={Variant})",
                parameterValueType.PrefixLibId => $"PrefixLibId(channelCount={channels}, stageLength={NumberOrDraft(StageLength)}, Variant={Variant})",
                parameterValueType.MergeLibId => $"MergeLibId(channelCount={channels}, mergeDimension={NumberOrDraft(MergeDimension)}, Variant={Variant})",
                _ => owner.Text
            };
            lastText = owner.Text; lastType = owner.ValueType;
        }
        finally { editingFields = false; }
        RefreshCatalog(); OnPropertyChanged("");
    }
    private void RefreshCatalog()
    {
        var choices = owner.ValueType switch
        {
            parameterValueType.SorterLibId => sorterChoices.Value,
            parameterValueType.PrefixLibId => prefixChoices.Value,
            parameterValueType.MergeLibId when Positive(ChannelCount, out var channels) => MergeLibId.availableForChannelCount(channels).Select(MergeLibId.toString).ToArray(),
            _ => []
        };
        if (!catalogChoices.SequenceEqual(choices)) catalogChoices = choices;
    }
}
