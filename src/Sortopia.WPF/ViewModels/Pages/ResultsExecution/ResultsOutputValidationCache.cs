using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using Sortopia.FileDb;

namespace Sortopia.WPF.ViewModels.Pages.ResultsExecution;

/// <summary>
/// Keeps validation receipts, never decoded model populations. Successful full
/// checks can be reused; known payload errors remain visible on a quick refresh.
/// Every reuse hashes the complete files again, including their envelopes, so
/// changed bytes invalidate a receipt even when file sizes and dates are unchanged.
/// The cache is local to this page's folder session and writes nothing to disk.
/// </summary>
internal sealed class ResultsOutputValidationCache
{
    private sealed record Receipt(string[] Paths, byte[][] Hashes, workItemOutputStatus Inspection);
    private readonly ConcurrentDictionary<string, Receipt> receipts = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal workItemOutputStatus InspectIntegrity(string path, CancellationToken token)
    {
        path = Path.GetFullPath(path);
        var result = RunParameterExecution.inspectFileIntegrity(path, token);
        if (result.Outputs.State == outputState.Complete && receipts.TryGetValue(path, out var receipt)
            && receipt.Inspection.Outputs.State == outputState.Invalid)
        {
            if (Fingerprints(receipt.Paths, token) is { } hashes && Same(hashes, receipt.Hashes))
                return receipt.Inspection;
            receipts.TryRemove(path, out _);
        }
        return result;
    }

    internal (workItemOutputStatus Inspection, bool Reused) Inspect(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var item = RunParameterFiles.loadFile(path);
        var folder = RunParameterFiles.projectFolder(Path.GetDirectoryName(path)!);
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var plan = RunOutputFiles.plan(item);
        var paths = new[] { path }.Concat(plan.Values.Select(output => store.GetPath(output.Key.Reference)))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToArray();
        var before = Fingerprints(paths, token);
        if (before is not null && receipts.TryGetValue(path, out var receipt)
            && receipt.Inspection.Outputs.State == outputState.Complete && Same(before, receipt.Hashes))
            return (receipt.Inspection, true);

        receipts.TryRemove(path, out _);
        var inspection = RunParameterExecution.inspectFile(path, token);
        token.ThrowIfCancellationRequested();
        // Do not retain missing, partial or concurrently changed outputs.
        // Full checks always retry failures, which can include transient I/O errors.
        // Reading both sides of validation prevents associating a receipt with
        // bytes that changed while the full decoder was checking them.
        if (inspection.Outputs.State is outputState.Complete or outputState.Invalid && before is not null
            && Fingerprints(paths, token) is { } after && Same(before, after))
            receipts[path] = new(paths, after, inspection);
        return (inspection, false);
    }

    private static bool Same(byte[][] left, byte[][] right) => left.Length == right.Length
        && left.Zip(right).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second));

    private static byte[][]? Fingerprints(string[] paths, CancellationToken token)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var hashes = new byte[paths.Length][];
            for (var index = 0; index < paths.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                using var stream = new FileStream(paths[index], FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1, FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                int count;
                while ((count = stream.Read(buffer)) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    hash.AppendData(buffer, 0, count);
                }
                hashes[index] = hash.GetHashAndReset();
            }
            token.ThrowIfCancellationRequested();
            return hashes;
        }
        // Let the ordinary inspector report missing/unreadable files with its
        // existing per-output diagnostics. Such a result cannot reuse a receipt.
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
