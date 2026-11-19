using Sortopia.Sorting.Networks;
using Sortopia.WPF.Models;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class NetworkCheckTests
{
    [Theory]
    [InlineData(3, null)]
    [InlineData(10, null)]
    [InlineData(8, 1)]
    [InlineData(8, 63)]
    [InlineData(8, 64)]
    [InlineData(8, 65)]
    [InlineData(8, 511)]
    [InlineData(8, 512)]
    [InlineData(8, 513)]
    [InlineData(64, 1025)]
    public void ReusedScalarAndSimdInputsMatchPlaybackWithOptionalCounterexamples(int width, int? samples)
    {
        var token = TestContext.Current.CancellationToken;
        sorter[] networks =
        [
            sorter.create(Guid.NewGuid(), width, []),
            sorter.create(Guid.NewGuid(), width, [ce.create(0, 1), ce.create(1, 2), ce.create(0, 1)]),
            sorter.create(Guid.NewGuid(), width, [ce.create(0, width - 1), ce.create(width - 2, width - 1)])
        ];
        foreach (var simd in new[] { false, true })
        {
            var check = new NetworkCheck(width, samples, simd, token);
            foreach (var network in networks)
            {
                var definition = NetworkDefinition.FromSorter("oracle", network);
                var complete = definition.ToSorter();
                var expected = PlaybackOracle(definition, samples);
                var progress = new List<int>();
                var counts = check.Evaluate(complete, token, includeCounterexample: false, new InlineProgress(progress.Add));
                Assert.Equal(expected.Tested, check.Count);
                AssertCounts(expected, counts);
                Assert.Null(counts.Counterexample);
                Assert.DoesNotContain("first failing input", counts.Description);
                Assert.Equal(check.Count, progress[^1]);
                Assert.All(progress, count => Assert.InRange(count, 1, check.Count));
                Assert.Equal(progress.Order(), progress);

                var detailed = check.Evaluate(complete, token);
                AssertCounts(expected, detailed);
                Assert.Equal(expected.Counterexample, detailed.Counterexample);
                if (detailed.Counterexample is { } example)
                {
                    var output = NetworkTrace.Create(definition, example).Values[^1];
                    Assert.False(output.SequenceEqual(output.Order()));
                    Array.Fill(example, -1); // Returned examples must not expose the cached input storage.
                }
                Assert.Equal(expected.Counterexample, check.Evaluate(complete, token).Counterexample);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SuccessfulChecksNeverReturnACounterexample(bool simd)
    {
        var token = TestContext.Current.CancellationToken;
        var network = sorter.create(Guid.NewGuid(), 3, [ce.create(0, 1), ce.create(1, 2), ce.create(0, 1)]);
        var check = new NetworkCheck(3, null, simd, token);
        foreach (var include in new[] { false, true })
        {
            var result = check.Evaluate(network, token, includeCounterexample: include);
            Assert.Equal(8, result.Tested);
            Assert.Equal(0, result.Failed);
            Assert.Null(result.Counterexample);
            Assert.Contains("Verified", result.Description);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellingDuringScalarEvaluationLeavesInputsReusable(bool includeCounterexample)
    {
        var token = TestContext.Current.CancellationToken;
        var network = sorter.create(Guid.NewGuid(), 8, [ce.create(0, 1)]);
        var check = new NetworkCheck(8, 2049, useSimd: false, token);
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress(done =>
        {
            Assert.Equal(1024, done);
            cancellation.Cancel();
        });
        Assert.Throws<OperationCanceledException>(() => check.Evaluate(network, cancellation.Token, includeCounterexample, progress));
        var replay = check.Evaluate(network, token, includeCounterexample);
        var expected = PlaybackOracle(NetworkDefinition.FromSorter("replay", network), 2049);
        AssertCounts(expected, replay);
        Assert.Equal(includeCounterexample ? expected.Counterexample : null, replay.Counterexample);
    }

    private static void AssertCounts(NetworkCheckResult expected, NetworkCheckResult actual)
    {
        Assert.Equal(expected.Tested, actual.Tested);
        Assert.Equal(expected.Exhaustive, actual.Exhaustive);
        Assert.Equal(expected.Failed, actual.Failed);
        Assert.Equal(expected.CeUses, actual.CeUses);
    }

    // Playback uses the sortable-array implementation, independently of the packed evaluator.
    private static NetworkCheckResult PlaybackOracle(NetworkDefinition network, int? samples)
    {
        var count = samples ?? (1 << network.Width);
        var random = samples is null ? null : new Random(NetworkCheck.SampleSeed);
        var uses = new int[network.ComparatorCount];
        var failed = 0;
        int[]? first = null;
        for (var index = 0; index < count; index++)
        {
            var input = Enumerable.Range(0, network.Width)
                .Select(wire => random is null ? (index >> wire) & 1 : random.Next(2)).ToArray();
            var trace = NetworkTrace.Create(network, input);
            for (var ceIndex = 0; ceIndex < uses.Length; ceIndex++) uses[ceIndex] += trace.SwapCounts[ceIndex];
            var output = trace.Values[^1];
            if (output.SequenceEqual(output.Order())) continue;
            failed++;
            first ??= input;
        }
        return new(count, samples is null, first, failed, uses);
    }

    private sealed class InlineProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }
}
