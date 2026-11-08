# Workspaces

Open **Workspace** from the home page and choose a root folder. The page reports whether a saved workspace file exists there. If it does not, enter a **Workspace name** and choose **Save workspace**. This writes `<workspace-id>.msgpack` (and any entities already added to the draft), then updates the indicator to show that the workspace is saved. Selecting a folder only remembers its location; it does not save a workspace. Selecting a folder with an existing workspace automatically loads its saved definition. Change the workspace name and choose **Save workspace** again to persist edits; the page indicates unsaved changes until saving succeeds. Saving an edit refuses to replace a workspace changed externally since loading. An unreadable workspace is reported and cannot be overwritten with Save.

**Discard changes**, next to Save, restores the snapshot from the last successful save or load. It clears rejected edits, configuration edits, additions, removals, pending moves, and pending imports without changing files. For a new workspace, it restores the empty draft with the root directory's name. Discard is enabled only when there are unsaved changes or rejected edits; both actions are disabled during an operation. **Cancel operation** appears only while work is in progress.

Before changing the root folder, returning Home, or closing the window, unsaved edits trigger a **Save / Discard / Cancel** prompt. Save continues only after a successful write; Discard abandons the draft; Cancel keeps editing. Selecting the same root or canceling its folder picker keeps the current draft without prompting. Simply browsing to an empty root does not count as an edit.

Select a node and use **New module**, **New project**, or **New run**. The available commands follow the hierarchy: workspaces contain modules; modules contain modules and projects; projects contain projects and runs. Edit the selected entity's name on the right. The read-only **Full path** field shows its absolute entity path as selectable text that can be copied. It follows folder edits and reparenting in the current draft; pending changes take effect on disk when saved. **Open folder**, beside the path, opens its folder in File Explorer and is enabled only when the folder exists. Workspaces, modules, and projects have separate **Folder name** values. Runs have no folder name; each is a file named by its ID in its project's `runs` directory. A new module or project starts with its folder name following its name. This continues through failed saves, and stops after the first successful save or an accepted manual folder-name edit. Names may be descriptive (for example, `Search: prefixes`), while folder names are portable directory names (for example, `prefix-search`). The workspace name and root folder are shown at the top; selecting Workspace hides the details panel. **Remove** removes the selected entity from the draft; existing folders and files are kept. **Save workspace** saves the current hierarchy and lists any retained folders in an expandable notice below the editor.

Run configuration is edited as JSON in the page, then validated and saved as MessagePack. `runKind` is `root` or `branch`. Supply the execution function and results database; branches also require their setup function/database and setup parameter/query settings. Parameter spans have `keyName` and an array of string `values`. Output type codes are `3` (`SorterModelSet`) and `4` (`SorterSetEval`). All fields from `Sortopia.Runs.run` are preserved, including parameter spans and output query keys. Project and run names and the run ID come from the tree editor, independently of the execution settings. The Workspace page defines runs; it does not execute them.

## Reorganizing the tree

Select a module, project, or run and choose **Cut**, then select its new parent and choose **Paste**. The same actions are available from the tree's right-click menu and with **Ctrl+X / Ctrl+V** while the tree has focus. Text fields keep their normal text-editing shortcuts. **Cancel cut** or **Escape** in the tree clears the cut selection. Cut only marks the item; it stays in place and does not create unsaved changes until pasted. The Workspace root cannot be cut.

Paste uses the selected entity as the destination parent. Workspaces accept modules; modules accept modules/projects; projects accept projects/runs; runs accept nothing. Pasting into the same parent moves the item to the end of its typed collection. Cycles, duplicate sibling names/folders, and occupied destination folders are rejected. A rejected paste keeps the cut item available so you can choose another destination.

Pasting changes the draft immediately. **Save workspace** then moves existing run files or folders with all contents, including output files and empty directories, while retaining their GUID filenames. Run metadata is updated for its new containing project. Pending moves are labeled in the tree. **Discard changes** cancels the draft moves without touching disk. Leaving the page resolves unsaved changes as usual and clears the cut selection. The clipboard is local to this workspace.

New items and pending imports can also be reorganized. A child moved out of an unsaved import is copied directly to its new destination, without being duplicated in the imported parent; external source folders stay unchanged. Saving stages nested moves and imports, rejects new disk conflicts, and restores folders and definitions if saving fails. Removing an item from the definition remains a separate operation that keeps its existing disk contents.

## Adding existing folders

Select the intended parent and choose **Add module…** or **Add project…**. These commands open a folder picker in that parent's modules or projects container. If the container or parent does not exist, the picker opens at the nearest existing ancestor without creating folders. Existing entities can also be added beneath an unsaved parent.

| Selected parent | New entities | Existing folders that can be added |
| --- | --- | --- |
| Workspace | Module | Module |
| Module | Module, project | Module, project |
| Project | Project, run | Project |
| Run | None | None |

Choose a module or project folder anywhere on disk. Saving copies the selected folder and all contents into the selected parent's matching container, leaving the source unchanged. A folder already in the exact destination is attached in place. Existing destination files or folders are never merged or overwritten by a copy. The folder's actual hierarchy is read recursively, preserving saved display names and run settings. Modules/projects without metadata use their folder names initially; runs are read from their saved definition files. Wrong entity types, forbidden child containers, invalid metadata, and name/folder collisions are rejected.

Adding changes only the draft. The inspector identifies pending copies. **Save workspace** copies every file and subfolder (including output data and empty folders) and writes the updated definitions. Removing or discarding an unsaved import performs no copy. Imports follow any destination folder-name changes made in the draft. Imported display-name changes keep their folder names fixed.

Copies are staged before publishing and rolled back if the transaction fails or is canceled. Changed sources require removing the pending import and adding it again. Overlapping source/destination trees and linked files or folders are rejected to avoid recursive or ambiguous copies.

## Saving and folders

The edited workspace is authoritative when saving. Save creates the required folders and writes the entity definitions in one transaction. Folders absent from the definition remain untouched, including their results and other user files. Changes made directly on disk are not automatically adopted into the workspace definition. Opening a workspace loads its root snapshot. Use **Add module…** or **Add project…** to include an existing disk hierarchy explicitly; importing reads the selected entity's actual children, ignoring stale child lists in parent snapshots.

Changing the name of a saved workspace, module, project, or run updates its metadata while keeping its ID and location. Changing a module/project **Folder name** creates the new folder and keeps/reports the old folder; existing output files are not moved. To copy an existing module or project with its output data, use the import actions.

## Folder layout

```text
rootFolder/
  <workspace-id>.msgpack
  modules/
    Sorting/
      <module-id>.msgpack
      modules/
        Nested module/
          <module-id>.msgpack
      projects/
        Search/
          <project-id>.msgpack
          projects/
            Nested project/
              <project-id>.msgpack
          runs/
            <run-id>.msgpack
```

Module/project metadata is generated when an imported folder is saved. Runs are stored directly in `runs/<run-id>.msgpack`. Reading an import retains its IDs, display names, and execution settings, and updates project names to the containing project. Saving a copied import assigns fresh IDs. Module/project folder names follow their directories. New folders without metadata use their directory names as initial display names. The workspace's display name is retained from its root definition; its folder name follows the selected directory. A copied or moved workspace uses the newly selected root path.

The tree labels saved components whose folder or definition is missing. Amber labels and a summary above the editor distinguish missing items from new items marked **Not saved** and imports marked **Pending copy**. Status refreshes every two seconds while the page is open, and after loading, saving, or discarding. Missing child definitions/folders can be restored with **Save workspace** even without editing the hierarchy. If the loaded workspace definition itself disappears, the tree remains available so it can be saved again. A missing root directory must be restored or another root selected first.

## Types and persistence

`Sortopia.Workspaces` defines `workspace`, `module`, and `project`. The `module` name is escaped in F# source because it is a keyword. The library reuses the measured name types and `run` from `Sortopia.Runs`. Each entity stores a measured display name and a `Guid` ID tagged with `workspaceId`, `moduleId`, `projectId`, or `runId`. Workspace/module/project types also store a plain `folderName` directory segment; runs do not. The absolute root path is kept separately in page settings and private save transactions, never in the workspace definition. Constructors validate nonblank display names, portable folder names, case-insensitive sibling name and folder uniqueness, and each run's containing project. Every `modules`, `projects`, and `runs` collection requires distinct display names. Modules and projects also require distinct folder names, and runs require distinct IDs because their files share a directory. For example, `Trial` and `trial` conflict in one collection. The editor rejects conflicting name/folder edits immediately and displays an error beside the field. The attempted text remains editable, while the tree retains its last accepted value; Save is disabled until the error is corrected. Names and folders in the loaded workspace snapshot remain reserved until removals or renames are successfully saved. An entity may retain or change the letter case of its own saved identity. Separate collections may reuse names and folder names because their typed containers or parents differ. Array properties return copies.

`Sortopia.Workspaces.Serialization` writes version-3 MessagePack DTOs with persisted IDs for workspace/module/project/run. Versions 1 and 2 remain readable. Definitions without IDs receive deterministic IDs on read, which are persisted on the next save. Legacy run folder names are ignored by the domain; saving locates the old run definitions by their migrated IDs and moves their metadata into the flat runs directory, retaining old folders and output files. It provides `WorkspaceCodec` entry points for each entity type. Each entity file contains a complete snapshot of that entity and its descendants. The workspace root snapshot is written last. DTO conversion validates the domain invariants and rejects unsupported versions.

Definition filenames are `<id>.msgpack`, using the entity's persisted ID. IDs stay stable through edits, moves, and restoration of missing definitions. Copying through import assigns fresh IDs to copied entities and descendants; both filenames and parent snapshots use the new IDs. Attaching an existing folder in place preserves its IDs. Older unsuffixed and kind-prefixed GUID filenames remain readable and migrate on save. Migration participates in rollback; a failed save restores the original filenames and bytes. Multiple matching workspace/module/project definitions in one entity folder are reported as ambiguous rather than choosing one.

`WorkspaceFileStore` exposes load, create/save, and entity-import operations. Save preparation and commit are private to the store; its result contains the saved definition, copy count, and retained folders. It stages writes, checks for changed source files, and restores earlier writes if a later write fails. Restoration failures are reported explicitly. Folder names cannot escape the selected root, and linked folders/files are rejected in the managed hierarchy. Limits are 64 nesting levels, 50,000 entities, and 16 MB per definition file. Unrelated data files are not serialized or moved; explicit imports copy their contents. Sources are never modified by an external copy.
