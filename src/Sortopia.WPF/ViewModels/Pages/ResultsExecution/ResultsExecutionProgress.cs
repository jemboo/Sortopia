using Sortopia.FileDb;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

/// <summary>Progress of one selected batch, independent of the grid's filter and selection.</summary>
public sealed class ResultsExecutionProgress(int total) : ViewModelBase
{
    private readonly Dictionary<Guid, workItemExecutionState> states = [];
    public int Total { get; } = total;
    public int Running { get; private set; }
    public int Finished { get; private set; }
    public int Failed { get; private set; }
    public int Cancelled { get; private set; }
    public int NotStarted => Total - Running - Finished;
    public bool HasWork => Total > 0;
    public double Percent => Total == 0 ? 0 : 100.0 * Finished / Total;
    public string Summary => $"Batch: {Finished:N0} of {Total:N0} finished ({Percent:0}%) · {Running:N0} running · {NotStarted:N0} not started"
        + (Failed > 0 ? $" · {Failed:N0} failed" : "")
        + (Cancelled > 0 ? $" · {Cancelled:N0} cancelled" : "");

    internal void Record(Guid id, workItemExecutionState state)
    {
        if (states.TryGetValue(id, out var previous)) Adjust(previous, -1);
        states[id] = state;
        Adjust(state, 1);
        foreach (var property in new[] { nameof(Running), nameof(Finished), nameof(Failed), nameof(Cancelled), nameof(NotStarted), nameof(Percent), nameof(Summary) })
            OnPropertyChanged(property);
    }

    private void Adjust(workItemExecutionState state, int delta)
    {
        if (state == workItemExecutionState.Running) Running += delta;
        else Finished += delta;
        if (state == workItemExecutionState.Failed) Failed += delta;
        if (state == workItemExecutionState.Cancelled) Cancelled += delta;
    }
}
