using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

public sealed class PrefixChoice
{
    private readonly ce[] comparators;
    internal PrefixChoice(string key, string name, ce[] comparators)
    {
        Key = key;
        Name = name;
        this.comparators = (ce[])comparators.Clone();
    }

    internal string Key { get; }
    public string Name { get; }
    internal ce[] Comparators => (ce[])comparators.Clone();
    internal bool Matches(ce[] prefix) => comparators.SequenceEqual(prefix);
}

/// <summary>Catalog prefixes for one width, plus the empty prefix.</summary>
public sealed class PrefixSelectorSettings : ViewModelBase
{
    private PrefixChoice selected;
    private int width;
    private static readonly PrefixChoice None = new("none", "None", []);

    public PrefixSelectorSettings() { selected = None; Configure(8); }

    public IReadOnlyList<PrefixChoice> Choices { get; private set; } = [None];
    public PrefixChoice Selected
    {
        get => selected;
        set
        {
            if (value is not null && Choices.Contains(value)) SetProperty(ref selected, value);
        }
    }

    internal void Configure(int channels)
    {
        if (width == channels) return;
        width = channels;
        var previousKey = selected.Key;
        Choices = new[] { None }.Concat(PrefixLib.ids().Where(id => id.ChannelCount == channels)
            .OrderBy(id => PrefixLibVariant.toString(id.PrefixLibVariant), StringComparer.Ordinal)
            .ThenBy(id => id.StageLength)
            .Select(id => new PrefixChoice($"{PrefixLibVariant.toString(id.PrefixLibVariant)}/{id.StageLength}",
                $"{PrefixLibVariant.toString(id.PrefixLibVariant)} · {id.StageLength} {(id.StageLength == 1 ? "stage" : "stages")}",
                SorterDataParse.getCeArrayFromPrefixLib(id).Value))).ToArray();
        // Update the backing selection before notifying WPF that its items have changed.
        selected = Choices.FirstOrDefault(choice => choice.Key == previousKey) ?? None;
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(Selected));
    }

    internal void SelectPrefix(int channels, ce[] prefix)
    {
        Configure(channels);
        var choice = Choices.FirstOrDefault(choice => choice.Matches(prefix));
        if (choice is null)
        {
            choice = new("custom", $"Custom · {prefix.Length} CEs", prefix);
            Choices = Choices.Append(choice).ToArray();
            OnPropertyChanged(nameof(Choices));
        }
        Selected = choice;
    }
}
