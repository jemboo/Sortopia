using System.IO;
using System.Security.Cryptography;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using WorkspaceModule = Sortopia.Workspaces.@module;

namespace Sortopia.WPF.Models;

public sealed record WorkspaceSaveResult(workspace Definition, int CopiedFolders, IReadOnlyList<string> RetainedFolders, int MovedFolders = 0);

/// <summary>Transactional workspace saves and explicit entity imports. Unlisted folders and user data are kept.</summary>
public static class WorkspaceFileStore
{
    private const int MaxDefinitionBytes = 16 * 1024 * 1024;
    private const int MaxEntities = 50_000;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;
    private sealed record DiskState(string Fingerprint, IReadOnlyList<string> Entities);
    private sealed record WorkspaceFileUpdate(string Path, byte[] Content, string? OriginalPath, byte[]? Original);
    private sealed record SavePlan(string RootFolder, workspace Definition, string Fingerprint,
        IReadOnlyList<WorkspaceFileUpdate> Writes, IReadOnlyList<string> RetainedFolders, IReadOnlyList<WorkspaceFolderCopy> Copies,
        IReadOnlyList<WorkspaceFolderMove> Moves);
    private sealed record Transfer(string Source, string Destination, WorkspaceFolderCopy? Copy);
    private sealed class StagedMove(WorkspaceFolderMove move, string staging)
    {
        public WorkspaceFolderMove Move { get; } = move;
        public string Staging { get; } = staging;
        public bool Published { get; set; }
    }

    public static workspace LoadDefinition(string rootFolder)
    {
        var root = NormalizeRoot(rootFolder);
        CheckDirectory(root);
        var saved = WorkspaceCodec.decodeWorkspace(ReadDefinition(WorkspaceDefinitionFiles.Require(root, "workspace")));
        // A copied or moved workspace always uses the folder the user actually selected.
        return Rebase(saved, root);
    }

    public static WorkspaceSaveResult CreateWorkspace(string rootFolder, workspace definition, CancellationToken token, IReadOnlyList<WorkspaceFolderCopy>? copies = null,
        IReadOnlyList<WorkspaceFolderMove>? moves = null)
    {
        var root = NormalizeRoot(rootFolder);
        void RequireNewWorkspace()
        {
            if (WorkspaceDefinitionFiles.Find(root, "workspace") is not null)
                throw new IOException("A workspace file already exists at this location. Reopen the workspace to load it.");
        }
        RequireNewWorkspace();
        var plan = PrepareSave(rootFolder, definition, token, copies, moves);
        RequireNewWorkspace();
        return Commit(plan, token);
    }

    public static WorkspaceSaveResult SaveWorkspace(string rootFolder, workspace definition, workspace savedDefinition, CancellationToken token, IReadOnlyList<WorkspaceFolderCopy>? copies = null,
        IReadOnlyList<WorkspaceFolderMove>? moves = null)
    {
        var plan = PrepareSave(rootFolder, definition, token, copies, moves);
        var current = WorkspaceDefinitionFiles.Find(rootFolder, "workspace") is null ? null : LoadDefinition(rootFolder);
        if (current is not null && !WorkspaceCodec.encodeWorkspace(current).AsSpan().SequenceEqual(WorkspaceCodec.encodeWorkspace(savedDefinition)))
            throw new IOException("The saved workspace changed since it was loaded. Reopen the workspace before saving again.");
        return Commit(plan, token);
    }

    public static string GetFolderName(string rootFolder)
    {
        var name = Path.GetFileName(NormalizeRoot(rootFolder));
        return string.IsNullOrEmpty(name) ? "Workspace" : name;
    }

    private static workspace Rebase(workspace value, string rootFolder) =>
        workspace.create(value.WorkspaceName, GetFolderName(rootFolder), value.Modules);

    private static SavePlan PrepareSave(string rootFolder, workspace definition, CancellationToken token, IReadOnlyList<WorkspaceFolderCopy>? copies,
        IReadOnlyList<WorkspaceFolderMove>? moves)
    {
        var root = NormalizeRoot(rootFolder);
        var before = Capture(root, token);
        definition = Rebase(definition, root);
        var expected = new HashSet<string>(PathComparer);
        var writes = new List<WorkspaceFileUpdate>();
        var retainedFolders = new List<string>();
        var imports = (copies ?? []).OrderBy(copy => copy.DestinationFolder.Length).ToArray();
        var relocations = (moves ?? []).Where(move => !PathComparer.Equals(move.SourceFolder, move.DestinationFolder)).ToArray();
        var transfers = imports.Select(copy => new Transfer(copy.SourceFolder, copy.DestinationFolder, copy))
            .Concat(relocations.Select(move => new Transfer(move.SourceFolder, move.DestinationFolder, null)))
            .OrderBy(transfer => transfer.Destination.Length).ToArray();
        if (transfers.Select(transfer => transfer.Destination).Distinct(PathComparer).Count() != transfers.Length)
            throw new IOException("Multiple entities target the same destination folder.");
        if (relocations.Select(move => move.SourceFolder).Distinct(PathComparer).Count() != relocations.Length)
            throw new IOException("A folder cannot be moved to more than one destination.");
        foreach (var move in relocations)
        {
            WorkspaceFolderMove.CheckPath(root, move.SourceFolder);
            WorkspaceFolderMove.CheckPath(root, move.DestinationFolder);
            if (!Directory.Exists(move.SourceFolder)) throw new DirectoryNotFoundException($"The folder to move is missing: {move.SourceFolder}");
        }
        foreach (var copy in imports)
        {
            copy.ValidateDestination(root);
            copy.ValidateSource(token);
        }
        foreach (var transfer in transfers)
        {
            var enclosing = transfers.LastOrDefault(other => other != transfer && WorkspaceFolderCopy.Inside(other.Destination, transfer.Destination));
            var existing = enclosing is null ? transfer.Destination
                : Path.Combine(enclosing.Source, Path.GetRelativePath(enclosing.Destination, transfer.Destination));
            var vacated = relocations.Any(move => WorkspaceFolderCopy.Inside(move.SourceFolder, existing))
                || enclosing?.Copy is { } parentCopy && !parentCopy.IncludesSourcePath(existing);
            if (!vacated && (Directory.Exists(existing) || File.Exists(existing)))
            {
                throw new IOException($"The destination already exists. Choose a different parent or folder name: {transfer.Destination}");
            }
        }
        void Add(string folder, string kind, byte[] bytes)
        {
            token.ThrowIfCancellationRequested();
            if (bytes.Length > MaxDefinitionBytes) throw new InvalidDataException($"Definition is too large: {folder}");
            expected.Add(folder);
            if (expected.Count > MaxEntities) throw new InvalidDataException("Workspace contains too many entities.");
            var transfer = transfers.LastOrDefault(item => WorkspaceFolderCopy.Inside(item.Destination, folder));
            var sourceFolder = transfer is null ? folder : Path.Combine(transfer.Source, Path.GetRelativePath(transfer.Destination, folder));
            // A child cut out of a moved parent is no longer part of that parent's physical contents.
            if (transfer is not null && relocations.Any(move => !PathComparer.Equals(move.SourceFolder, transfer.Source)
                && WorkspaceFolderCopy.Inside(transfer.Source, move.SourceFolder)
                && WorkspaceFolderCopy.Inside(move.SourceFolder, sourceFolder))) sourceFolder = folder;
            var sourcePath = transfer?.Copy is { } sourceCopy && !sourceCopy.IncludesSourcePath(sourceFolder) ? null : WorkspaceDefinitionFiles.Find(sourceFolder, kind);
            var original = sourcePath is null ? null : ReadDefinition(sourcePath);
            var originalPath = sourcePath is null ? null : Path.Combine(folder, Path.GetFileName(sourcePath));
            // Keep saved identities stable; copied entities get their own identities.
            var path = transfer?.Copy is null && sourcePath is not null && WorkspaceDefinitionFiles.HasGuid(Path.GetFileName(sourcePath), kind)
                ? originalPath! : Path.Combine(folder, WorkspaceDefinitionFiles.NewName(kind));
            if (PathComparer.Equals(path, originalPath) && original is not null && original.AsSpan().SequenceEqual(bytes)) return;
            writes.Add(new(path, bytes, originalPath, original));
        }
        void AddProject(project value, string folder, int depth)
        {
            CheckDepth(depth);
            foreach (var child in value.Projects) AddProject(child, Child(folder, "projects", child.FolderName), depth + 1);
            foreach (var run in value.Runs) Add(Child(folder, "runs", run.FolderName), "run", WorkspaceCodec.encodeRun(run));
            Add(folder, "project", WorkspaceCodec.encodeProject(value));
        }
        void AddModule(WorkspaceModule value, string folder, int depth)
        {
            CheckDepth(depth);
            foreach (var child in value.Modules) AddModule(child, Child(folder, "modules", child.FolderName), depth + 1);
            foreach (var child in value.Projects) AddProject(child, Child(folder, "projects", child.FolderName), depth + 1);
            Add(folder, "module", WorkspaceCodec.encodeModule(value));
        }
        foreach (var child in definition.Modules) AddModule(child, Child(root, "modules", child.FolderName), 1);
        Add(root, "workspace", WorkspaceCodec.encodeWorkspace(definition)); // Root snapshot commits last.
        if (transfers.Any(transfer => !expected.Contains(transfer.Destination)))
            throw new ArgumentException("Every moved or imported folder must belong to an entity in the workspace definition.");
        foreach (var copy in imports)
        {
            var kind = Path.GetFileName(Path.GetDirectoryName(copy.DestinationFolder)!)[..^1];
            foreach (var sourceEntity in Capture(copy.SourceFolder, token, kind).Entities)
            {
                if (!copy.IncludesSourcePath(sourceEntity)) continue;
                var target = Path.GetFullPath(Path.Combine(copy.DestinationFolder, Relative(copy.SourceFolder, sourceEntity)));
                if (!expected.Contains(target)) retainedFolders.Add(Relative(root, target));
            }
        }
        foreach (var existing in before.Entities)
        {
            var move = relocations.Where(item => WorkspaceFolderCopy.Inside(item.SourceFolder, existing)).MaxBy(item => item.SourceFolder.Length);
            var target = move is null ? existing : Path.GetFullPath(Path.Combine(move.DestinationFolder, Relative(move.SourceFolder, existing)));
            if (!expected.Contains(target)) retainedFolders.Add(Relative(root, target));
        }
        if (before.Fingerprint != Capture(root, token).Fingerprint)
            throw new IOException("Folders changed while preparing the save. Try saving again.");
        return new(root, definition, before.Fingerprint, writes.AsReadOnly(), retainedFolders.AsReadOnly(), imports, relocations);
    }

    private static WorkspaceSaveResult Commit(SavePlan plan, CancellationToken token)
    {
        var root = plan.RootFolder;
        if (Capture(root, token).Fingerprint != plan.Fingerprint)
            throw new IOException("The file system changed while preparing the save. Try saving again.");
        var staged = new List<(WorkspaceFileUpdate Write, string Temporary)>();
        var applied = new List<WorkspaceFileUpdate>();
        var stagedCopies = new List<(WorkspaceFolderCopy Copy, string Staging)>();
        var publishedCopies = new List<WorkspaceFolderCopy>();
        var stagedMoves = new List<StagedMove>();
        var createdFolders = new List<string>();
        var committed = false;
        void EnsureDirectory(string path)
        {
            CheckContained(root, path);
            if (Directory.Exists(path)) { CheckDirectory(path); return; }
            if (!PathComparer.Equals(path, root)) EnsureDirectory(Path.GetDirectoryName(path)!);
            Directory.CreateDirectory(path);
            createdFolders.Add(path);
        }
        try
        {
            if (plan.Copies.Count > 0) EnsureDirectory(root);
            var beforeStaging = plan.Copies.Count > 0 ? Capture(root, token).Fingerprint : null;
            foreach (var copy in plan.Copies)
            {
                token.ThrowIfCancellationRequested();
                stagedCopies.Add((copy, copy.Stage(root, token)));
            }
            // Staging is outside the typed containers and must not change the managed workspace.
            if (plan.Copies.Count > 0 && Capture(root, token).Fingerprint != beforeStaging)
                throw new IOException("The file system changed while staging imports. Try saving again.");
            // Detach nested sources first, so each subtree is moved exactly once.
            foreach (var move in plan.Moves.OrderByDescending(move => move.SourceFolder.Length))
            {
                token.ThrowIfCancellationRequested();
                WorkspaceFolderMove.CheckPath(root, move.SourceFolder);
                var staging = Path.Combine(root, ".sortopia-move-" + Guid.NewGuid().ToString("N"));
                Directory.Move(move.SourceFolder, staging);
                stagedMoves.Add(new(move, staging));
            }
            var publications = stagedCopies.Select(item => (Destination: item.Copy.DestinationFolder, item.Staging, Copy: (WorkspaceFolderCopy?)item.Copy, Move: (StagedMove?)null))
                .Concat(stagedMoves.Select(item => (Destination: item.Move.DestinationFolder, item.Staging, Copy: (WorkspaceFolderCopy?)null, Move: (StagedMove?)item)))
                .OrderBy(item => item.Destination.Length);
            foreach (var publication in publications)
            {
                token.ThrowIfCancellationRequested();
                WorkspaceFolderMove.CheckPath(root, publication.Destination);
                if (Directory.Exists(publication.Destination) || File.Exists(publication.Destination))
                    throw new IOException($"The destination already exists: {publication.Destination}");
                EnsureDirectory(Path.GetDirectoryName(publication.Destination)!);
                Directory.Move(publication.Staging, publication.Destination);
                if (publication.Move is { } moved) moved.Published = true;
                if (publication.Copy is { } copy)
                {
                    publishedCopies.Add(copy);
                    createdFolders.AddRange(copy.Directories.Select(relative => Path.Combine(copy.DestinationFolder, relative)));
                }
            }
            foreach (var write in plan.Writes)
            {
                token.ThrowIfCancellationRequested();
                EnsureDirectory(Path.GetDirectoryName(write.Path)!);
                var temporary = write.Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((write, temporary));
                File.WriteAllBytes(temporary, write.Content);
            }
            foreach (var (write, temporary) in staged)
            {
                token.ThrowIfCancellationRequested();
                var current = write.OriginalPath is not null && File.Exists(write.OriginalPath) ? ReadDefinition(write.OriginalPath) : null;
                if (!Equal(current, write.Original)) throw new IOException($"Definition changed while saving: {write.Path}");
                var samePath = PathComparer.Equals(write.Path, write.OriginalPath);
                File.Move(temporary, write.Path, overwrite: samePath);
                applied.Add(write);
                if (!samePath && write.OriginalPath is not null) File.Delete(write.OriginalPath);
            }
            committed = true;
            return new(plan.Definition, plan.Copies.Count, plan.RetainedFolders, plan.Moves.Count);
        }
        catch (Exception failure)
        {
            var errors = new List<Exception> { failure };
            foreach (var write in applied.AsEnumerable().Reverse())
            {
                try
                {
                    // Do not overwrite an external edit made after our write.
                    if (!File.Exists(write.Path) || !ReadDefinition(write.Path).AsSpan().SequenceEqual(write.Content))
                        throw new IOException($"Could not roll back externally changed definition: {write.Path}");
                    if (PathComparer.Equals(write.Path, write.OriginalPath)) File.WriteAllBytes(write.Path, write.Original!);
                    else
                    {
                        if (write.OriginalPath is not null)
                        {
                            // A failed rename may have left the old file in place (for example, when locked).
                            if (File.Exists(write.OriginalPath))
                            {
                                if (!Equal(ReadDefinition(write.OriginalPath), write.Original))
                                    throw new IOException($"Could not restore externally changed definition: {write.OriginalPath}");
                            }
                            else
                            {
                                using var restored = new FileStream(write.OriginalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                                restored.Write(write.Original!);
                            }
                        }
                        File.Delete(write.Path);
                    }
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            // Remove temporary metadata before restoring paths; never carry it back into a source folder.
            foreach (var (_, temporary) in staged)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            foreach (var folder in createdFolders.AsEnumerable().Reverse())
            {
                try
                {
                    CheckContained(root, folder);
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                    { CheckDirectory(folder); Directory.Delete(folder); }
                }
                catch (Exception rollback) { errors.Add(rollback); }
            }
            var rollbackFolders = stagedMoves.Where(item => item.Published)
                .Select(item => (Destination: item.Move.DestinationFolder, Move: (StagedMove?)item, Copy: (WorkspaceFolderCopy?)null))
                .Concat(publishedCopies.Select(copy => (Destination: copy.DestinationFolder, Move: (StagedMove?)null, Copy: (WorkspaceFolderCopy?)copy)))
                .OrderByDescending(item => item.Destination.Length);
            foreach (var publication in rollbackFolders)
            {
                if (publication.Move is { } item)
                {
                    try
                    {
                        WorkspaceFolderMove.CheckPath(root, item.Move.DestinationFolder);
                        Directory.Move(item.Move.DestinationFolder, item.Staging);
                        item.Published = false;
                    }
                    catch (Exception rollback) { errors.Add(new IOException($"Could not recover moved folder at {item.Move.DestinationFolder}.", rollback)); }
                    continue;
                }
                var copy = publication.Copy!;
                foreach (var file in copy.Files)
                {
                    try
                    {
                        var path = Path.Combine(copy.DestinationFolder, file.RelativePath);
                        CheckContained(root, path);
                        if (!File.Exists(path)) continue;
                        if (!WorkspaceFolderCopy.Matches(path, file)) throw new IOException($"Could not remove an externally changed imported file: {path}");
                        File.Delete(path);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                }
                foreach (var relative in copy.Directories.Reverse())
                {
                    try
                    {
                        var folder = Path.Combine(copy.DestinationFolder, relative);
                        WorkspaceFolderMove.CheckPath(root, folder);
                        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
                    }
                    catch (Exception rollback) { errors.Add(rollback); }
                }
            }
            // Restore parents before their separately moved descendants.
            foreach (var item in stagedMoves.Where(item => !item.Published).OrderBy(item => item.Move.SourceFolder.Length))
            {
                try
                {
                    WorkspaceFolderMove.CheckPath(root, item.Move.SourceFolder);
                    EnsureDirectory(Path.GetDirectoryName(item.Move.SourceFolder)!);
                    Directory.Move(item.Staging, item.Move.SourceFolder);
                }
                catch (Exception rollback) { errors.Add(new IOException($"Could not restore {item.Move.SourceFolder}; its contents are retained at {item.Staging}.", rollback)); }
            }
            if (errors.Count > 1) throw new AggregateException("Save failed; some files could not be restored.", errors);
            throw;
        }
        finally
        {
            foreach (var (_, temporary) in staged) if (File.Exists(temporary)) File.Delete(temporary);
            foreach (var (_, staging) in stagedCopies) WorkspaceFolderCopy.DeleteStaging(root, staging);
            if (!committed)
                foreach (var folder in createdFolders.AsEnumerable().Reverse())
                    if (!plan.Moves.Any(move => WorkspaceFolderCopy.Inside(move.SourceFolder, folder))
                        && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
    }

    public static WorkspaceModule LoadModuleFolder(string folder, CancellationToken token) =>
        ReadEntityFolder(folder, "module", token, root => ReadModuleFolder(root, 0, token));

    public static project LoadProjectFolder(string folder, CancellationToken token) =>
        ReadEntityFolder(folder, "project", token, root => ReadProjectFolder(root, 0, token));

    private static T ReadEntityFolder<T>(string folder, string kind, CancellationToken token, Func<string, T> read)
    {
        var root = NormalizeRoot(folder);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Choose an existing entity folder.");
        var before = Capture(root, token, kind);
        var result = read(root);
        if (before.Fingerprint != Capture(root, token, kind).Fingerprint)
            throw new IOException("The entity folders changed while being read. Add the folder again.");
        return result;
    }

    private static project ReadProjectFolder(string folder, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); CheckDepth(depth);
        ValidateEntityFolder(folder, "project");
        var marker = WorkspaceDefinitionFiles.Find(folder, "project");
        var folderName = Path.GetFileName(folder);
        var projectName = marker is not null ? WorkspaceCodec.decodeProject(ReadDefinition(marker)).ProjectName : folderName;
        var runs = Children(folder, "runs").Select(runFolder =>
        {
            token.ThrowIfCancellationRequested();
            ValidateEntityFolder(runFolder, "run");
            var runFile = WorkspaceDefinitionFiles.Find(runFolder, "run");
            if (runFile is null) throw new InvalidDataException($"Missing run definition in {runFolder}. Create the run in Workspace or restore its definition file.");
            return WorkspaceRun.withLocation(projectName, Path.GetFileName(runFolder), WorkspaceCodec.decodeRun(ReadDefinition(runFile)));
        }).ToArray();
        return project.create(projectName, folderName, runs, Children(folder, "projects").Select(path => ReadProjectFolder(path, depth + 1, token)).ToArray());
    }

    private static WorkspaceModule ReadModuleFolder(string folder, int depth, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); CheckDepth(depth);
        ValidateEntityFolder(folder, "module");
        var marker = WorkspaceDefinitionFiles.Find(folder, "module");
        var folderName = Path.GetFileName(folder);
        var moduleName = marker is not null ? WorkspaceCodec.decodeModule(ReadDefinition(marker)).ModuleName : folderName;
        return WorkspaceModule.create(moduleName, folderName,
            Children(folder, "modules").Select(path => ReadModuleFolder(path, depth + 1, token)).ToArray(),
            Children(folder, "projects").Select(path => ReadProjectFolder(path, depth + 1, token)).ToArray());
    }

    private static void ValidateEntityFolder(string folder, string kind)
    {
        CheckDirectory(folder);
        foreach (var other in new[] { "workspace", "module", "project", "run" }.Where(other => other != kind))
            if (WorkspaceDefinitionFiles.Find(folder, other) is not null)
                throw new InvalidDataException($"Expected a {kind} folder, but found a {other} definition in {folder}.");
        foreach (var container in new[] { "modules", "projects", "runs" }.Except(Containers(kind)))
            if (Directory.Exists(Path.Combine(folder, container)))
                throw new InvalidDataException($"A {kind} cannot contain {container}: {folder}.");
    }

    private static DiskState Capture(string root, CancellationToken token, string rootKind = "workspace")
    {
        token.ThrowIfCancellationRequested();
        var entries = new List<string>();
        var entities = new List<string>();
        void Visit(string folder, string kind, int depth)
        {
            token.ThrowIfCancellationRequested(); CheckDepth(depth);
            CheckDirectory(folder);
            entities.Add(folder);
            if (entities.Count > MaxEntities) throw new InvalidDataException("Workspace contains too many entities.");
            entries.Add("D:" + Relative(root, folder));
            var marker = WorkspaceDefinitionFiles.Find(folder, kind);
            if (marker is not null) entries.Add("F:" + Relative(root, marker) + ":" + Convert.ToHexString(SHA256.HashData(ReadDefinition(marker))));
            foreach (var container in Containers(kind))
            {
                var path = Path.Combine(folder, container);
                if (File.Exists(path)) throw new IOException($"A file occupies an entity container: {path}");
                if (!Directory.Exists(path)) continue;
                CheckDirectory(path);
                entries.Add("C:" + Relative(root, path));
                foreach (var child in Children(folder, container)) Visit(child, container[..^1], depth + 1);
            }
        }
        if (File.Exists(root)) throw new IOException("Workspace root must be a folder.");
        if (Directory.Exists(root)) Visit(root, rootKind, 0);
        return new(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', entries)))), entities.AsReadOnly());
    }

    private static string[] Containers(string kind) => kind switch
    { "workspace" => ["modules"], "module" => ["modules", "projects"], "project" => ["projects", "runs"], _ => [] };
    private static IEnumerable<string> Children(string folder, string container)
    {
        var path = Path.Combine(folder, container);
        if (!Directory.Exists(path)) return [];
        CheckDirectory(path);
        return Directory.GetDirectories(path).Order(StringComparer.OrdinalIgnoreCase);
    }
    private static string Child(string folder, string container, string name) => Path.Combine(folder, container, FolderName.validate(name));
    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute workspace root folder.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
    private static void CheckDirectory(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"Linked folders are not supported in the workspace hierarchy: {path}");
    }
    private static byte[] ReadDefinition(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException($"Linked definition files are not supported: {path}");
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MaxDefinitionBytes) throw new InvalidDataException($"Definition exceeds 16 MB: {path}");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
    private static bool Equal(byte[]? a, byte[]? b) => a is null ? b is null : b is not null && a.AsSpan().SequenceEqual(b);
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path);
    private static void CheckDepth(int depth)
    { if (depth > 64) throw new InvalidDataException("Workspace nesting must not exceed 64 levels."); }
    private static void CheckContained(string root, string path)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!PathComparer.Equals(root, path) && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Workspace path escapes the selected root folder.");
    }
}
