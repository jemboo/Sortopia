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

    public NetworkGeneratorSettings() : this(false) { }

    internal NetworkGeneratorSettings(bool configureSimpleGenerator)
    {
        if (configureSimpleGenerator)
        {
            Options = new();
            Options.Configure(method, channelCount);
            ApplyStageDefault();
        }
    }

    public SimpleGenerationSettings? Options { get; }
    public string StageDefaultDescription { get; private set; } = "";
    private bool UsesCeCount => Options is null && Method == "msce";

    private void ApplyStageDefault()
    {
        if (Options is null) return;
        // Both exhaustive and sampled checks use unrestricted binary inputs.
        var recommendation = SimpleSorterStageCounts.fullSortDefault(SimpleSorterModelType.fromString(Method), ChannelCount);
        var source = recommendation.IsEstimate ? "estimate" : "measured";
        StageDefaultDescription = $"{recommendation.Stages} ({source}, full sort)";
        OnPropertyChanged(nameof(StageDefaultDescription));
        CountText = recommendation.Stages.ToString(CultureInfo.InvariantCulture);
    }

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
            Options?.Configure(method, channelCount);
            ApplyStageDefault();
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
        set
        {
            if (!channelCounts.Contains(value) || !SetProperty(ref channelCount, value)) return;
            Options?.Configure(method, value);
            ApplyStageDefault();
            NotifyInputs();
        }
    }
    public string CountText { get => countText; set { if (SetProperty(ref countText, value)) NotifyInputs(); } }
    public string CountLabel => UsesCeCount ? "CE count (≤1M)" : "Stages (≤10,000)";
    public string WidthHint => Method switch
    {
        "msrs" => "Even channel counts, starting at 4.",
        "msuf4" => "2ⁿ channels, n > 1: 4, 8, 16, 32, 64.",
        "msuf6" => "3 × 2ⁿ channels, n > 1: 12, 24, 48.",
        _ => "2–64 channels."
    };
    public string SetupError => !ChannelCounts.Contains(ChannelCount) ? "Choose a supported channel count."
        : !int.TryParse(CountText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count < 1 || count > (UsesCeCount ? MaxCeCount : MaxStageCount)
            ? $"Enter a whole {(UsesCeCount ? "CE" : "stage")} count from 1 to {(UsesCeCount ? MaxCeCount : MaxStageCount)}." : "";
    public string SeedError => !int.TryParse(SeedText, NumberStyles.None, CultureInfo.InvariantCulture, out _) ? "Enter a seed from 0 to 2147483647." : "";
    public string InputError => SetupError.Length > 0 ? SetupError : SeedError;

    public string SamplesText
    {
        get => samplesText;
        set
        {
            if (!SetProperty(ref samplesText, value)) return;
            if (TestInputError.Length == 0) ApplyStageDefault();
            NotifyTestInputs();
        }
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
    private void NotifyInputs()
    {
        OnPropertyChanged(nameof(SetupError));
        OnPropertyChanged(nameof(SeedError));
        OnPropertyChanged(nameof(InputError));
    }
    private void NotifyTestInputs()
    {
        OnPropertyChanged(nameof(CanTestAll));
        OnPropertyChanged(nameof(TestHint));
        OnPropertyChanged(nameof(TestInputError));
    }

    internal string Label => $"{Method} · {ChannelCount} channels";
    internal string Description => $"{Label} · {CountText} {(UsesCeCount ? "CEs" : "stages")} · seed {SeedText}";
    internal NetworkRecipe Snapshot()
    {
        int size = int.Parse(CountText, CultureInfo.InvariantCulture);
        return new(Method, ChannelCount, size, int.Parse(SeedText, CultureInfo.InvariantCulture), Options?.CreateGenerator(size));
    }
}

internal sealed record NetworkRecipe(string Method, int Width, int Size, int Seed, simpleSorterModelGen? ConfiguredGenerator = null)
{
    internal string Label => $"{Method} · {Width} channels";
    internal simpleSorterModel CreateModel()
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, Seed);
        var id = new Guid(bytes);
        return ConfiguredGenerator is null ? RandomNetwork.generateModel(Method, Width, Size, id)
            : SimpleSorterModelGen.makeSorterModelFromId(id, ConfiguredGenerator);
    }
}
