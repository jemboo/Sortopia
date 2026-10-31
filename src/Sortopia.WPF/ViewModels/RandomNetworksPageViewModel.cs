using System.Globalization;
using Sortopia.Models.Sorting;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public sealed class RandomNetworksPageViewModel : NetworkPlaybackViewModel
{
    public const int MaxCeCount = 1_000_000;
    public const int MaxStageCount = 10_000;
    private string method = "msce";
    private int channelCount = 8;
    private string countText = "32";
    private string seedText = "1";
    private string status = "Generate a network, then test it or explore a single input with playback.";
    private string generationLabel = "msce · 8 channels · 32 CEs · seed 1";
    private bool isTesting;
    private bool hasCompacted;
    private bool isCompacting;
    private Sortopia.WPF.Rendering.CompactionTransition? compaction;
    private bool hasRemovedUnused;
    private string samplesText = "all";
    private NetworkCheckResult? usage;
    private bool showCeUsage = true;
    private CancellationTokenSource? testCancellation;

    public RandomNetworksPageViewModel(Action returnHome)
        : base(returnHome, GenerateNetwork("msce", 8, 32, 1))
    {
        RemoveUnusedCesCommand = new RelayCommand(RemoveUnusedCes, () => CanEdit && Usage is not null && !hasRemovedUnused);
        CompactStagesCommand = new RelayCommand(CompactStages, () => CanEdit && Usage is not null && hasRemovedUnused && !hasCompacted && SelectedNetwork.StageCount > 1);
        GenerateCommand = new RelayCommand(Generate, () => CanEdit && InputError.Length == 0);
        TestCommand = new RelayCommand(async () => await RunTestAsync(), () => CanEdit && TestInputError.Length == 0);
        NewSeedCommand = new RelayCommand(() => SeedText = System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue).ToString(CultureInfo.InvariantCulture), () => CanEdit);
        CancelTestCommand = new RelayCommand(CancelTest, () => IsTesting);
    }

    public RelayCommand RemoveUnusedCesCommand { get; }
    public RelayCommand CompactStagesCommand { get; }
    public int UnusedCeCount => Usage?.CeUses.Count(count => count == 0) ?? 0;
    public string RemoveUnusedHint => Usage is null ? "Run a test first. Removes individual CEs with no swaps."
        : $"{UnusedCeCount} CEs had no swaps in the completed test." + (Usage.Exhaustive ? "" : " Sample-only: unused on these inputs, not proven unused on every input.");
    private void NotifyTransforms()
    {
        OnPropertyChanged(nameof(UnusedCeCount));
        OnPropertyChanged(nameof(RemoveUnusedHint));
        RemoveUnusedCesCommand.RaiseCanExecuteChanged();
        CompactStagesCommand.RaiseCanExecuteChanged();
    }
    private void RemoveUnusedCes()
    {
        if (!CanEdit || Usage is null || hasRemovedUnused) return;
        if (UnusedCeCount == 0)
        {
            hasRemovedUnused = true;
            Status = "Removed unused: no unused CEs found.";
            NotifyTransforms();
            return;
        }
        var sampled = !Usage.Exhaustive;
        ApplyTransformation(NetworkTransforms.RemoveUnusedCes(SelectedNetwork, Usage), "Removed unused",
            sampled ? "Usage preserved from sampled inputs; this is not an exhaustive proof." : "Usage preserved from the completed test.", removeUnused: true);
        hasRemovedUnused = true;
        NotifyTransforms();
    }
    private void CompactStages()
    {
        if (!CanEdit || Usage is null || !hasRemovedUnused || hasCompacted || SelectedNetwork.StageCount <= 1) return;
        var compacted = NetworkTransforms.Compact(SelectedNetwork);
        if (compacted.StageLengths.SequenceEqual(SelectedNetwork.StageLengths) && compacted.Comparators.SequenceEqual(SelectedNetwork.Comparators))
        {
            hasCompacted = true;
            NotifyTransforms();
            Status = "Stages are already compact; no changes made.";
            return;
        }
        var before = Trace;
        var beforeUsage = Usage;
        Pause();
        SetCompacting(true);
        try
        {
            ApplyTransformation(compacted, "Compacted stages", "Comparator dependencies and recorded CE usage are preserved.");
            Compaction = new(before, Trace, beforeUsage, Usage);
        }
        catch { FinishCompaction(); throw; }
        hasCompacted = true;
        NotifyTransforms();
    }
    private void ApplyTransformation(NetworkDefinition network, string action, string detail, bool removeUnused = false)
    {
        if (removeUnused) hasCompacted = false;
        var before = SelectedNetwork;
        var input = Trace.Values[0].ToArray();
        Pause();
        var retainedUsage = NetworkTransforms.PreserveUsage(before, network, Usage, removeUnused);
        SelectedNetwork = network;
        Usage = retainedUsage;
        ReplaceTrace(NetworkTrace.CreateForPlayback(network, input));
        GenerationLabel = $"{network.Label} · edited network";
        Status = $"{action}: {before.StageCount} → {network.StageCount} stages, {before.ComparatorCount} → {network.ComparatorCount} CEs. {detail}";
        NotifyTransforms();
    }

    public string SamplesText
    {
        get => samplesText;
        set { if (SetProperty(ref samplesText, value)) NotifyTestInputs(); }
    }
    public bool CanTestAll => SelectedNetwork.Width <= NetworkCheck.MaxExhaustiveWidth;
    public string TestHint => CanTestAll
        ? $"Enter all for all {1 << SelectedNetwork.Width:N0} binary combinations, or 1–1,000,000 random samples."
        : "More than 18 channels: enter 1–1,000,000 random samples; all is unavailable.";
    private bool AllSamples => string.Equals(SamplesText.Trim(), "all", StringComparison.OrdinalIgnoreCase);
    public string TestInputError => AllSamples ? CanTestAll ? "" : "All inputs is available only through 18 channels."
        : int.TryParse(SamplesText, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count >= 1 && count <= NetworkCheck.MaxSamples
            ? "" : "Enter a sample count from 1 to 1,000,000, or all for 18 channels or fewer.";
    public NetworkCheckResult? Usage
    {
        get => usage;
        private set { SetProperty(ref usage, value); OnPropertyChanged(nameof(DisplayedUsage)); OnPropertyChanged(nameof(UsageSummary)); NotifyTransforms(); }
    }
    public bool ShowCeUsage
    {
        get => showCeUsage;
        set { if (SetProperty(ref showCeUsage, value)) OnPropertyChanged(nameof(DisplayedUsage)); }
    }
    public NetworkCheckResult? DisplayedUsage => ShowCeUsage ? Usage : null;
    public IReadOnlyList<Sortopia.WPF.Rendering.UsageBand> UsageBands => Sortopia.WPF.Rendering.UsagePalette.Bands;
    public string UsageSummary => Usage?.UsageSummary ?? "Run a test to measure CE usage. Usage counts actual swaps, not comparisons.";
    private void NotifyTestInputs()
    {
        OnPropertyChanged(nameof(CanTestAll));
        OnPropertyChanged(nameof(TestHint));
        OnPropertyChanged(nameof(TestInputError));
        TestCommand.RaiseCanExecuteChanged();
    }

    public string[] Methods { get; } = ["msce", "msrs", "mssi", "msuf4", "msuf6"];
    public RelayCommand NewSeedCommand { get; }
    public string SeedText { get => seedText; set { if (SetProperty(ref seedText, value)) NotifyInputs(); } }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public IReadOnlyList<int> ChannelCounts => Enumerable.Range(2, 63).Where(width => RandomNetwork.supportsWidth(Method, width)).ToArray();
    public string Method
    {
        get => method;
        set
        {
            if (!Methods.Contains(value) || !SetProperty(ref method, value)) return;
            OnPropertyChanged(nameof(ChannelCounts));
            if (!ChannelCounts.Contains(ChannelCount)) ChannelCount = ChannelCounts.First();
            OnPropertyChanged(nameof(CountLabel));
            OnPropertyChanged(nameof(WidthHint));
            NotifyInputs();
        }
    }
    public int ChannelCount { get => channelCount; set { if (SetProperty(ref channelCount, value)) NotifyInputs(); } }
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
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public string GenerationLabel { get => generationLabel; private set => SetProperty(ref generationLabel, value); }
    public bool IsTesting
    {
        get => isTesting;
        private set
        {
            SetProperty(ref isTesting, value);
            OnPropertyChanged(nameof(CanEdit));
            GenerateCommand.RaiseCanExecuteChanged();
            TestCommand.RaiseCanExecuteChanged();
            CancelTestCommand.RaiseCanExecuteChanged();
            NewSeedCommand.RaiseCanExecuteChanged();
            NotifyTransforms();
        }
    }
    public bool CanEdit => !IsTesting && !IsCompacting;
    public bool IsCompacting => isCompacting;
    public bool CanInteract => !IsCompacting;
    public Sortopia.WPF.Rendering.CompactionTransition? Compaction
    {
        get => compaction;
        private set => SetProperty(ref compaction, value);
    }
    private void SetCompacting(bool value)
    {
        SetPlaybackSuspended(value);
        SetProperty(ref isCompacting, value, nameof(IsCompacting));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanInteract));
        GenerateCommand.RaiseCanExecuteChanged();
        TestCommand.RaiseCanExecuteChanged();
        NewSeedCommand.RaiseCanExecuteChanged();
        NotifyTransforms();
    }
    public void FinishCompaction()
    {
        Compaction = null;
        SetCompacting(false);
    }

    private void NotifyInputs()
    {
        OnPropertyChanged(nameof(InputError));
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private static NetworkDefinition GenerateNetwork(string method, int width, int size, int seed) =>
        new($"{method} · {width} channels", RandomNetwork.generate(method, width, size, SeedId(seed)));

    private static Guid SeedId(int seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Clear();
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, seed);
        return new Guid(bytes);
    }

    private void Generate()
    {
        if (!CanEdit || InputError.Length > 0) return;
        try
        {
            var count = int.Parse(CountText, CultureInfo.InvariantCulture);
            var seed = int.Parse(SeedText, CultureInfo.InvariantCulture);
            SelectedNetwork = GenerateNetwork(Method, ChannelCount, count, seed);
            hasRemovedUnused = false;
            hasCompacted = false;
            Usage = null;
            if (!CanTestAll && AllSamples) SamplesText = "4096";
            NotifyTestInputs();
            GenerationLabel = $"{Method} · {ChannelCount} channels · {count} {(Method == "msce" ? "CEs" : "stages")} · seed {seed}";
            Status = "New network generated. Test it to check whether it sorts binary inputs.";
        }
        catch (Exception ex) { Status = $"Could not generate network: {ex.Message}"; }
    }

    public async Task RunTestAsync()
    {
        if (!CanEdit || TestInputError.Length > 0) return;
        Pause();
        var network = SelectedNetwork;
        int? samples = AllSamples ? null : int.Parse(SamplesText, CultureInfo.InvariantCulture);
        var total = samples ?? (1 << network.Width);
        hasRemovedUnused = false;
        Usage = null;
        using var cancellation = new CancellationTokenSource();
        testCancellation = cancellation;
        IsTesting = true;
        Status = $"Checking 0 / {total:N0} binary inputs…";
        var progress = new Progress<int>(done =>
        {
            if (IsTesting && ReferenceEquals(testCancellation, cancellation))
                Status = $"Checking {done:N0} / {total:N0} binary inputs…";
        });
        try
        {
            var result = await Task.Run(() => NetworkCheck.Run(network, samples, cancellation.Token, progress), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            Status = result.Description;
            Usage = result;
            if (result.Counterexample is { } input)
            {
                UseZeroOneRule = true;
                ReplaceTrace(NetworkTrace.CreateForPlayback(network, input));
            }
        }
        catch (OperationCanceledException) { Status = "Test cancelled."; }
        catch (Exception ex) { Status = $"Test could not finish: {ex.Message}"; }
        finally
        {
            testCancellation = null;
            IsTesting = false;
        }
    }

    public void CancelTest() => testCancellation?.Cancel();
    public void Leave() { Pause(); CancelTest(); FinishCompaction(); }
    public override void Dispose() { CancelTest(); FinishCompaction(); base.Dispose(); }
}
