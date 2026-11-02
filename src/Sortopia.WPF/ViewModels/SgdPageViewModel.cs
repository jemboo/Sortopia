using System.Diagnostics;
using System.Globalization;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Threading;
using Sortopia.Models.Sorting;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.Rendering;

namespace Sortopia.WPF.ViewModels;

public sealed class SgdPageViewModel : ViewModelBase
{
    private enum Activity { Idle, Generate, SampleSeeds, Evolve }

    private SearchCandidate? incumbent;
    private EvolutionVisits visits = EvolutionVisits.Empty;
    private SorterScore? bestPlotted;
    private int? lastPlottedFailures;
    private CancellationTokenSource? operation;
    private Activity activity;
    private int activeWidth;
    private bool useAvx512;
    private bool sampleOnGenerate;
    private int generation;
    private int generationMilliseconds;
    private string status = "Generate a starting network or choose Run SGD to begin.";

    public SgdPageViewModel(Action returnHome)
    {
        ReturnHomeCommand = new RelayCommand(returnHome);
        GenerateCommand = new RelayCommand(async selection =>
        {
            if (CanEdit && selection is int width)
            {
                Generator.ChannelCount = width;
                if (Generator.ChannelCount != width) return;
            }
            await GenerateAsync();
        }, () => activity == Activity.SampleSeeds
            ? operation is { IsCancellationRequested: false } : CanEdit && CanGenerate);
        RunSgdCommand = new RelayCommand(async () => await RunSgdAsync(), () =>
            (activity == Activity.Idle || (activity == Activity.SampleSeeds && incumbent is not null && Generation == 0)) &&
            (HasSelectedWidth ? Generator.TestInputError.Length == 0 : CanGenerate));
        StopCommand = new RelayCommand(Stop, () => activity != Activity.SampleSeeds && operation is { IsCancellationRequested: false });
        ClearGraphCommand = new RelayCommand(() =>
        {
            bestPlotted = null;
            lastPlottedFailures = null;
            Visits = EvolutionVisits.Empty;
            OnPropertyChanged(nameof(BestGraphTitle));
            OnPropertyChanged(nameof(BestPosition));
        }, () => Visits.Current.HasValue);
        NewSeedCommand = new RelayCommand(() =>
        {
            Generator.NewSeed();
            sampleOnGenerate = true;
            Status = "Press Generate to sample fresh seeds continuously.";
        }, () => CanEdit);
        BestKnownNetworks = CatalogNetwork.LoadForWidth(Generator.ChannelCount);
        Generator.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Generator.ChannelCount))
            {
                if (operation is not null && Generator.ChannelCount != activeWidth) Stop();
                Generator.SetTestWidth(Generator.ChannelCount);
                BestKnownNetworks = CatalogNetwork.LoadForWidth(Generator.ChannelCount);
                OnPropertyChanged(nameof(BestKnownNetworks));
            }
            if (change.PropertyName == nameof(Generator.SeedText) && sampleOnGenerate)
            {
                sampleOnGenerate = false;
                Status = "Generate will use the entered seed once.";
            }
            if (change.PropertyName is nameof(Generator.InputError) or nameof(Generator.TestInputError)) NotifyCommands();
        };
    }

    public NetworkGeneratorSettings Generator { get; } = new();
    public RelayCommand GenerateCommand { get; }
    public RelayCommand RunSgdCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewSeedCommand { get; }
    public RelayCommand ClearGraphCommand { get; }
    public RelayCommand ReturnHomeCommand { get; }
    // The generator remains available during evolution so Generate can replace the run.
    public bool CanEdit => activity is Activity.Idle or Activity.Evolve;
    private bool HasSelectedWidth => incumbent?.Network.Width == Generator.ChannelCount;
    public bool CanChooseAvx512 => Avx512F.IsSupported && activity == Activity.Idle;
    public string Avx512Hint => Avx512F.IsSupported
        ? "Use Sortopia.Evaluation to test 512 binary inputs per vector. Choose before starting a run."
        : "AVX-512 is unavailable on this machine.";
    public bool UseAvx512
    {
        get => useAvx512;
        set { if (CanChooseAvx512) SetProperty(ref useAvx512, value); }
    }
    public long GeneratedCount { get; private set; }
    public string GenerateLabel => activity == Activity.SampleSeeds ? GeneratedCount.ToString("N0", CultureInfo.CurrentCulture) : "Generate";
    private bool CanGenerate => Generator.InputError.Length == 0 && (Generator.AllSamples || Generator.TestInputError.Length == 0);
    public int[] ChildChoices { get; } = [1, 2, 4, 8, 16, 32];
    public double[] RateChoices { get; } = [0.5, 0.1, 0.05, 0.02, 0.01, 0.005, 0.0025, 0.001, 0.0005, 0.00025];
    public int Children { get; set; } = 4;
    public double MutationRate { get; set; } = 0.1;
    public int Generation => generation;
    public int GenerationMilliseconds
    {
        get => generationMilliseconds;
        set { if (SetProperty(ref generationMilliseconds, Math.Clamp(value, 0, 500))) OnPropertyChanged(nameof(SpeedLabel)); }
    }
    public string SpeedLabel => GenerationMilliseconds == 0 ? "As quickly as possible" : $"{GenerationMilliseconds} ms per generation";
    public string Status { get => status; private set => SetProperty(ref status, value); }
    public NetworkDefinition? Network => incumbent?.Network;
    public NetworkCheckResult? Usage => incumbent?.Usage;
    public EvolutionVisits Visits
    {
        get => visits;
        private set
        {
            var hadVisits = visits.Current.HasValue;
            SetProperty(ref visits, value);
            OnPropertyChanged(nameof(CurrentGraphTitle));
            OnPropertyChanged(nameof(Summary));
            if (hadVisits != value.Current.HasValue) ClearGraphCommand.RaiseCanExecuteChanged();
        }
    }
    public IReadOnlyList<UsageBand> UsageBands => UsagePalette.Bands;
    public IReadOnlyList<CatalogNetwork> BestKnownNetworks { get; private set; }
    public Point? BestPosition => bestPlotted?.Position;
    public string BestGraphTitle => bestPlotted is { } best
        ? $"Best: Stages: {best.StageCount}, CE's: {best.ComparatorCount}"
        : "Best: No zero-failure sorter plotted";
    public string CurrentGraphTitle
    {
        get
        {
            var point = Visits.Current ?? incumbent?.Position ?? default;
            return $"Current: Stages: {point.Y:0}, CE's: {point.X:0}";
        }
    }
    public string Summary
    {
        get
        {
            if (Usage is not { } usage) return "";
            var inputs = usage.Exhaustive ? $"all {usage.Tested:N0} binary inputs" : $"{usage.Tested:N0} sampled binary inputs";
            return $"{lastPlottedFailures ?? usage.Failed:N0} failures across {inputs}";
        }
    }

    public Task GenerateAsync()
    {
        if (!GenerateCommand.CanExecute(null)) return Task.CompletedTask;
        if (activity != Activity.SampleSeeds) return StartAsync(sampleOnGenerate ? Activity.SampleSeeds : Activity.Generate);
        Stop();
        return Task.CompletedTask;
    }

    public Task RunSgdAsync() => RunSgdCommand.CanExecute(null) ? StartAsync(Activity.Evolve) : Task.CompletedTask;

    private async Task StartAsync(Activity nextActivity)
    {
        operation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        operation = cancellation;
        activity = nextActivity;
        sampleOnGenerate = false;
        try
        {
            var needsSeed = nextActivity != Activity.Evolve || !HasSelectedWidth;
            var recipe = needsSeed ? Generator.Snapshot() : null;
            var width = recipe?.Width ?? incumbent!.Network.Width;
            activeWidth = width;
            var testSamples = Generator.SamplesFor(width);
            SetGeneratedCount(0);
            if (needsSeed) Generator.SetTestWidth(width);
            NotifyCommands();
            var simd = UseAvx512
                ? await Task.Run(() => new SimdNetworkCheck(width, testSamples, token), token)
                : null;
            token.ThrowIfCancellationRequested();
            if (recipe is not null)
            {
                await GenerateNetworksAsync(recipe, testSamples, simd, nextActivity == Activity.SampleSeeds, token);
                token.ThrowIfCancellationRequested();
                if (nextActivity == Activity.Generate) { Status = "Generation 0 is ready."; return; }
            }

            var parent = incumbent!;
            if (parent.Usage.Exhaustive != (testSamples is null) || (testSamples is int count && count != parent.Usage.Tested))
            {
                Status = "Testing the parent on the new input set…";
                var previous = parent;
                parent = await Task.Run(() => NetworkSearch.Prepare(previous.Network.Label, previous.Model, testSamples, token, simd), token);
                token.ThrowIfCancellationRequested();
                Publish(parent, Generation, recordVisit: true);
            }
            await EvolveAsync(parent, simd, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (ReferenceEquals(operation, cancellation))
                Status = nextActivity == Activity.SampleSeeds ? "Random generation stopped. Sorter count reset to 0." : $"Stopped at generation {Generation:N0}.";
        }
        catch (Exception error)
        {
            if (ReferenceEquals(operation, cancellation)) Status = $"SGD could not finish. {error.Message}";
        }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                activity = Activity.Idle;
                SetGeneratedCount(0);
                NotifyCommands();
            }
        }
    }

    private async Task GenerateNetworksAsync(NetworkRecipe recipe, int? samples, SimdNetworkCheck? simd, bool repeat, CancellationToken token)
    {
        Status = repeat ? "Click the sorter count to stop sampling, or Run SGD to evolve the pink sorter."
            : "Generating, testing and compacting the starting network…";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var draw = recipe;
            var candidate = await Task.Run(() => NetworkSearch.Prepare(draw.Label, draw.CreateModel(), samples, token, simd), token);
            token.ThrowIfCancellationRequested();
            if (repeat) Generator.SeedText = draw.Seed.ToString(CultureInfo.InvariantCulture);
            Publish(candidate, 0, recordVisit: true);
            if (!repeat) return;
            await Dispatcher.Yield(DispatcherPriority.Background);
            recipe = recipe with { Seed = NetworkGeneratorSettings.NextSeed() };
        }
    }

    private async Task EvolveAsync(SearchCandidate parent, SimdNetworkCheck? simd, CancellationToken token)
    {
        Status = "Evolving. Stop to keep the current best network.";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var next = checked(Generation + 1);
            var children = Children;
            var mutator = RandomNetwork.createMutator(parent.Model, MutationRate);
            async Task RecordChild(SorterScore score, int evaluated)
            {
                RecordVisit(score);
                var target = TimeSpan.FromMilliseconds((double)GenerationMilliseconds * evaluated / children);
                var delay = target - Stopwatch.GetElapsedTime(started);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            parent = await NetworkSearch.NextGenerationAsync(parent, mutator, next, children, token, RecordChild, simd);
            token.ThrowIfCancellationRequested();
            Publish(parent, next);
        }
    }

    private void Publish(SearchCandidate best, int number, bool recordVisit = false)
    {
        var generationChanged = generation != number;
        generation = number;
        if (!ReferenceEquals(incumbent, best))
        {
            incumbent = best;
            if (recordVisit) RecordVisit(best.Score);
            if (activity == Activity.SampleSeeds) SetGeneratedCount(GeneratedCount + 1);
            OnPropertyChanged(nameof(Network));
            OnPropertyChanged(nameof(Usage));
            if (!Visits.Current.HasValue)
            {
                OnPropertyChanged(nameof(CurrentGraphTitle));
                OnPropertyChanged(nameof(Summary));
            }
        }
        if (generationChanged) OnPropertyChanged(nameof(Generation));
        if (activity == Activity.SampleSeeds) RunSgdCommand.RaiseCanExecuteChanged();
    }

    public void Stop()
    {
        if (operation is null) return;
        operation.Cancel();
        StopCommand.RaiseCanExecuteChanged();
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private void RecordVisit(SorterScore score)
    {
        lastPlottedFailures = score.Failures;
        if (score.Failures == 0 && (bestPlotted is not { } best || score.Improves(best)))
        {
            bestPlotted = score;
            OnPropertyChanged(nameof(BestGraphTitle));
            OnPropertyChanged(nameof(BestPosition));
        }
        Visits = Visits.Record(score.Position);
    }

    private void SetGeneratedCount(long count)
    {
        GeneratedCount = count;
        OnPropertyChanged(nameof(GeneratedCount));
        OnPropertyChanged(nameof(GenerateLabel));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanChooseAvx512));
        GenerateCommand.RaiseCanExecuteChanged();
        RunSgdCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NewSeedCommand.RaiseCanExecuteChanged();
    }
}
