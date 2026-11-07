using System.Diagnostics;
using System.Windows.Threading;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Sorting.Networks;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Shared.Controls;

namespace Sortopia.WPF.ViewModels.Pages.Sgd.SubPages;

public sealed class SgdEvolutionPageViewModel : SgdResultsViewModel
{
    private readonly SgdSetupPageViewModel setup;
    private readonly SgdOperation operation;
    private int generation;
    private int generationMilliseconds;
    private bool syncingPrefix;

    internal SgdEvolutionPageViewModel(SgdSetupPageViewModel setup, SgdOperation operation)
        : base("Parents", "Children", secondIsChild: true)
    {
        this.setup = setup;
        this.operation = operation;
        RunSgdCommand = new RelayCommand(async () => await RunSgdAsync(), () => CanRun);
        StopCommand = new RelayCommand(operation.Stop, () => IsRunning && operation.CanStop);
        operation.Changed += NotifyCommands;
        setup.PropertyChanged += (_, _) => NotifyCommands();
        setup.Generator.PropertyChanged += (_, change) =>
        {
            if (Parent is null && change.PropertyName is nameof(NetworkGeneratorSettings.Method) or nameof(NetworkGeneratorSettings.ChannelCount))
            {
                Mutation.SelectMethod(setup.Generator.Method, setup.Generator.ChannelCount);
                Prefix.Configure(setup.Generator.ChannelCount);
                UpdateBestKnownNetworks(setup.Generator.ChannelCount);
            }
        };
        Mutation.SelectMethod(setup.Generator.Method, setup.Generator.ChannelCount);
        Prefix.PropertyChanged += (_, change) =>
        {
            if (syncingPrefix || change.PropertyName != nameof(Prefix.Selected)) return;
            if (!CanChoosePrefix) { SyncPrefix(); return; }
            PrefixChangeTask = ChangePrefixAsync(Prefix.Selected.Comparators);
        };
        UpdateBestKnownNetworks(setup.Generator.ChannelCount);
    }

    internal SearchCandidate? Parent { get; private set; }
    public bool HasParent => Parent is not null;
    public bool CanRun => operation.IsIdle && HasParent && setup.IsValid;
    public bool IsRunning => operation.Activity is SgdActivity.Evolve or SgdActivity.ChangePrefix;
    public bool CanChooseMutation => operation.IsIdle;
    public bool CanChoosePrefix => operation.IsIdle && HasParent;
    public PrefixSelectorSettings Prefix { get; } = new();
    internal Task PrefixChangeTask { get; private set; } = Task.CompletedTask;
    public override bool ShowResultPanes => HasParent;
    private protected override SearchCandidate? DisplayFallback => Parent;
    public NetworkCheckResult? Usage => Parent?.RawUsage;
    public GraphNetworkBuffer ParentSuccess => FirstGroup.Success;
    public GraphNetworkBuffer ParentFailure => FirstGroup.Failure;
    public GraphNetworkBuffer ChildSuccess => SecondGroup.Success;
    public GraphNetworkBuffer ChildFailure => SecondGroup.Failure;
    public SorterMutationSettings Mutation { get; } = new();
    public int[] ChildChoices { get; } = [1, 2, 4, 8, 16, 32];
    public int Children { get; set; } = 4;
    public int Generation => generation;
    public int GenerationMilliseconds
    {
        get => generationMilliseconds;
        set { if (SetProperty(ref generationMilliseconds, Math.Clamp(value, 0, 500))) OnPropertyChanged(nameof(SpeedLabel)); }
    }
    public string SpeedLabel => GenerationMilliseconds == 0 ? "As quickly as possible" : $"{GenerationMilliseconds} ms per generation";
    public int UniqueSorters { get; private set; }
    public long DuplicateSorters { get; private set; }
    public string SorterUniqueness => $"{UniqueSorters:N0} unique / {DuplicateSorters:N0} repeats";
    public RelayCommand RunSgdCommand { get; }
    public RelayCommand StopCommand { get; }

    internal void SetParent(SearchCandidate parent)
    {
        Parent = parent;
        SyncPrefix();
        SetProperty(ref generation, 0, nameof(Generation));
        Mutation.SelectMethod(parent.Model.IsMsce ? "msce" : parent.Model.IsMssi ? "mssi" :
            parent.Model.IsMsrs ? "msrs" : parent.Model.IsMsuf4 ? "msuf4" : "msuf6", parent.RawNetwork.Width);
        UpdateBestKnownNetworks(parent.Network.Width);
        var entry = HistoryEntry(parent, 0, FirstGroup);
        NetworkHistory.Add(entry);
        NetworkHistory.FollowLiveCommand.Execute(null);
        RecordSnapshot(parent, FirstGroup, entry);
        UpdateUniqueness(1, 0);
        OnPropertyChanged(nameof(HasParent));
        OnPropertyChanged(nameof(ShowResultPanes));
        OnPropertyChanged(nameof(Usage));
        NotifyNetworkDisplay();
        NotifyCommands();
        Status = "Selected sorter is ready. Choose Run SGD to start evolution.";
    }

    private void SyncPrefix()
    {
        syncingPrefix = true;
        try
        {
            if (Parent is { } parent) Prefix.SelectPrefix(parent.Network.Width, SimpleSorterModel.getPrefix(parent.Model));
        }
        finally { syncingPrefix = false; }
    }

    private Task ChangePrefixAsync(ce[] prefix) => operation.RunAsync(SgdActivity.ChangePrefix, async token =>
    {
        try
        {
            var previous = Parent!;
            var samples = setup.Generator.SamplesFor(previous.Network.Width);
            var useAvx512 = setup.UseAvx512;
            var label = $"{Mutation.Method} · {previous.Network.Width} channels · {Prefix.Selected.Name}";
            Status = "Testing the new prefix with the current suffix…";
            var candidate = await Task.Run(() =>
            {
                var model = SimpleSorterModel.withPrefix(prefix, previous.Model);
                var inputs = new PreparedNetworkCheck(previous.Network.Width, samples, prefix, useAvx512, token);
                return NetworkSearch.Prepare(label, model, samples, token, inputs);
            }, token);
            token.ThrowIfCancellationRequested();
            SetParent(candidate);
            Status = "Prefix updated. Choose Run SGD to start from this sorter.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (operation.IsCurrent(token)) { SyncPrefix(); Status = "Prefix change stopped. The previous parent is retained."; }
        }
        catch (Exception error)
        {
            if (operation.IsCurrent(token)) { SyncPrefix(); Status = $"Prefix could not be changed. {error.Message}"; }
        }
    });

    public Task RunSgdAsync() => CanRun ? operation.RunAsync(SgdActivity.Evolve, EvolveAsync) : Task.CompletedTask;

    private async Task EvolveAsync(CancellationToken token)
    {
        try
        {
            var parent = Parent!;
            var samples = setup.Generator.SamplesFor(parent.Network.Width);
            var useAvx512 = setup.UseAvx512;
            var inputs = await Task.Run(() => new PreparedNetworkCheck(parent.Network.Width, samples,
                SimpleSorterModel.getPrefix(parent.Model), useAvx512, token), token);
            token.ThrowIfCancellationRequested();
            if (samples != parent.RequestedSamples)
            {
                Status = "Testing the parent on the new input set…";
                var previous = parent;
                parent = await Task.Run(() => NetworkSearch.Prepare(previous.Network.Label, previous.Model, samples, token, inputs), token);
                token.ThrowIfCancellationRequested();
                Publish(parent, Generation);
                RecordCandidate(parent, Generation);
            }
            var duplicates = new SgdDuplicateTracker();
            duplicates.Observe(parent.Fingerprint);
            UpdateUniqueness(duplicates.UniqueCount, duplicates.DuplicateCount);
            if (NetworkHistory.Entries.Count == 0 || !ReferenceEquals(NetworkHistory.Entries[^1].Network, parent.RawNetwork))
                NetworkHistory.Add(HistoryEntry(parent, Generation, FirstGroup));
            if (FirstGroup.For(parent.Score).Find(parent.Position)?.Entry.Network != parent.RawNetwork)
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
                    token.ThrowIfCancellationRequested();
                    UpdateUniqueness(duplicates.UniqueCount, duplicates.DuplicateCount);
                    RecordCandidate(candidate, next, isChild: true);
                    var delay = TimeSpan.FromMilliseconds((double)GenerationMilliseconds * evaluated / children) - Stopwatch.GetElapsedTime(started);
                    if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                    await Dispatcher.Yield(DispatcherPriority.Background);
                }
                parent = await NetworkSearch.NextGenerationAsync(parent, mutator, next, children, token, inputs: inputs, candidateEvaluated: RecordChild, duplicates: duplicates);
                token.ThrowIfCancellationRequested();
                RecordCandidate(parent, next);
                Publish(parent, next);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (operation.IsCurrent(token)) Status = $"Stopped at generation {Generation:N0}.";
        }
        catch (Exception error)
        {
            if (operation.IsCurrent(token)) Status = $"Evolution could not finish. {error.Message}";
        }
    }

    internal void Publish(SearchCandidate candidate, int number)
    {
        var changed = generation != number;
        generation = number;
        if (!ReferenceEquals(Parent, candidate))
        {
            var improved = Parent is { } previous && candidate.Improves(previous);
            Parent = candidate;
            if (improved) NetworkHistory.Add(HistoryEntry(candidate, number, FirstGroup));
            NotifyNetworkDisplay();
            OnPropertyChanged(nameof(Usage));
        }
        if (changed) OnPropertyChanged(nameof(Generation));
    }

    private static NetworkHistoryEntry HistoryEntry(SearchCandidate candidate, int number, GraphBufferGroup group) =>
        new(candidate.RawNetwork, candidate.RawUsage,
            $"{group.Name} · Gen {number:N0} · {candidate.Usage.Failed:N0} failed, {candidate.Network.StageCount:N0} stages, {candidate.Network.ComparatorCount:N0} CEs", candidate.Position) { Group = group };

    internal void RecordCandidate(SearchCandidate candidate, int number, bool isChild = false)
    {
        var group = isChild ? SecondGroup : FirstGroup;
        RecordSnapshot(candidate, group, HistoryEntry(candidate, number, group));
    }

    private void UpdateUniqueness(int unique, long repeats)
    {
        UniqueSorters = unique;
        DuplicateSorters = repeats;
        OnPropertyChanged(nameof(UniqueSorters));
        OnPropertyChanged(nameof(DuplicateSorters));
        OnPropertyChanged(nameof(SorterUniqueness));
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanChooseMutation));
        OnPropertyChanged(nameof(CanChoosePrefix));
        RunSgdCommand.RaiseCanExecuteChanged();
        StopCommand.RaiseCanExecuteChanged();
    }
}
