namespace Sortopia.WPF.ViewModels.Pages.Sgd;

internal enum SgdActivity { Idle, Generate, Sample, Evolve }

/// <summary>Owns the one active run for an SGD workflow, including replacement and cancellation.</summary>
internal sealed class SgdOperation
{
    private CancellationTokenSource? current;
    public SgdActivity Activity { get; private set; }
    public bool IsIdle => Activity == SgdActivity.Idle;
    public bool CanStop => current is { IsCancellationRequested: false };
    public event Action? Changed;
    public bool IsCurrent(CancellationToken token) => current?.Token == token;

    public async Task RunAsync(SgdActivity activity, Func<CancellationToken, Task> run)
    {
        current?.Cancel();
        using var cancellation = new CancellationTokenSource();
        current = cancellation;
        Activity = activity;
        Changed?.Invoke();
        try { await run(cancellation.Token); }
        finally
        {
            // A replaced run must not clear the new run's busy state.
            if (ReferenceEquals(current, cancellation))
            {
                current = null;
                Activity = SgdActivity.Idle;
                Changed?.Invoke();
            }
        }
    }

    public void Stop()
    {
        if (!CanStop) return;
        current!.Cancel();
        Changed?.Invoke();
    }
}
