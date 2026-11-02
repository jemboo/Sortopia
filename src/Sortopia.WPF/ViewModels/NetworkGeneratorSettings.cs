using System.Globalization;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public sealed class NetworkGeneratorSettings : ViewModelBase
{
    public const int MaxCeCount = 1_000_000;
    public const int MaxStageCount = 10_000;
    private string method = "msce";
    private int channelCount = 8;
    private IReadOnlyList<int> channelCounts = SupportedWidths("msce");
    private string countText = "32";
    private string seedText = "1";
    private string samplesText = "all";
    private int testWidth = 8;

    public string[] Methods { get; } = ["msce", "msrs", "mssi", "msuf4", "msuf6"];
    public string SeedText { get => seedText; set { if (SetProperty(ref seedText, value)) NotifyInputs(); } }
    private static IReadOnlyList<int> SupportedWidths(string kind) =>
        Array.AsReadOnly(Enumerable.Range(2, 63).Where(width => RandomNetwork.supportsWidth(kind, width)).ToArray());

    public IReadOnlyList<int> ChannelCounts => channelCounts;
    public string Method
    {
        get => method;
        set
        {
            if (!Methods.Contains(value) || method == value) return;
            method = value;
            channelCounts = SupportedWidths(value);
            channelCount = channelCounts.Contains(channelCount) ? channelCount : channelCounts[0];
            OnPropertyChanged(nameof(Method));
            OnPropertyChanged(nameof(ChannelCounts));
            // Reapply the selection after WPF replaces the items, even if the width is still supported.
            OnPropertyChanged(nameof(ChannelCount));
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(WidthHint));
            NotifyInputs();
        }
    }
    public int ChannelCount
    {
        get => channelCount;
        set { if (channelCounts.Contains(value) && SetProperty(ref channelCount, value)) NotifyInputs(); }
    }
    public string CountText { get => countText; set { if (SetProperty(ref countText, value)) NotifyInputs(); } }
    public string CountLabel => Method == "msce" ? "CE count (≤1M)" : "Stages (≤10,000)";
    public string WidthHint => Method switch
    {
        "msrs" => "Even channel counts, starting at 4.",
        "msuf4" => "2ⁿ channels, n > 1: 4, 8, 16, 32, 64.",
        "msuf6" => "3 × 2ⁿ channels, n > 1: 12, 24, 48.",
        _ => "2–64 channels."
    };
    public string InputError => !ChannelCounts.Contains(ChannelCount) ? "Choose a supported channel count."
        : !int.TryParse(CountText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > (Method == "msce" ? MaxCeCount : MaxStageCount)
            ? $"Enter a whole {(Method == "msce" ? "CE" : "stage")} count from 1 to {(Method == "msce" ? MaxCeCount : MaxStageCount)}." : !int.TryParse(SeedText, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? "Enter a seed from 0 to 2147483647." : "";

    public string SamplesText
    {
        get => samplesText;
        set { if (SetProperty(ref samplesText, value)) NotifyTestInputs(); }
    }
    public bool CanTestAll => testWidth <= NetworkCheck.MaxExhaustiveWidth;
    public string TestHint => CanTestAll
        ? $"Enter all for all {1 << testWidth:N0} binary combinations, or 1–1,000,000 random samples."
        : "More than 18 channels: enter 1–1,000,000 random samples; all is unavailable.";
    internal bool AllSamples => string.Equals(SamplesText.Trim(), "all", StringComparison.OrdinalIgnoreCase);
    public string TestInputError => AllSamples ? CanTestAll ? "" : "All inputs is available only through 18 channels."
        : int.TryParse(SamplesText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 1 && count <= NetworkCheck.MaxSamples
            ? "" : "Enter a sample count from 1 to 1,000,000, or all for 18 channels or fewer.";

    internal void SetTestWidth(int width)
    {
        testWidth = width;
        if (!CanTestAll && AllSamples) SamplesText = "4096";
        NotifyTestInputs();
    }

    internal int? SamplesFor(int width) => AllSamples
        ? width <= NetworkCheck.MaxExhaustiveWidth ? null : 4096
        : int.Parse(SamplesText, CultureInfo.InvariantCulture);

    internal static int NextSeed() => System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue);
    internal void NewSeed() => SeedText = NextSeed().ToString(CultureInfo.InvariantCulture);
    private void NotifyInputs() => OnPropertyChanged(nameof(InputError));
    private void NotifyTestInputs()
    {
        OnPropertyChanged(nameof(CanTestAll));
        OnPropertyChanged(nameof(TestHint));
        OnPropertyChanged(nameof(TestInputError));
    }

    internal string Label => $"{Method} · {ChannelCount} channels";
    internal string Description => $"{Label} · {CountText} {(Method == "msce" ? "CEs" : "stages")} · seed {SeedText}";
    internal NetworkRecipe Snapshot() => new(Method, ChannelCount, int.Parse(CountText, CultureInfo.InvariantCulture),
        int.Parse(SeedText, CultureInfo.InvariantCulture));
}

internal sealed record NetworkRecipe(string Method, int Width, int Size, int Seed)
{
    internal string Label => $"{Method} · {Width} channels";
    internal simpleSorterModel CreateModel()
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, Seed);
        return RandomNetwork.generateModel(Method, Width, Size, new Guid(bytes));
    }
}
