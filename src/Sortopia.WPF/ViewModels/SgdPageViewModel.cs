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
    private EvolutionVisits visits = EvolutionVisits.Empty;
    private EvolutionVisits uniqueSortersByPoint = EvolutionVisits.Empty;
    private readonly GraphBufferGroup parents = new("Parents");
    private readonly GraphBufferGroup children = new("Children", isChild: true);
    private readonly GraphBufferGroup samplesA = new("A");
    private readonly GraphBufferGroup samplesB = new("B");
    private bool samplingGraph;
    private GraphBufferGroup samplingRunGroup;
    private bool sampleToB;
    public bool SampleToB { get => sampleToB; set => SetProperty(ref sampleToB, value); }
    private bool showVisitStats;
    public bool ShowVisitStats
    {
        get => showVisitStats;
        set
        {
            if (!SetProperty(ref showVisitStats, value)) return;
            OnPropertyChanged(nameof(PlotData));
            OnPropertyChanged(nameof(PlotLegendTitle));
        }
    }
    public EvolutionVisits PlotData => ShowVisitStats ? Visits : uniqueSortersByPoint;
    public string PlotLegendTitle => ShowVisitStats ? "Visits per point (log scale)" : "Unique sorters per point (log scale)";
    private bool showStageStats;
    public bool ShowStageStats { get => showStageStats; set => SetProperty(ref showStageStats, value); }
    public GraphBufferGroup FirstGroup => samplingGraph ? samplesA : parents;
    public GraphBufferGroup SecondGroup => samplingGraph ? samplesB : children;
    public GraphNetworkBuffer ParentSuccess => parents.Success;
    public GraphNetworkBuffer ParentFailure => parents.Failure;
    public GraphNetworkBuffer ChildSuccess => children.Success;
    public GraphNetworkBuffer ChildFailure => children.Failure;
    private GraphNetworkBuffer[] GraphBuffers => [FirstGroup.Success, FirstGroup.Failure, SecondGroup.Success, SecondGroup.Failure];
    public Rect AllBufferBounds
    {
        get
        {
            var bounds = Rect.Empty;
            foreach (var buffer in GraphBuffers) bounds.Union(buffer.Bounds);
            return bounds;
        }
    }
    private long snapshotOrder;
    private SorterScore? bestPlotted;
    private NetworkCheckResult? lastPlottedUsage;
    private CancellationTokenSource? operation;
    private Activity activity;
    private int activeWidth;
    private bool useAvx512 = Avx512F.IsSupported;
    public bool PlotFailures
    {
        get => FirstGroup.Failure.IsVisible && SecondGroup.Failure.IsVisible;
        set { FirstGroup.Failure.IsVisible = value; SecondGroup.Failure.IsVisible = value; }
    }
    public bool PlotChildren
    {
        get => ChildSuccess.IsVisible && ChildFailure.IsVisible;
        set { ChildSuccess.IsVisible = value; ChildFailure.IsVisible = value; }
    }
    private bool sampleOnGenerate;
    private int generation;
    private int generationMilliseconds;
    private string status = "";

    public SgdPageViewModel(Action returnHome)
    {
        samplingRunGroup = samplesA;
        foreach (var buffer in new[] { parents.Success, parents.Failure, children.Success, children.Failure,
                                       samplesA.Success, samplesA.Failure, samplesB.Success, samplesB.Failure })
            buffer.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(GraphNetworkBuffer.Bounds) && GraphBuffers.Contains(buffer))
                    OnPropertyChanged(nameof(AllBufferBounds));
                if (change.PropertyName != nameof(GraphNetworkBuffer.IsVisible)) return;
                OnPropertyChanged(nameof(PlotChildren));
                OnPropertyChanged(nameof(PlotFailures));
                RefreshGraph();
                if (NetworkHistory.GraphPoint?.Position is { } point) ShowGraphNetwork(point);
            };
        foreach (var history in new[] { samplingHistory, evolutionHistory })
        {
            history.PropertyChanged += (_, change) =>
            {
                if (history == NetworkHistory && change.PropertyName == nameof(history.Current)) NotifyNetworkDisplay();
            };
        }
        Mutation.SelectMethod(Generator.Method);
        ReturnHomeCommand = new RelayCommand(returnHome);
        SelectGraphPointCommand = new RelayCommand(value => { if (value is Point point) ShowGraphNetwork(point); });
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
            foreach (var buffer in GraphBuffers) buffer.Clear();
            OnPropertyChanged(nameof(GenerationOutcomes));
            NetworkHistory.Replace(Array.Empty<NetworkHistoryEntry>());

            RefreshGraph();
        }, () => GraphBuffers.Any(buffer => buffer.TotalVisits > 0) || NetworkHistory.Entries.Count > 0);
        ClearGroupCommand = new RelayCommand(value =>
        {
            if (value is not GraphBufferGroup group || (group != FirstGroup && group != SecondGroup)) return;
            group.Success.Clear();
            group.Failure.Clear();
            var remaining = group == FirstGroup ? SecondGroup : FirstGroup;
            NetworkHistory.RemoveGroup(group, samplingGraph ? remaining.Latest : null);
            RefreshGraph();
            if (NetworkHistory.GraphPoint is { Group: var inspected, Position: { } position } && inspected == group) ShowGraphNetwork(position);
            OnPropertyChanged(nameof(GenerationOutcomes));
            ClearGraphCommand.RaiseCanExecuteChanged();
        });
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
    public int? PlottedFailures => lastPlottedUsage?.Failed;
    private void SelectStep(int step)
    {
        if (step == 2 || step == 3) SetGraphMode(step == 2);
        if (SetProperty(ref wizardStep, step, nameof(WizardStep)) && activity == Activity.Idle) Status = "";
    }
    private void SetGraphMode(bool sampling)
    {
        if (samplingGraph == sampling) return;
        samplingGraph = sampling;
        OnPropertyChanged(nameof(AllBufferBounds));
        OnPropertyChanged(nameof(FirstGroup));
        OnPropertyChanged(nameof(SecondGroup));
        OnPropertyChanged(nameof(PlotFailures));
        OnPropertyChanged(nameof(NetworkHistory));
        NotifyNetworkDisplay();
        RefreshGraph();
        ClearGraphCommand.RaiseCanExecuteChanged();
    }
    public SorterMutationSettings Mutation { get; } = new();
    public RelayCommand GenerateCommand { get; }
    public RelayCommand RunSgdCommand { get; }
    public RelayCommand StopCommand { get; }
    public RelayCommand NewSeedCommand { get; }
    public RelayCommand ClearGraphCommand { get; }
    public RelayCommand ClearGroupCommand { get; }
    public RelayCommand ReturnHomeCommand { get; }
    public RelayCommand SelectGraphPointCommand { get; }
    // The generator remains available during evolution so Generate can replace the run.
    public bool CanEdit => activity is Activity.Idle or Activity.Evolve;
    private bool SetupValid => Generator.SetupError.Length == 0 && Generator.TestInputError.Length == 0;
    private bool HasMatchingPoint => GenerationResult is { } draw && incumbent is not null
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
    public string GenerationOutcomes => $"{samplesA.Success.TotalVisits + samplesB.Success.TotalVisits:N0} / {samplesA.Failure.TotalVisits + samplesB.Failure.TotalVisits:N0}";
    private bool CanGenerate => SetupValid && Generator.SeedError.Length == 0 && GenerationOptions.Error.Length == 0;
    public int[] ChildChoices { get; } = [1, 2, 4, 8, 16, 32];
    public int Children { get; set; } = 4;
    public double MutationRate { get => Mutation.Parameters[0].Value; set => Mutation.Parameters[0].Value = value; }
    public int UniqueSorters { get; private set; }
    public long DuplicateSorters { get; private set; }
    public string SorterUniqueness => $"{UniqueSorters:N0} unique / {DuplicateSorters:N0} repeats";
    public int Generation => generation;
    public int GenerationMilliseconds
    {
        get => generationMilliseconds;
        set { if (SetProperty(ref generationMilliseconds, Math.Clamp(value, 0, 500))) OnPropertyChanged(nameof(SpeedLabel)); }
    }
    public string SpeedLabel => GenerationMilliseconds == 0 ? "As quickly as possible" : $"{GenerationMilliseconds} ms per generation";
    public string Status { get => status; private set => SetProperty(ref status, value); }
    private readonly NetworkHistoryViewModel samplingHistory = new();
    private readonly NetworkHistoryViewModel evolutionHistory = new();
    public NetworkHistoryViewModel NetworkHistory => samplingGraph ? samplingHistory : evolutionHistory;
    public NetworkDefinition? Network => NetworkHistory.Current?.Network ?? incumbent?.RawNetwork;
    public NetworkCheckResult? DisplayUsage => NetworkHistory.Current is { } entry ? entry.Usage : incumbent?.RawUsage;
    public Point? SelectedNetworkPosition => NetworkHistory.Current?.Position;
    public string DisplayedNetworkDescription => NetworkHistory.Current?.Description ?? "";
    public void ShowGraphNetwork(Point position)
    {
        var selected = FirstGroup.LatestVisibleAt(position) ?? SecondGroup.LatestVisibleAt(position);
        if (selected is null)
        {
            var known = BestKnownNetworks.FirstOrDefault(network => network.Position == position);
            if (known is not null) selected = new(known, null, known.BestKnownTitle, position);
        }
        NetworkHistory.Inspect(selected);
    }
    private void NotifyNetworkDisplay()
    {
        OnPropertyChanged(nameof(Network));
        OnPropertyChanged(nameof(DisplayUsage));
        OnPropertyChanged(nameof(DisplayedNetworkDescription));
        OnPropertyChanged(nameof(SelectedNetworkPosition));
    }
    public NetworkCheckResult? Usage => incumbent?.RawUsage;
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
            if (lastPlottedUsage is not { } usage) return "";
            var inputs = usage.Exhaustive ? $"all {usage.Tested:N0} binary inputs" : $"{usage.Tested:N0} sampled binary inputs";
            return $"{usage.Failed:N0} failures across {inputs}";
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
        if (nextActivity != Activity.Evolve) samplingRunGroup = SampleToB ? samplesB : samplesA;
        SetGraphMode(nextActivity != Activity.Evolve);
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
            GenerationResult = new(draw.Method, draw.Width, draw.Size, draw.Seed,
                candidate.Usage.Failed, candidate.Usage.Tested, candidate.Usage.Coverage);
            Publish(candidate, 0, recordVisit: true);
            OnPropertyChanged(nameof(GenerationOutcomes));
            if (!repeat) return;
            await Dispatcher.Yield(DispatcherPriority.Background);
            recipe = recipe with { Seed = NetworkGeneratorSettings.NextSeed() };
        }
    }

    private async Task EvolveAsync(SearchCandidate parent, SimdNetworkCheck? simd, CancellationToken token)
    {
        var duplicates = new SgdDuplicateTracker();
        duplicates.Observe(parent.Fingerprint);
        UpdateUniqueness(duplicates);
        if (NetworkHistory.Entries.Count == 0 || !ReferenceEquals(NetworkHistory.Entries[^1].Network, parent.RawNetwork))
            NetworkHistory.Add(HistoryEntry(parent, Generation));
        if (parents.For(parent.Score).Find(parent.Position)?.Entry.Network != parent.RawNetwork)
            RecordCandidate(parent, Generation);
        Status = "Evolving. Stop to keep the current best network.";
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var started = Stopwatch.GetTimestamp();
            var next = checked(Generation + 1);
            var children = Children;
            var mutator = Mutation.Create(parent.Model);
            async Task RecordChild(SearchCandidate candidate, int evaluated)
            {
                UpdateUniqueness(duplicates);
                RecordCandidate(candidate, next, isChild: true);
                var target = TimeSpan.FromMilliseconds((double)GenerationMilliseconds * evaluated / children);
                var delay = target - Stopwatch.GetElapsedTime(started);
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            parent = await NetworkSearch.NextGenerationAsync(parent, mutator, next, children, token, simd: simd, candidateEvaluated: RecordChild, duplicates: duplicates);
            token.ThrowIfCancellationRequested();
            RecordCandidate(parent, next);
            Publish(parent, next);
        }
    }

    private void UpdateUniqueness(SgdDuplicateTracker duplicates)
    {
        UniqueSorters = duplicates.UniqueCount;
        DuplicateSorters = duplicates.DuplicateCount;
        OnPropertyChanged(nameof(UniqueSorters));
        OnPropertyChanged(nameof(DuplicateSorters));
        OnPropertyChanged(nameof(SorterUniqueness));
    }

    private void Publish(SearchCandidate best, int number, bool recordVisit = false)
    {
        var generationChanged = generation != number;
        generation = number;
        if (!ReferenceEquals(incumbent, best))
        {
            var improved = incumbent is { } previous && best.Improves(previous);
            incumbent = best;
            var group = RecordingGroup(isChild: false);
            var entry = HistoryEntry(best, number, group);
            if (activity == Activity.Evolve)
            {
                if (improved) NetworkHistory.Add(entry);
            }
            else PublishSample(entry);
            Mutation.SelectMethod(best.Model.IsMsce ? "msce" : best.Model.IsMssi ? "mssi" : best.Model.IsMsrs ? "msrs" : best.Model.IsMsuf4 ? "msuf4" : "msuf6");
            if (recordVisit) RecordSnapshot(best, group, entry);
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

    private NetworkHistoryEntry HistoryEntry(SearchCandidate candidate, int number, GraphBufferGroup? group = null)
    {
        group ??= parents;
        var source = group == samplesA || group == samplesB ? $"Buffer {group.Name}" : $"{group.Name} · Generation {number:N0}";
        return new(candidate.RawNetwork, candidate.RawUsage,
            $"{source} · {candidate.Usage.Failed:N0} failures · {candidate.Network.StageCount:N0} stages · {candidate.Network.ComparatorCount:N0} CEs", candidate.Position)
            { Group = group };
    }

    private void PublishSample(NetworkHistoryEntry entry)
    {
        if (activity == Activity.SampleSeeds && NetworkHistory.Entries.Any(saved => saved.Group == entry.Group))
            NetworkHistory.UpdateLive(entry);
        else
            NetworkHistory.Add(entry);
        ClearGraphCommand.RaiseCanExecuteChanged();
    }

    private GraphBufferGroup RecordingGroup(bool isChild) =>
        activity is Activity.Generate or Activity.SampleSeeds ? samplingRunGroup : isChild ? children : parents;

    internal void RecordCandidate(SearchCandidate candidate, int number, bool isChild = false)
    {
        var group = RecordingGroup(isChild);
        RecordSnapshot(candidate, group, HistoryEntry(candidate, number, group));
    }

    private void RecordSnapshot(SearchCandidate candidate, GraphBufferGroup group, NetworkHistoryEntry entry)
    {
        var buffer = group.For(candidate.Score);
        var added = buffer.Record(candidate.Score, ++snapshotOrder, entry, candidate.Fingerprint);
        if (buffer.IsVisible && (group == FirstGroup || group == SecondGroup))
        {
            var point = candidate.Position;
            var newToPlot = added && !GraphBuffers.Any(other => other != buffer && other.IsVisible && other.Contains(point, candidate.Fingerprint));
            uniqueSortersByPoint = newToPlot ? uniqueSortersByPoint.Record(point) : uniqueSortersByPoint.WithCurrent(point);
            RefreshGraph(Visits.Record(point));
        }
        ClearGraphCommand.RaiseCanExecuteChanged();
    }

    public void Stop()
    {
        if (operation is null) return;
        operation.Cancel();
        StopCommand.RaiseCanExecuteChanged();
        GenerateCommand.RaiseCanExecuteChanged();
    }

    private void RefreshGraph(EvolutionVisits? updatedVisits = null)
    {
        var visible = GraphBuffers.Where(buffer => buffer.IsVisible).OrderBy(buffer => buffer.LastOrder).ToArray();
        lastPlottedUsage = visible.LastOrDefault()?.Latest?.Usage;
        bestPlotted = null;
        foreach (var buffer in visible)
            if (buffer.BestScore is { } score && (bestPlotted is not { } best || score.Improves(best))) bestPlotted = score;
        if (updatedVisits is null)
        {
            updatedVisits = EvolutionVisits.Merge(visible.Select(buffer => buffer.Visits));
            uniqueSortersByPoint = updatedVisits.Recount(point => GraphNetworkBuffer.CountDistinctAt(point, visible));
        }
        Visits = updatedVisits;
        OnPropertyChanged(nameof(PlotData));
        OnPropertyChanged(nameof(BestGraphTitle));
        OnPropertyChanged(nameof(BestPosition));
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
