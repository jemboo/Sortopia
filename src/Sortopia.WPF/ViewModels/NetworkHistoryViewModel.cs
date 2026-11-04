using System.Collections.ObjectModel;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

public sealed record NetworkHistoryEntry(NetworkDefinition Network, NetworkCheckResult? Usage, string Description, System.Windows.Point? Position = null)
{
    internal GraphBufferGroup? Group { get; init; }
}

/// <summary>A browsable list of network snapshots, independent of the active search parent.</summary>
public sealed class NetworkHistoryViewModel : ViewModelBase
{
    private readonly ObservableCollection<NetworkHistoryEntry> entries = [];
    private NetworkHistoryEntry? live;
    private int? selectedIndex;
    internal NetworkHistoryEntry? GraphPoint { get; private set; }
    // A live sample can be newer than every saved entry. Back then starts at the last saved entry.
    private int Cursor => selectedIndex ?? (entries.Count > 0 && ReferenceEquals(live, entries[^1]) ? entries.Count - 1 : entries.Count);
    public NetworkHistoryViewModel()
    {
        Entries = new(entries);
        PreviousCommand = new RelayCommand(() => Select(Cursor - 1), () => Cursor > 0);
        NextCommand = new RelayCommand(() => Select(Cursor + 1), () => Cursor + 1 < entries.Count);
        FollowLiveCommand = new RelayCommand(() => Select(null), () => GraphPoint is not null || selectedIndex.HasValue);
    }
    public ReadOnlyObservableCollection<NetworkHistoryEntry> Entries { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand FollowLiveCommand { get; }
    public NetworkHistoryEntry? Current => GraphPoint ?? (selectedIndex is int i ? entries[i] : live);
    public string Position => GraphPoint is not null ? "Graph point" : entries.Count == 0 ? "0 / 0"
        : Cursor == entries.Count ? "Live" : $"{Cursor + 1} / {entries.Count}";
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
        live = entry;
        NotifySelection();
    }
    internal void Inspect(NetworkHistoryEntry? entry)
    {
        GraphPoint = entry;
        NotifySelection();
    }
    internal void RemoveGroup(GraphBufferGroup group, NetworkHistoryEntry? remainingLive)
    {
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].Group != group) continue;
            entries.RemoveAt(i);
            if (i == selectedIndex) selectedIndex = null;
            else if (i < selectedIndex) selectedIndex--;
        }
        if (live?.Group == group) live = remainingLive ?? entries.LastOrDefault();
        NotifySelection();
    }
    private void Select(int? selected)
    {
        GraphPoint = null;
        selectedIndex = selected;
        NotifySelection();
    }
    private void NotifySelection()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(Position));
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        FollowLiveCommand.RaiseCanExecuteChanged();
    }
}
