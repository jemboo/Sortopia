namespace Sortopia.WPF.Models;

public sealed record NetworkCheckResult(int Tested, bool Exhaustive, int[]? Counterexample,
    int Failed, IReadOnlyList<int> CeUses)
{
    public string Description => Failed > 0
        ? $"Failed: {Failed:N0} of {Tested:N0} {(Exhaustive ? "all" : "sampled")} binary inputs were unsorted. The first failing input is loaded below."
        : Exhaustive ? $"Verified: sorted all {Tested:N0} binary inputs (0–1 rule)."
        : $"Passed {Tested:N0} sampled binary inputs. This is not a proof that the network sorts every input.";
    public string UsageSummary => $"CE usage: {CeUses.Count(n => n > 0):N0} of {CeUses.Count:N0} CEs swapped values across {Tested:N0} inputs. Hover over a CE for its count and percentage.";
}

/// <summary>Checks the entire requested input set and records swaps per comparator.</summary>
public static class NetworkCheck
{
    public const int MaxExhaustiveWidth = 18;
    public const int MaxSamples = 1_000_000;

    public static NetworkCheckResult Run(NetworkDefinition network, CancellationToken cancellationToken) =>
        Run(network, network.Width <= MaxExhaustiveWidth ? null : 4096, cancellationToken);

    // null means all combinations; a count means random draws with replacement.
    public static NetworkCheckResult Run(NetworkDefinition network, int? samples, CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        if (samples is null && network.Width > MaxExhaustiveWidth)
            throw new ArgumentOutOfRangeException(nameof(samples), "All inputs is available only through 18 channels.");
        if (samples is < 1 or > MaxSamples) throw new ArgumentOutOfRangeException(nameof(samples));
        var exhaustive = samples is null;
        var count = samples ?? (1 << network.Width);
        var random = new Random(8675309);
        var input = new int[network.Width];
        var working = new int[network.Width];
        var uses = new int[network.ComparatorCount];
        int[]? counterexample = null;
        var failed = 0;
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var wire = 0; wire < input.Length; wire++)
                input[wire] = exhaustive ? (index >> wire) & 1 : random.Next(2);
            input.CopyTo(working, 0);
            for (var ceIndex = 0; ceIndex < network.Comparators.Length; ceIndex++)
            {
                if ((ceIndex & 255) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // A sorted binary input cannot swap again; the remaining usage counts stay zero.
                    var sorted = true;
                    for (var wire = 1; wire < working.Length; wire++)
                        if (working[wire - 1] > working[wire]) { sorted = false; break; }
                    if (sorted) break;
                }
                var comparator = network.Comparators[ceIndex];
                var low = comparator.LowChannel;
                var high = comparator.HighChannel;
                if (working[low] > working[high])
                {
                    (working[low], working[high]) = (working[high], working[low]);
                    uses[ceIndex]++;
                }
            }
            for (var wire = 1; wire < working.Length; wire++)
                if (working[wire - 1] > working[wire])
                {
                    failed++;
                    counterexample ??= (int[])input.Clone();
                    break;
                }
            if ((index + 1) % 1024 == 0 || index + 1 == count) progress?.Report(index + 1);
        }
        return new(count, exhaustive, counterexample, failed, Array.AsReadOnly(uses));
    }
}
