using System.Diagnostics;
using System.Windows.Threading;
using Sortopia.WPF.Commands;
using Sortopia.WPF.Models;

namespace Sortopia.WPF.ViewModels;

/// <summary>Selects catalog/input data and plays an immutable trace. Ticks never execute a network.</summary>
public class NetworkPlaybackViewModel : ViewModelBase, IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch clock = new();
    private NetworkDefinition selectedNetwork;
    private NetworkTrace trace;
    private double position;
    private double speed = 1;
    private double zoom = 1;
    private int stepMode;
    private bool isPlaying;
    private bool disposed;
    private bool playbackSuspended;
    private bool followPlayback = true;
    private bool useZeroOneRule;

    public NetworkPlaybackViewModel(Action returnHome, NetworkDefinition network)
    {
        selectedNetwork = network;
        trace = MakeReversedTrace(selectedNetwork);
        ReturnHomeCommand = new RelayCommand(() => { Pause(); returnHome(); });
        PlayPauseCommand = new RelayCommand(TogglePlayback, () => !playbackSuspended && StepCount > 0);
        PreviousCommand = new RelayCommand(() => SeekPosition = Math.Ceiling(Position) - 1, () => !playbackSuspended && Position > 0);
        NextCommand = new RelayCommand(() => SeekPosition = Math.Floor(Position) + 1, () => !playbackSuspended && Position < StepCount);
        ResetCommand = new RelayCommand(() => SeekPosition = 0, () => !playbackSuspended);
        ReverseCommand = new RelayCommand(() => ReplaceTrace(MakeReversedTrace(SelectedNetwork)), () => !playbackSuspended);
        ShuffleCommand = new RelayCommand(() =>
        {
            var values = UseZeroOneRule
                ? Enumerable.Range(0, SelectedNetwork.Width).Select(_ => Random.Shared.Next(2)).ToArray()
                : Enumerable.Range(0, SelectedNetwork.Width).ToArray();
            Random.Shared.Shuffle(values);
            ReplaceTrace(NetworkTrace.CreateForPlayback(SelectedNetwork, values));
        }, () => !playbackSuspended);
        timer.Tick += OnTick;
    }

    public RelayCommand ReturnHomeCommand { get; }
    public RelayCommand PlayPauseCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand ShuffleCommand { get; }
    public RelayCommand ReverseCommand { get; }
    public string[] StepModes { get; } = ["Comparators", "Stages"];

    public NetworkDefinition SelectedNetwork
    {
        get => selectedNetwork;
        set
        {
            if (value is null || ReferenceEquals(value, selectedNetwork)) return;
            var nextTrace = MakeReversedTrace(value);
            SetProperty(ref selectedNetwork, value);
            ReplaceTrace(nextTrace);
            OnPropertyChanged(nameof(NetworkSummary));
        }
    }

    public bool UseZeroOneRule
    {
        get => useZeroOneRule;
        set
        {
            if (!SetProperty(ref useZeroOneRule, value)) return;
            ReplaceTrace(MakeReversedTrace(SelectedNetwork));
        }
    }

    public NetworkTrace Trace => trace;
    public NetworkTrace DisplayTrace => trace;
    public bool IsDiagramAvailable => true;
    public string NetworkSummary => $"{SelectedNetwork.Width} channels  ·  CE: {SelectedNetwork.ComparatorCount}  ·  Stages: {SelectedNetwork.StageCount}";
    public double Position => position;
    public int StepCount => Trace.StepCount(ByStage);
    public bool ByStage => StepMode == 1;
    public bool IsPlaying => isPlaying;
    public string PlayPauseLabel => IsPlaying ? "Pause" : "Play";
    public string ProgressText => $"{(int)Math.Floor(Position)} / {StepCount} {(ByStage ? "stages" : "comparators")}{(Trace.IsPreview ? " (shown prefix)" : "")}";
    public string CurrentValuesText => $"{(Position == 0 ? "Input" : Position >= StepCount ? Trace.IsPreview ? "At cutoff" : "Output" : "Current")}:  {string.Join("  ", Trace.Values[Trace.FrameIndex((int)Math.Floor(Position), ByStage)])}";

    public double SeekPosition
    {
        get => Position;
        set { Pause(); SetPosition(value); }
    }

    public int StepMode
    {
        get => stepMode;
        set
        {
            if (value is < 0 or > 1 || value == stepMode) return;
            Pause();
            SetProperty(ref stepMode, value);
            OnPropertyChanged(nameof(ByStage));
            SetPosition(0);
            NotifyPlayback();
        }
    }

    public double Speed
    {
        get => speed;
        set => SetProperty(ref speed, double.IsFinite(value) ? Math.Clamp(value, 0.25, 3) : 1);
    }

    public bool FollowPlayback
    {
        get => followPlayback;
        set => SetProperty(ref followPlayback, value);
    }

    public double Zoom
    {
        get => zoom;
        set => SetProperty(ref zoom, double.IsFinite(value) ? Math.Clamp(value, 0.4, 1.5) : 1);
    }

    private NetworkTrace MakeReversedTrace(NetworkDefinition network) =>
        NetworkTrace.CreateForPlayback(network, Enumerable.Range(0, network.Width)
            .Select(value => UseZeroOneRule ? (value < network.Width / 2 ? 0 : 1) : value)
            .Reverse().ToArray());

    public void ReplaceTrace(NetworkTrace value)
    {
        Pause();
        trace = value;
        OnPropertyChanged(nameof(Trace));
        OnPropertyChanged(nameof(DisplayTrace));
        OnPropertyChanged(nameof(IsDiagramAvailable));
        SetPosition(0);
        NotifyPlayback();
    }

    private void SetPosition(double value)
    {
        value = double.IsFinite(value) ? Math.Clamp(value, 0, StepCount) : 0;
        if (SetProperty(ref position, value, nameof(Position))) NotifyPlayback();
    }

    private void NotifyPlayback()
    {
        OnPropertyChanged(nameof(SeekPosition));
        OnPropertyChanged(nameof(StepCount));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(CurrentValuesText));
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        PlayPauseCommand.RaiseCanExecuteChanged();
    }

    private void TogglePlayback()
    {
        if (disposed || playbackSuspended || StepCount == 0) return;
        if (IsPlaying) { Pause(); return; }
        if (Position >= StepCount) SetPosition(0);
        isPlaying = true;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
        clock.Restart();
        timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var elapsed = clock.Elapsed;
        clock.Restart();
        Advance(elapsed);
    }

    // Kept separate from the timer so timing, end-of-play and pause behavior can be tested deterministically.
    internal void Advance(TimeSpan elapsed)
    {
        if (!IsPlaying || disposed || elapsed < TimeSpan.Zero) return;
        SetPosition(Position + elapsed.TotalSeconds * Speed / 0.65);
        if (Position >= StepCount) Pause();
    }

    protected void SetPlaybackSuspended(bool value)
    {
        playbackSuspended = value;
        if (value) Pause();
        PlayPauseCommand.RaiseCanExecuteChanged();
        PreviousCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
        ResetCommand.RaiseCanExecuteChanged();
        ReverseCommand.RaiseCanExecuteChanged();
        ShuffleCommand.RaiseCanExecuteChanged();
    }

    public void Pause()
    {
        timer.Stop();
        clock.Stop();
        if (!isPlaying) return;
        isPlaying = false;
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayPauseLabel));
    }

    public virtual void Dispose()
    {
        if (disposed) return;
        Pause();
        timer.Tick -= OnTick;
        disposed = true;
    }
}
