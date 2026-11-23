namespace Sortopia.Workspaces.Persistence

open System
open System.IO
open System.Threading
open WorkspaceFileSystem

module internal WorkspaceExport =
    let copy rootFolder destinationFolder (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let root = normalizeRoot rootFolder
        let destination = normalizeRoot destinationFolder
        if WorkspaceFolderCopy.Inside(root, destination) || WorkspaceFolderCopy.Inside(destination, root) then
            raise (IOException("Choose an export folder outside the workspace. The source and destination cannot contain one another."))
        if not (Directory.Exists destination) then
            raise (DirectoryNotFoundException($"Choose an existing empty export folder: {destination}"))
        if Directory.EnumerateFileSystemEntries(destination) |> Seq.isEmpty |> not then
            raise (IOException($"The export folder must be empty. Choose another folder: {destination}"))
        // Load shallow documents so saved drafts can be exported too.
        WorkspaceDisk.loadWorkspace root |> ignore
        let snapshot = WorkspaceFolderCopy.PrepareExport(root, destination, token)
        let staging = snapshot.StageExport(token)
        let published = ResizeArray<string * string>()
        try
            // Validate the copied hierarchy before publishing any of it.
            WorkspaceDisk.loadWorkspaceWithFolderName staging (WorkspaceDisk.folderName destination) |> ignore
            token.ThrowIfCancellationRequested()
            if Directory.EnumerateFileSystemEntries(destination) |> Seq.exists (fun path -> not (pathComparer.Equals(path, staging))) then
                raise (IOException($"The export folder changed while copying. Choose an empty folder: {destination}"))
            // Commit without cancellation; moves never overwrite existing files.
            for source in Directory.GetFileSystemEntries staging do
                let target = Path.Combine(destination, Path.GetFileName source)
                movePath source target
                published.Add(source, target)
            Directory.Delete staging
        with failure ->
            try
                for source, target in published |> Seq.rev do movePath target source
                WorkspaceFolderCopy.DeleteStaging(destination, staging)
            with cleanup ->
                raise (IOException($"Workspace export failed: {failure.Message} Cleanup also failed: {cleanup.Message} Incomplete export files remain in {destination}; staging folder: {staging}", failure))
            reraise ()
