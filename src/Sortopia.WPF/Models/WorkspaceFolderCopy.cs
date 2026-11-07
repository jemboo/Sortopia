using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sortopia.WPF.Models;

/// <summary>A read-only source snapshot, copied only when its workspace transaction is applied.</summary>
public sealed class WorkspaceFolderCopy
{
    internal sealed record Entry(string RelativePath, long Length, string Hash, DateTime LastWriteTimeUtc);
    private sealed record Snapshot(string[] Directories, Entry[] Files);
    private readonly Snapshot snapshot;
    private WorkspaceFolderCopy(string source, string destination, Snapshot snapshot)
    { SourceFolder = source; DestinationFolder = destination; this.snapshot = snapshot; }
    public string SourceFolder { get; }
    public string DestinationFolder { get; }
    internal IReadOnlyList<string> Directories => snapshot.Directories;
    internal IReadOnlyList<Entry> Files => snapshot.Files;

    public static WorkspaceFolderCopy? Prepare(string workspaceRoot, string sourceFolder, string destinationFolder, CancellationToken token)
    {
        var source = Normalize(sourceFolder);
        var destination = Normalize(destinationFolder);
        CheckAncestors(source);
        CheckDestination(workspaceRoot, source, destination);
        if (Same(source, destination)) return null; // Already in the correct container: attach in place.
        return new(source, destination, Capture(source, token));
    }

    public WorkspaceFolderCopy WithDestination(string workspaceRoot, string destinationFolder)
    {
        var destination = Normalize(destinationFolder);
        if (Same(SourceFolder, destination)) throw new IOException("A pending copy cannot replace its source folder. Choose another folder name.");
        CheckDestination(workspaceRoot, SourceFolder, destination);
        return new(SourceFolder, destination, snapshot);
    }

    public void ValidateSource(CancellationToken token)
    {
        if (Fingerprint(Capture(SourceFolder, token)) != Fingerprint(snapshot))
            throw new IOException($"The import source changed. Remove it from the draft and add it again: {SourceFolder}");
    }

    internal void ValidateDestination(string workspaceRoot) => CheckDestination(workspaceRoot, SourceFolder, DestinationFolder);

    internal string Stage(string workspaceRoot, CancellationToken token)
    {
        ValidateDestination(workspaceRoot);
        CheckAncestors(SourceFolder);
        var staging = Path.Combine(Normalize(workspaceRoot), ".sortopia-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var directory in snapshot.Directories)
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.Combine(staging, directory));
            }
            foreach (var file in snapshot.Files)
            {
                token.ThrowIfCancellationRequested();
                var source = Path.Combine(SourceFolder, file.RelativePath);
                CheckAncestors(source);
                var target = Path.Combine(staging, file.RelativePath);
                using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var hash = Hash(input, token, output);
                    if (input.Length != file.Length || hash != file.Hash)
                        throw new IOException($"The import source changed while copying: {source}");
                }
                File.SetLastWriteTimeUtc(target, file.LastWriteTimeUtc);
            }
            ValidateSource(token); // Also detects added/removed files and empty directories.
            return staging;
        }
        catch { DeleteStaging(workspaceRoot, staging); throw; }
    }

    internal static void DeleteStaging(string workspaceRoot, string staging)
    {
        var root = Normalize(workspaceRoot);
        var path = Normalize(staging);
        if (!Same(Path.GetDirectoryName(path)!, root) || !Path.GetFileName(path).StartsWith(".sortopia-import-", StringComparison.Ordinal))
            throw new IOException("Invalid import staging path.");
        if (Directory.Exists(path))
        {
            // Delete only regular files/directories in this transaction's private staging folder.
            CheckAncestors(path);
            DeleteRegularTree(path);
        }
    }
    private static void DeleteRegularTree(string path)
    {
        CheckLink(path);
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            CheckLink(entry);
            if (Directory.Exists(entry)) DeleteRegularTree(entry);
            else File.Delete(entry);
        }
        Directory.Delete(path);
    }

    internal static bool Matches(string path, Entry expected)
    {
        CheckAncestors(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return input.Length == expected.Length && Hash(input, CancellationToken.None) == expected.Hash;
    }

    private static Snapshot Capture(string source, CancellationToken token)
    {
        CheckAncestors(source);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Import source is unavailable: {source}");
        var directories = new List<string> { "" };
        var files = new List<Entry>();
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            CheckLink(directory);
            foreach (var path in Directory.GetFileSystemEntries(directory).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                CheckLink(path);
                var relative = Path.GetRelativePath(source, path);
                if (Directory.Exists(path)) { directories.Add(relative); pending.Push(path); }
                else
                {
                    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    files.Add(new(relative, input.Length, Hash(input, token), File.GetLastWriteTimeUtc(path)));
                }
            }
        }
        return new(directories.OrderBy(path => path.Length).ThenBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }
    private static string Fingerprint(Snapshot value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static string Hash(Stream input, CancellationToken token, Stream? output = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        int count;
        while ((count = input.Read(buffer)) > 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
            output?.Write(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void CheckDestination(string workspaceRoot, string source, string destination)
    {
        var root = Normalize(workspaceRoot);
        if (!Inside(root, destination) || Same(root, destination)) throw new IOException("Import destination must be inside the selected workspace.");
        CheckAncestors(destination);
        if (Same(source, destination)) return;
        if (Inside(source, destination) || Inside(destination, source)) throw new IOException("An import source and destination cannot contain one another.");
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"The import destination already exists. Choose a different folder name: {destination}");
    }
    internal static bool Inside(string parent, string path) => Same(parent, path)
        || path.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static void CheckAncestors(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current)) CheckLink(current);
    }
    private static void CheckLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Linked files or folders cannot be copied into a workspace: {path}");
    }
}
