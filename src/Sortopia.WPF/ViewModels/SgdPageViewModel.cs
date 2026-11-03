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

public sealed record GenerationDraw(string Method, int Channels, int Stages, int Seed, int Failures, int Inputs, string Coverage);

public sealed class SgdPageViewModel : ViewModelBase
{
    private enum Activity { Idle, Generate, SampleSeeds, Evolve }

    private SearchCandidate? incumbent;
    private int wizardStep = 1;
    private GenerationDraw? generationResult;
    private long successfulDraws;
    private long failedDraws;
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
    private string status = "";

    public SgdPageViewModel(Action returnHome)
    {
        Mutation.SelectMethod(Generator.Method);
        ReturnHomeCommand = new RelayCommand(returnHome);
        SetupStepCommand = new RelayCommand(() => SelectStep(1), () => activity == Activity.Idle);
        GenerationStepCommand = new RelayCommand(() => SelectStep(2), () => activity == Activity.Idle && SetupValid);
        EvolutionStepCommand = new RelayCommand(() =>
        {
            if (activity == Activity.SampleSeeds) Stop();
            SelectStep(3);
        }, () => CanStartEvolution);
        GenerateCommand = new RelayCommand(async selection =>
        {
            if (CanEdit && selection is int width)
            {
                Generator.ChannelCount = width;
                if (Generator.ChannelCount != width) return;
            }
            await GenerateAsync();
        }, () => CanEdit && CanGenerate);
        RunSgdCommand = new RelayCommand(async () => await RunSgdAsync(), () => CanStartEvolution);
        StopCommand = new RelayCommand(Stop, () => operation is { IsCancellationRequested: false });
        ClearGraphCommand = new RelayCommand(() =>
        {
            bestPlotted = null;
            lastPlottedFailures = null;
            successfulDraws = failedDraws = 0;
            OnPropertyChanged(nameof(GenerationOutcomes));
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
        GenerationOptions.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(GenerationOptions.Error)) GenerateCommand.RaiseCanExecuteChanged();
        };
        Generator.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(Generator.Method) && incumbent is null) Mutation.SelectMethod(Generator.Method);
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
            if (change.PropertyName == nameof(Generator.TestInputError))
            {
                OnPropertyChanged(nameof(PlannedInputs));
                OnPropertyChanged(nameof(PlannedCoverage));
            }
            if (change.PropertyName is nameof(Generator.InputError) or nameof(Generator.TestInputError)) NotifyCommands();
        };
    }

    public NetworkGeneratorSettings Generator { get; } = new(configureSimpleGenerator: true);
    public SimpleGenerationSettings GenerationOptions => Generator.Options!;
    public int WizardStep => wizardStep;
    public RelayCommand SetupStepCommand { get; }
    public RelayCommand GenerationStepCommand { get; }
    public RelayCommand EvolutionStepCommand { get; }
    public GenerationDraw? GenerationResult { get => generationResult; private set => SetProperty(ref generationResult, value); }
    public int? PlannedInputs => Generator.TestInputError.Length != 0 ? null
        : Generator.SamplesFor(Generator.ChannelCount) ?? (1 << Generator.ChannelCount);
    public string PlannedCoverage => Generator.TestInputError.Length != 0 ? "—" : Generator.AllSamples ? "Exhaustive" : "Sampled";
    public int? PlottedFailures => lastPlottedFailures;
    private void SelectStep(int step)
    {
        if (SetProperty(ref wizardStep, step, nameof(WizardStep)) && activity == Activity.Idle) Status = "";
    }
    public SorterMutationSettings Mutation { get; } = new();
    public RelayCommand GenerateCommand { get; }
    public RelayCommand RunSgdCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewSeedCommand { get; }
    public RelayCommand ClearGraphCommand { get; }
    public RelayCommand ReturnHomeCommand { get; }
    // The generator remains available during evolution so Generate can replace the run.
    public bool CanEdit => activity is Activity.Idle or Activity.Evolve;
    private bool SetupValid => Generator.SetupError.Length == 0 && Generator.TestInputError.Length == 0;
    private bool HasMatchingPoint => Visits.Current.HasValue && GenerationResult is { } draw && incumbent is not null
        && draw.Method == Generator.Method && draw.Channels == Generator.ChannelCount
        && int.TryParse(Generator.CountText, out var stages) && stages == draw.Stages && SetupValid;
    private bool CanStartEvolution => (activity == Activity.Idle || (activity == Activity.SampleSeeds && GeneratedCount > 0))
        && HasMatchingPoint;
    public bool CanChooseAvx512 => Avx512F.IsSupported && activity == Activity.Idle;
    public bool CanChooseMutation => activity == Activity.Idle;
    public string Avx512Hint => Avx512F.IsSupported
        ? "Use Sortopia.Evaluation to test 512 binary inputs per vector. Choose before starting a run."
        : "AVX-512 is unavailable on this machine.";
    public bool UseAvx512
    {
        get => useAvx512;
        set { if (CanChooseAvx512) SetProperty(ref useAvx512, value); }
    }
    public long GeneratedCount { get; private set; }
    public string GenerationOutcomes => $"{successfulDraws:N0} / {failedDraws:N0}";
    private bool CanGenerate => SetupValid && Generator.SeedError.Length == 0 && GenerationOptions.Error.Length == 0;
    public int[] ChildChoices { get; } = [1, 2, 4, 8, 16, 32];
    public int Children { get; set; } = 4;
    public double MutationRate { get => Mutation.Parameters[0].Value; set => Mutation.Parameters[0].Value = value; }
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
            OnPropertyChanged(nameof(PlottedFailures));
            if (hadVisits != value.Current.HasValue)
            {
                ClearGraphCommand.RaiseCanExecuteChanged();
                NotifyCommands();
            }
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
        return StartAsync(sampleOnGenerate ? Activity.SampleSeeds : Activity.Generate);
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
            var recipe = nextActivity == Activity.Evolve ? null : Generator.Snapshot();
            var width = recipe?.Width ?? incumbent!.Network.Width;
            activeWidth = width;
            var testSamples = Generator.SamplesFor(width);
            SetGeneratedCount(0);
            if (recipe is not null) Generator.SetTestWidth(width);
            NotifyCommands();
            var simd = UseAvx512
                ? await Task.Run(() => new SimdNetworkCheck(width, testSamples, token), token)
                : null;
            token.ThrowIfCancellationRequested();
            if (recipe is not null)
            {
                await GenerateNetworksAsync(recipe, testSamples, simd, nextActivity == Activity.SampleSeeds, token);
                token.ThrowIfCancellationRequested();
                Status = "Point plotted. Open Evolution to use this sorter as the parent.";
                return;
            }

            var parent = incumbent!;
            if (testSamples != (parent.Usage.Exhaustive ? (int?)null : parent.Usage.Tested))
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
                Status = nextActivity == Activity.SampleSeeds ? "Random sampling stopped." : $"Stopped at generation {Generation:N0}.";
        }
        catch (Exception error)
        {
            if (ReferenceEquals(operation, cancellation))
                Status = $"{(nextActivity == Activity.Evolve ? "Evolution" : "Generation")} could not finish. {error.Message}";
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
        Status = repeat ? "Sampling random seeds. Choose Stop or open Evolution to keep the current draw."
            : "Generating, testing and compacting the starting network…";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var draw = recipe;
            var candidate = await Task.Run(() => NetworkSearch.Prepare(draw.Label, draw.CreateModel(), samples, token, simd), token);
            token.ThrowIfCancellationRequested();
            if (repeat) Generator.SeedText = draw.Seed.ToString(CultureInfo.InvariantCulture);
            if (candidate.Usage.Failed == 0) successfulDraws++;
            else failedDraws++;
            OnPropertyChanged(nameof(GenerationOutcomes));
            GenerationResult = new(draw.Method, draw.Width, draw.Size, draw.Seed,
                candidate.Usage.Failed, candidate.Usage.Tested, candidate.Usage.Coverage);
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
            var mutator = Mutation.Create(parent.Model);
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
            Mutation.SelectMethod(best.Model.IsMsce ? "msce" : best.Model.IsMssi ? "mssi" : best.Model.IsMsrs ? "msrs" : best.Model.IsMsuf4 ? "msuf4" : "msuf6");
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
        if (activity == Activity.SampleSeeds)
        {
            EvolutionStepCommand.RaiseCanExecuteChanged();
            RunSgdCommand.RaiseCanExecuteChanged();
        }
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
    }

    private void NotifyCommands()
    {
        if (activity == Activity.Idle && wizardStep == 3 && !HasMatchingPoint) SelectStep(SetupValid ? 2 : 1);
        SetupStepCommand.RaiseCanExecuteChanged();
        GenerationStepCommand.RaiseCanExecuteChanged();
        EvolutionStepCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanChooseAvx512));
        OnPropertyChanged(nameof(CanChooseMutation));
        GenerateCommand.RaiseCanExecuteChanged();
        RunSgdCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NewSeedCommand.RaiseCanExecuteChanged();
    }
}
