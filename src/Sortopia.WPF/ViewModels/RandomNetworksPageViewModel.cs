using Sortopia.Models.Sorting;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public sealed class RandomNetworksPageViewModel : NetworkPlaybackViewModel
{
    public const int MaxCeCount = NetworkGeneratorSettings.MaxCeCount;
    public const int MaxStageCount = NetworkGeneratorSettings.MaxStageCount;
    private string status = "Generate a network, then test it or explore a single input with playback.";
    private string generationLabel = "msce · 8 channels · 32 CEs · seed 1";
    private bool isTesting;
    private bool hasCompacted;
    private bool isCompacting;
    private Sortopia.WPF.Rendering.CompactionTransition? compaction;
    private bool hasRemovedUnused;
    private NetworkCheckResult? usage;
    private bool showCeUsage = true;
    private CancellationTokenSource? testCancellation;

    public RandomNetworksPageViewModel(Action returnHome) : this(returnHome, new NetworkGeneratorSettings()) { }

    private RandomNetworksPageViewModel(Action returnHome, NetworkGeneratorSettings generator)
        : base(returnHome, GenerateNetwork(generator))
    {
        Generator = generator;
        RemoveUnusedCesCommand = new RelayCommand(RemoveUnusedCes, () => CanEdit && Usage is not null && !hasRemovedUnused);
        CompactStagesCommand = new RelayCommand(CompactStages, () => CanEdit && Usage is not null && hasRemovedUnused && !hasCompacted && SelectedNetwork.StageCount > 1);
        GenerateCommand = new RelayCommand(Generate, () => CanEdit && InputError.Length == 0);
        TestCommand = new RelayCommand(async () => await RunTestAsync(), () => CanEdit && TestInputError.Length == 0);
        NewSeedCommand = new RelayCommand(generator.NewSeed, () => CanEdit);
        CancelTestCommand = new RelayCommand(CancelTest, () => IsTesting);
        PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SelectedNetwork)) generator.SetTestWidth(SelectedNetwork.Width);
        };
        generator.PropertyChanged += (_, change) =>
        {
            OnPropertyChanged(change.PropertyName);
            if (change.PropertyName == nameof(InputError)) GenerateCommand.RaiseCanExecuteChanged();
            if (change.PropertyName == nameof(TestInputError)) TestCommand.RaiseCanExecuteChanged();
        };
    }

    public RelayCommand RemoveUnusedCesCommand { get; }
    public NetworkGeneratorSettings Generator { get; }
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

    public string SamplesText { get => Generator.SamplesText; set => Generator.SamplesText = value; }
    public bool CanTestAll => Generator.CanTestAll;
    public string TestHint => Generator.TestHint;
    public string TestInputError => Generator.TestInputError;
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
    public string[] Methods => Generator.Methods;
    public RelayCommand NewSeedCommand { get; }
    public string SeedText { get => Generator.SeedText; set => Generator.SeedText = value; }
    public RelayCommand GenerateCommand { get; }
    public RelayCommand TestCommand { get; }
    public RelayCommand CancelTestCommand { get; }
    public IReadOnlyList<int> ChannelCounts => Generator.ChannelCounts;
    public string Method { get => Generator.Method; set => Generator.Method = value; }
    public int ChannelCount { get => Generator.ChannelCount; set => Generator.ChannelCount = value; }
    public string CountText { get => Generator.CountText; set => Generator.CountText = value; }
    public string CountLabel => Generator.CountLabel;
    public string WidthHint => Generator.WidthHint;
    public string InputError => Generator.InputError;
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

    private static NetworkDefinition GenerateNetwork(NetworkGeneratorSettings generator) =>
        new(generator.Label, RandomNetwork.toStages(generator.Snapshot().CreateModel()));

    private void Generate()
    {
        if (!CanEdit || InputError.Length > 0) return;
        try
        {
            SelectedNetwork = GenerateNetwork(Generator);
            hasRemovedUnused = false;
            hasCompacted = false;
            Usage = null;
            GenerationLabel = Generator.Description;
            Status = "New network generated. Test it to check whether it sorts binary inputs.";
        }
        catch (Exception ex) { Status = $"Could not generate network: {ex.Message}"; }
    }

    public async Task RunTestAsync()
    {
        if (!CanEdit || TestInputError.Length > 0) return;
        Pause();
        var network = SelectedNetwork;
        var samples = Generator.SamplesFor(network.Width);
        var total = samples ?? (1 << network.Width);
        hasRemovedUnused = false;
        Usage = null;
        using var cancellation = new CancellationTokenSource();
        testCancellation = cancellation;
        IsTesting = true;
        Status = $"Checking 0 / {total:N0} binary inputs…";
        void ShowProgress(string message)
        {
            if (IsTesting && ReferenceEquals(testCancellation, cancellation))
                Status = message;
        }
        try
        {
            var progress = new Progress<int>(done => ShowProgress($"Checking {done:N0} / {total:N0} binary inputs…"));
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
