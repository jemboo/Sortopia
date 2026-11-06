using System.Globalization;
using System.Windows.Threading;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Shared.Controls;

namespace Sortopia.WPF.ViewModels.Pages.Sgd.SubPages;

public sealed record GenerationDraw(string Method, int Channels, int Stages, int Seed, int Failures, int Inputs, string Coverage);

public sealed class SgdSamplingPageViewModel : SgdResultsViewModel
{
    private readonly SgdSetupPageViewModel setup;
    private readonly SgdOperation operation;
    private GenerationDraw? generationResult;
    private bool sampleToB;

    internal SgdSamplingPageViewModel(SgdSetupPageViewModel setup, SgdOperation operation, Action<SearchCandidate> evolve)
        : base("A", "B")
    {
        this.setup = setup;
        this.operation = operation;
        GenerateCommand = new RelayCommand(async () => { Generator.NewSeed(); await GenerateAsync(); }, () => CanGenerate(refreshSeed: true));
        BatchCommand = new RelayCommand(async () => await BatchAsync(), () => CanGenerate());
        NewSeedCommand = new RelayCommand(Generator.NewSeed, () => CanEdit);
        StopCommand = new RelayCommand(operation.Stop, () => IsRunning && operation.CanStop);
        EvolveSelectionCommand = new RelayCommand(() => evolve(NetworkHistory.Current!.Sample!), () => CanEvolveSelection);
        operation.Changed += NotifyCommands;
        Generator.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Generator.ChannelCount)) UpdateBestKnownNetworks(Generator.ChannelCount);
            NotifyCommands();
        };
        GenerationOptions.PropertyChanged += (_, _) => NotifyCommands();
        UpdateBestKnownNetworks(Generator.ChannelCount);
    }

    public NetworkGeneratorSettings Generator => setup.Generator;
    public SimpleGenerationSettings GenerationOptions => Generator.Options!;
    public bool SampleToB { get => sampleToB; set => SetProperty(ref sampleToB, value); }
    public bool CanEdit => operation.Activity is SgdActivity.Idle or SgdActivity.Evolve;
    public bool IsRunning => operation.Activity is SgdActivity.Generate or SgdActivity.Sample;
    public bool CanEvolveSelection => setup.IsValid && NetworkHistory.Current?.Sample is not null
        && operation.Activity is SgdActivity.Idle or SgdActivity.Sample;
    internal SearchCandidate? LatestCandidate { get; private set; }
    internal bool LatestMatchesSetup => LatestCandidate is not null && GenerationResult is { } draw
        && draw.Method == Generator.Method && draw.Channels == Generator.ChannelCount
        && int.TryParse(Generator.CountText, out var stages) && stages == draw.Stages;
    public NetworkCheckResult? Usage => LatestCandidate?.RawUsage;
    public long GeneratedCount { get; private set; }
    public string GenerationOutcomes => $"{FirstGroup.Success.TotalVisits + SecondGroup.Success.TotalVisits:N0} / {FirstGroup.Failure.TotalVisits + SecondGroup.Failure.TotalVisits:N0}";
    public GenerationDraw? GenerationResult
    {
        get => generationResult;
        private set { if (SetProperty(ref generationResult, value)) OnPropertyChanged(nameof(ShowResultPanes)); }
    }
    public override bool ShowResultPanes => GenerationResult is not null;
    protected override bool FollowGroupHistory => true;
    protected override void OnSelectionChanged() => EvolveSelectionCommand.RaiseCanExecuteChanged();
    protected override void OnBuffersCleared() => OnPropertyChanged(nameof(GenerationOutcomes));
    public RelayCommand GenerateCommand { get; }
    public RelayCommand BatchCommand { get; }
    public RelayCommand NewSeedCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand EvolveSelectionCommand { get; }

    private bool CanGenerate(bool refreshSeed = false) => CanEdit && setup.IsValid
        && GenerationOptions.Error.Length == 0 && (refreshSeed || Generator.SeedError.Length == 0);
    public Task GenerateAsync() => CanGenerate() ? StartAsync(repeat: false) : Task.CompletedTask;
    public Task BatchAsync() => CanGenerate() ? StartAsync(repeat: true) : Task.CompletedTask;

    private Task StartAsync(bool repeat)
    {
        var recipe = Generator.Snapshot();
        var group = SampleToB ? SecondGroup : FirstGroup;
        var samples = Generator.SamplesFor(recipe.Width);
        var useAvx512 = setup.UseAvx512;
        return operation.RunAsync(repeat ? SgdActivity.Sample : SgdActivity.Generate, async token =>
        {
            SetGeneratedCount(0);
            try
            {
                Status = repeat ? "Sampling random seeds. Choose Stop to finish the batch."
                    : "Generating, testing and compacting the starting network…";
                var simd = useAvx512 ? await Task.Run(() => new SimdNetworkCheck(recipe.Width, samples, token), token) : null;
                token.ThrowIfCancellationRequested();
                while (true)
                {
                    var draw = recipe;
                    var candidate = await Task.Run(() => NetworkSearch.Prepare(draw.Label, draw.CreateModel(), samples, token, simd), token);
                    token.ThrowIfCancellationRequested();
                    if (repeat) Generator.SeedText = draw.Seed.ToString(CultureInfo.InvariantCulture);
                    GenerationResult = new(draw.Method, draw.Width, draw.Size, draw.Seed,
                        candidate.Usage.Failed, candidate.Usage.Tested, candidate.Usage.Coverage);
                    Publish(candidate, group, repeat);
                    if (!repeat)
                    {
                        Status = "Sorter plotted. Choose Evolve to use the selected sorter as the parent.";
                        return;
                    }
                    await Dispatcher.Yield(DispatcherPriority.Background);
                    token.ThrowIfCancellationRequested();
                    recipe = recipe with { Seed = NetworkGeneratorSettings.NextSeed() };
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                if (operation.IsCurrent(token)) Status = "Random sampling stopped.";
            }
            catch (Exception error)
            {
                if (operation.IsCurrent(token)) Status = $"Generation could not finish. {error.Message}";
            }
            finally
            {
                if (operation.IsCurrent(token)) SetGeneratedCount(0);
            }
        });
    }

    private void Publish(SearchCandidate candidate, GraphBufferGroup group, bool repeat)
    {
        LatestCandidate = candidate;
        var entry = new NetworkHistoryEntry(candidate.RawNetwork, candidate.RawUsage,
            $"Buffer {group.Name} · {candidate.Usage.Failed:N0} failed, {candidate.Network.StageCount:N0} stages, {candidate.Network.ComparatorCount:N0} CEs", candidate.Position)
            { Group = group, Sample = candidate };
        if (repeat && NetworkHistory.Entries.Any(saved => saved.Group == group)) NetworkHistory.UpdateLive(entry);
        else NetworkHistory.Add(entry);
        RecordSnapshot(candidate, group, entry);
        if (repeat) SetGeneratedCount(GeneratedCount + 1);
        NotifyNetworkDisplay();
        OnPropertyChanged(nameof(Usage));
        OnPropertyChanged(nameof(LatestCandidate));
        OnPropertyChanged(nameof(GenerationOutcomes));
        NotifyCommands();
    }

    private void SetGeneratedCount(long count)
    {
        GeneratedCount = count;
        OnPropertyChanged(nameof(GeneratedCount));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(IsRunning));
        GenerateCommand.RaiseCanExecuteChanged();
        BatchCommand.RaiseCanExecuteChanged();
        NewSeedCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        EvolveSelectionCommand.RaiseCanExecuteChanged();
    }
}
