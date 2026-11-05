using System.Collections.ObjectModel;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public sealed record NetworkHistoryEntry(NetworkDefinition Network, NetworkCheckResult? Usage, string Description, System.Windows.Point? Position = null)
{
    internal GraphBufferGroup? Group { get; init; }
    internal SearchCandidate? Sample { get; init; }
}

/// <summary>A browsable list of network snapshots, independent of the active search parent.</summary>
public sealed class NetworkHistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<NetworkHistoryEntry> entries = [];
    private NetworkHistoryEntry? live;
    // A cleared sample leaves the cursor before the first surviving entry.
    private const int BeforeFirst = -1;
    private int? selectedIndex;
    private string? pendingIndex;
    private NetworkHistoryEntry? graphEntry;
    internal System.Windows.Point? GraphPosition { get; private set; }
    // A live sample can be newer than every saved entry. Back then starts at the last saved entry.
    private int Cursor => selectedIndex ?? (entries.Count > 0 && ReferenceEquals(live, entries[^1]) ? entries.Count - 1 : entries.Count);
    public NetworkHistoryViewModel()
    {
        Entries = new(entries);
        PreviousCommand = new RelayCommand(() => Select(Cursor - 1), () => Cursor > 0);
        NextCommand = new RelayCommand(() => Select(Cursor + 1), () => Cursor + 1 < entries.Count);
        FollowLiveCommand = new RelayCommand(() => Select(null), () => GraphPosition.HasValue || (selectedIndex.HasValue && live is not null));
    }
    public ReadOnlyObservableCollection<NetworkHistoryEntry> Entries { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand FollowLiveCommand { get; }
    public NetworkHistoryEntry? Current => GraphPosition.HasValue ? graphEntry
        : selectedIndex switch { BeforeFirst => null, int i => entries[i], null => live };
    public bool HasEntries => entries.Count > 0;
    private string CurrentIndex => GraphPosition.HasValue ? "Graph" : !HasEntries ? "0"
        : Cursor == BeforeFirst ? "—" : Cursor == entries.Count ? "Live" : (Cursor + 1).ToString();
    public string Position => GraphPosition.HasValue || (HasEntries && Cursor == entries.Count)
        ? CurrentIndex : $"{CurrentIndex} / {entries.Count}";
    public string IndexText
    {
        get => pendingIndex ?? CurrentIndex;
        set => pendingIndex = value;
    }

    internal void FinishIndexEdit(bool commit)
    {
        if (pendingIndex is not { } text) return;
        pendingIndex = null;
        if (commit && int.TryParse(text, out var index) && index >= 1 && index <= entries.Count)
            Select(index - 1);
        else
            OnPropertyChanged(nameof(IndexText));
    }
    public void Replace(IEnumerable<NetworkHistoryEntry> networks)
    {
        var snapshots = networks.ToArray();
        entries.Clear();
        foreach (var entry in snapshots) entries.Add(entry);
        live = entries.LastOrDefault();
        Select(null);
    }
    public void Add(NetworkHistoryEntry entry)
    {
        entries.Add(entry);
        UpdateLive(entry);
    }
    internal void UpdateLive(NetworkHistoryEntry entry)
    {
        if (selectedIndex == BeforeFirst) selectedIndex = null;
        live = entry;
        NotifySelection();
    }
    internal void Inspect(System.Windows.Point position, NetworkHistoryEntry? entry)
    {
        pendingIndex = null;
        GraphPosition = position;
        graphEntry = entry;
        NotifySelection();
    }
    internal void RemoveGroup(GraphBufferGroup group, NetworkHistoryEntry? remainingLive, bool clearSelection)
    {
        var removedSelection = !GraphPosition.HasValue && clearSelection && Current?.Group == group;
        if (graphEntry?.Group == group) graphEntry = null;
        pendingIndex = null;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Group != group) continue;
            if (i == selectedIndex) selectedIndex = null;
            else if (i < selectedIndex) selectedIndex--;
            entries.RemoveAt(i);
        }
        if (live?.Group == group) live = remainingLive ?? entries.LastOrDefault();
        if (removedSelection)
        {
            Select(BeforeFirst);
            return;
        }
        NotifySelection();
    }
    private void Select(int? selected)
    {
        pendingIndex = null;
        GraphPosition = null;
        graphEntry = null;
        selectedIndex = selected;
        NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Position));
        OnPropertyChanged(nameof(HasEntries));
        if (pendingIndex is null) OnPropertyChanged(nameof(IndexText));
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        FollowLiveCommand.RaiseCanExecuteChanged();
    }
}
