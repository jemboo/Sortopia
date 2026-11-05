using System.Diagnostics;
using System.Globalization;
using System.Runtime.Intrinsics.X86;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Catalog;
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
                RefreshGraphSelection();
            };
        foreach (var history in new[] { samplingHistory, evolutionHistory })
        {
            history.PropertyChanged += (_, change) =>
            {
                if (history == NetworkHistory && change.PropertyName == nameof(history.Current)) NotifyNetworkDisplay();
            };
        }
        SelectedAnnotation.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SorterAnnotation.VisiblePosition)) OnPropertyChanged(nameof(GraphSelectionPosition));
        };
        Mutation.SelectMethod(Generator.Method, Generator.ChannelCount);
        ReturnHomeCommand = new RelayCommand(returnHome);
        SelectGraphPointCommand = new RelayCommand(value => { if (value is Point point) ShowGraphNetwork(point); });
        SetupStepCommand = new RelayCommand(() => SelectStep(1), () => activity == Activity.Idle);
        GenerationStepCommand = new RelayCommand(() => SelectStep(2), () => activity == Activity.Idle && SetupValid);
        EvolutionStepCommand = new RelayCommand(() =>
        {
            if (activity == Activity.SampleSeeds) Stop();
            SelectStep(3);
        }, () => CanStartEvolution);
        GenerateCommand = CreateGenerationCommand(GenerateAsync);
        BatchCommand = CreateGenerationCommand(BatchAsync);
        EvolveSelectionCommand = new RelayCommand(EvolveSelection, () => samplingGraph && samplingHistory.Current?.Sample is not null
            && activity is Activity.Idle or Activity.SampleSeeds && SetupValid);
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
            NetworkHistory.RemoveGroup(group, samplingGraph ? remaining.Latest : null, clearSelection: samplingGraph);
            RefreshGraph();
            RefreshGraphSelection();
            OnPropertyChanged(nameof(GenerationOutcomes));
            ClearGraphCommand.RaiseCanExecuteChanged();
        });
        NewSeedCommand = new RelayCommand(Generator.NewSeed, () => CanEdit);
        UpdateBestKnownNetworks();
        GenerationOptions.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(GenerationOptions.Error))
            {
                GenerateCommand.RaiseCanExecuteChanged();
                BatchCommand.RaiseCanExecuteChanged();
            }
        };
        Generator.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName is nameof(Generator.Method) or nameof(Generator.ChannelCount) && incumbent is null)
                Mutation.SelectMethod(Generator.Method, Generator.ChannelCount);
            if (change.PropertyName == nameof(Generator.ChannelCount))
            {
                if (operation is not null && Generator.ChannelCount != activeWidth) Stop();
                Generator.SetTestWidth(Generator.ChannelCount);
                UpdateBestKnownNetworks();
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
    public bool ShowResultPanes => WizardStep == 3 || (WizardStep == 2 && GenerationResult is not null);
    public GenerationDraw? GenerationResult
    {
        get => generationResult;
        private set
        {
            var hadResult = generationResult is not null;
            if (SetProperty(ref generationResult, value) && hadResult != (value is not null))
                OnPropertyChanged(nameof(ShowResultPanes));
        }
    }
    public int? PlannedInputs => Generator.TestInputError.Length != 0 ? null
        : Generator.SamplesFor(Generator.ChannelCount) ?? (1 << Generator.ChannelCount);
    public string PlannedCoverage => Generator.TestInputError.Length != 0 ? "—" : Generator.AllSamples ? "Exhaustive" : "Sampled";
    public int? PlottedFailures => lastPlottedUsage?.Failed;
    private void SelectStep(int step)
    {
        if (step == 2 || step == 3) SetGraphMode(step == 2);
        if (SetProperty(ref wizardStep, step, nameof(WizardStep)))
        {
            OnPropertyChanged(nameof(ShowResultPanes));
            UpdateBestKnownNetworks();
            EvolutionStepCommand.RaiseCanExecuteChanged();
            RunSgdCommand.RaiseCanExecuteChanged();
            if (activity == Activity.Idle) Status = "";
        }
    }

    private void EvolveSelection()
    {
        if (samplingHistory.Current?.Sample is not { } parent) return;
        if (activity == Activity.SampleSeeds) Stop();
        incumbent = parent;
        SetProperty(ref generation, 0, nameof(Generation));
        SelectMutationMethod(parent);
        SelectStep(3);
        var entry = HistoryEntry(parent, 0, parents);
        evolutionHistory.Add(entry);
        evolutionHistory.FollowLiveCommand.Execute(null);
        RecordSnapshot(parent, parents, entry);
        UpdateUniqueness(1, 0);
        OnPropertyChanged(nameof(Usage));
        Status = "Selected sorter is ready. Choose Run SGD to start evolution.";
        NotifyCommands();
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
    public RelayCommand BatchCommand { get; }
    public RelayCommand EvolveSelectionCommand { get; }
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
    private bool HasEvolutionParent => incumbent is not null && SetupValid && (WizardStep == 3
        || GenerationResult is { } draw && draw.Method == Generator.Method && draw.Channels == Generator.ChannelCount
        && int.TryParse(Generator.CountText, out var stages) && stages == draw.Stages);
    private bool CanStartEvolution => (activity == Activity.Idle || (activity == Activity.SampleSeeds && GeneratedCount > 0))
        && HasEvolutionParent;
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
    public double MutationRate { get => Mutation.OverallRate.Value; set => Mutation.OverallRate.Value = value; }
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
    private SearchCandidate? DisplayFallback => samplingGraph || NetworkHistory.GraphPosition.HasValue ? null : incumbent;
    public NetworkDefinition? Network => NetworkHistory.Current?.Network ?? DisplayFallback?.RawNetwork;
    public NetworkCheckResult? DisplayUsage => NetworkHistory.Current is { } entry ? entry.Usage : DisplayFallback?.RawUsage;
    public Point? SelectedNetworkPosition => NetworkHistory.Current is { } entry
        ? entry.Position ?? new Point(entry.Network.ComparatorCount, entry.Network.StageCount)
        : DisplayFallback?.Position;
    // An empty graph selection keeps its cursor, while the Selected annotation describes only the viewer's sorter.
    public Point? GraphSelectionPosition => NetworkHistory.GraphPosition is { } point && NetworkHistory.Current is null
        ? point : SelectedAnnotation.VisiblePosition;
    public string DisplayedNetworkDescription => NetworkHistory.Current?.Description ?? "";
    public void ShowGraphNetwork(Point position)
    {
        var selected = FirstGroup.LatestVisibleAt(position) ?? SecondGroup.LatestVisibleAt(position);
        if (selected is null)
        {
            var known = VisibleCatalogNetworks.LastOrDefault(network => network.Position == position);
            if (known is not null) selected = new(known, null, known.BestKnownTitle, position);
        }
        NetworkHistory.Inspect(position, selected);
    }
    private void RefreshGraphSelection()
    {
        if (NetworkHistory.GraphPosition is { } point) ShowGraphNetwork(point);
    }
    private void NotifyNetworkDisplay()
    {
        SelectedAnnotation.Position = SelectedNetworkPosition;
        OnPropertyChanged(nameof(Network));
        OnPropertyChanged(nameof(DisplayUsage));
        OnPropertyChanged(nameof(DisplayedNetworkDescription));
        OnPropertyChanged(nameof(SelectedNetworkPosition));
        OnPropertyChanged(nameof(GraphSelectionPosition));
        EvolveSelectionCommand.RaiseCanExecuteChanged();
    }
    public NetworkCheckResult? Usage => incumbent?.RawUsage;
    public EvolutionVisits Visits
    {
        get => visits;
        private set
        {
            var hadVisits = visits.Current.HasValue;
            SetProperty(ref visits, value);
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
    public SorterAnnotation BestAnnotation { get; } = new("Best", Brushes.LimeGreen);
    public SorterAnnotation CurrentAnnotation { get; } = new("Current", Brushes.HotPink);
    public SorterAnnotation SelectedAnnotation { get; } = new("Selected", Brushes.White);
    private readonly Dictionary<sorterLibVariant, SorterAnnotation> catalogAnnotations = [];
    public IReadOnlyList<SorterAnnotation> Annotations { get; private set; } = [];
    public IReadOnlyList<CatalogNetwork> BestKnownNetworks { get; private set; } = [];
    public IReadOnlyList<CatalogNetwork> VisibleCatalogNetworks { get; private set; } = [];
    private void UpdateBestKnownNetworks()
    {
        var width = WizardStep == 3 && incumbent is not null ? incumbent.Network.Width : Generator.ChannelCount;
        BestKnownNetworks = CatalogNetwork.LoadForWidth(width);
        var items = new List<SorterAnnotation> { BestAnnotation, CurrentAnnotation, SelectedAnnotation };
        foreach (var network in BestKnownNetworks)
        {
            var variant = network.Id.SorterVariant;
            if (!catalogAnnotations.TryGetValue(variant, out var annotation))
            {
                annotation = new(network.VariantName, network.VariantBrush);
                annotation.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SorterAnnotation.IsShown)) UpdateVisibleCatalogNetworks();
                };
                catalogAnnotations.Add(variant, annotation);
            }
            annotation.Position = network.Position;
            items.Add(annotation);
        }
        Annotations = items;
        OnPropertyChanged(nameof(Annotations));
        OnPropertyChanged(nameof(BestKnownNetworks));
        UpdateVisibleCatalogNetworks();
    }
    private void UpdateVisibleCatalogNetworks()
    {
        VisibleCatalogNetworks = BestKnownNetworks.Where(n => catalogAnnotations[n.Id.SorterVariant].IsShown).ToArray();
        OnPropertyChanged(nameof(VisibleCatalogNetworks));
        RefreshGraphSelection();
    }
    public Point? BestPosition => BestAnnotation.Position;
    public string Summary
    {
        get
        {
            if (lastPlottedUsage is not { } usage) return "";
            var inputs = usage.Exhaustive ? $"all {usage.Tested:N0} binary inputs" : $"{usage.Tested:N0} sampled binary inputs";
            return $"{usage.Failed:N0} failures across {inputs}";
        }
    }

    private RelayCommand CreateGenerationCommand(Func<Task> generate) => new(async selection =>
    {
        if (selection is int width)
        {
            Generator.ChannelCount = width;
            if (Generator.ChannelCount != width) return;
        }
        await generate();
    }, () => CanEdit && CanGenerate);

    public Task GenerateAsync() => GenerateCommand.CanExecute(null) ? StartAsync(Activity.Generate) : Task.CompletedTask;

    public Task BatchAsync() => BatchCommand.CanExecute(null) ? StartAsync(Activity.SampleSeeds) : Task.CompletedTask;

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
                Status = "Sorter plotted. Choose Evolve to use the selected sorter as the parent.";
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
        Status = repeat ? "Sampling random seeds. Choose Stop to finish the batch."
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
        UpdateUniqueness(duplicates.UniqueCount, duplicates.DuplicateCount);
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
                UpdateUniqueness(duplicates.UniqueCount, duplicates.DuplicateCount);
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

    private void UpdateUniqueness(int unique, long repeats)
    {
        UniqueSorters = unique;
        DuplicateSorters = repeats;
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
            SelectMutationMethod(best);
            if (recordVisit) RecordSnapshot(best, group, entry);
            if (activity == Activity.SampleSeeds) SetGeneratedCount(GeneratedCount + 1);
            NotifyNetworkDisplay();
            OnPropertyChanged(nameof(Usage));
            if (!Visits.Current.HasValue)
            {
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

    private void SelectMutationMethod(SearchCandidate candidate) =>
        Mutation.SelectMethod(candidate.Model.IsMsce ? "msce" : candidate.Model.IsMssi ? "mssi" :
            candidate.Model.IsMsrs ? "msrs" : candidate.Model.IsMsuf4 ? "msuf4" : "msuf6", candidate.RawNetwork.Width);

    private NetworkHistoryEntry HistoryEntry(SearchCandidate candidate, int number, GraphBufferGroup? group = null)
    {
        group ??= parents;
        var source = group == samplesA || group == samplesB ? $"Buffer {group.Name}" : $"{group.Name} · Gen {number:N0}";
        return new(candidate.RawNetwork, candidate.RawUsage,
            $"{source} · {candidate.Usage.Failed:N0} failed, {candidate.Network.StageCount:N0} stages, {candidate.Network.ComparatorCount:N0} CEs", candidate.Position)
            { Group = group, Sample = group == samplesA || group == samplesB ? candidate : null };
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
            if (NetworkHistory.GraphPosition == point && NetworkHistory.Current is null) ShowGraphNetwork(point);
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
        SorterScore? bestPlotted = null;
        foreach (var buffer in visible)
            if (buffer.BestScore is { } score && (bestPlotted is not { } best || score.Improves(best))) bestPlotted = score;
        if (updatedVisits is null)
        {
            updatedVisits = EvolutionVisits.Merge(visible.Select(buffer => buffer.Visits));
            uniqueSortersByPoint = updatedVisits.Recount(point => GraphNetworkBuffer.CountDistinctAt(point, visible));
        }
        BestAnnotation.Position = bestPlotted?.Position;
        CurrentAnnotation.Position = updatedVisits.Current;
        Visits = updatedVisits;
        OnPropertyChanged(nameof(PlotData));
        OnPropertyChanged(nameof(BestPosition));
    }

    private void SetGeneratedCount(long count)
    {
        GeneratedCount = count;
        OnPropertyChanged(nameof(GeneratedCount));
    }

    private void NotifyCommands()
    {
        if (activity == Activity.Idle && wizardStep == 3 && !HasEvolutionParent) SelectStep(SetupValid ? 2 : 1);
        SetupStepCommand.RaiseCanExecuteChanged();
        GenerationStepCommand.RaiseCanExecuteChanged();
        EvolutionStepCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanChooseAvx512));
        OnPropertyChanged(nameof(CanChooseMutation));
        GenerateCommand.RaiseCanExecuteChanged();
        RunSgdCommand.RaiseCanExecuteChanged();
        BatchCommand.RaiseCanExecuteChanged();
        EvolveSelectionCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
        NewSeedCommand.RaiseCanExecuteChanged();
    }
}
