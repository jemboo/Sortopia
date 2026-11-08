# Workspaces

Open **Workspace** from the home page and choose a root folder. Enter a workspace name and choose **Save workspace** to create it. Selecting a folder only remembers the location; an existing workspace loads automatically. Names, hierarchy edits, imports, and moves remain in the draft until saved. **Discard changes** restores the last saved state without changing disk contents.

## Root and branch hierarchies

A workspace can contain both root and branch modules. Each module and all its descendants use one type:

| Container | Allowed children |
| --- | --- |
| Workspace | Root modules and branch modules |
| Root module | Root modules and root projects |
| Branch module | Branch modules and branch projects |
| Root project | Root runs |
| Branch project | Branch runs |
| Run | None |

Root runs execute without a setup step. Branch runs have separate setup and execution settings. Projects contain runs only; use nested modules to organize projects.

Select the workspace to choose **New root module** or **New branch module**. Inside a module, creation buttons name the matching type, such as **New branch module** and **New branch project**. Inside a project, **New root run** or **New branch run** creates the correct run type automatically. The tree identifies each item's type with text and color; the hint above it explains the selected item's allowed children. Types are fixed when created.

The inspector edits an entity's display name and, for modules/projects, its folder name. New folder names follow their display names until saved or manually edited. Saved display-name edits keep folder names fixed. Names may be descriptive, such as `Search: prefixes`, while folder names must be portable directory segments, such as `prefix-search`.

Runs have no folder name. A run is stored directly as `runs/<run-id>.msgpack`. The **Full path** field shows the selected folder or run file and supports text selection/copy. **Open folder** opens that folder, or the containing `runs` directory for a run.

## Run configuration

The inspector edits execution settings as JSON. The project supplies the run type, project name, run name, and ID. These are not editable execution settings. Root run editors show execution fields; branch run editors also show setup fields. Pasting a conflicting `runKind` is rejected rather than changing the run's type.

Supply the execution function and results database. Branch runs also require a setup function/database and their setup parameter/query settings. Parameter spans have `keyName` and an array of string `values`. Output type codes are `3` (`SorterModelSet`) and `4` (`SorterSetEval`). Configuration is validated before saving as MessagePack. The Workspace page defines runs; it does not execute them.

## Reorganizing and importing

Use **Cut** and **Paste**, the tree's context menu, or **Ctrl+X / Ctrl+V** while the tree has focus. Text fields retain normal text-editing shortcuts. **Cancel cut** or **Escape** clears the cut selection. Cut alone does not change the draft. Paste is disabled for incompatible types, including a project selected as the parent of another project. Its tooltip explains a type mismatch. Cycles, sibling collisions, and occupied destinations are rejected.

A successful paste changes the draft. Saving moves the run file or folder and its contents, preserves entity IDs, and updates a run's project name. Discard cancels pending moves. Moving an item within its current parent reorders its typed collection.

**Add module…** and **Add project…** import existing saved entities. Modules can be added to workspaces or matching modules; projects can be added only to matching modules. Imports require valid definition files identifying their root/branch type. All descendants must match. Plain folders without definitions cannot establish an entity's type.

The picker starts in the selected parent's matching container or its nearest existing ancestor. An external import is copied only on save, including output files and empty folders. Copying assigns fresh IDs and corresponding filenames to the copied entities and descendants; source files stay unchanged. An entity already at the exact destination is attached in place and keeps its IDs. Existing destinations are not merged or overwritten.

Imports read the actual disk hierarchy, ignoring stale child lists in parent snapshots. Wrong types, malformed settings, forbidden child containers, and name/folder collisions leave the draft unchanged. Changes to an import source require removing the pending import and adding it again. Linked paths and overlapping source/destination trees are rejected. A child moved out of an unsaved import is copied once to its new destination, excluding it from the parent copy.

## Saving and disk status

Save creates the required folders and writes all definitions transactionally, with the workspace snapshot written last. Failed or canceled operations restore previous definitions and locations. External changes detected since loading or during a save prevent overwriting those changes. Recovery failures are reported explicitly.

The workspace definition is authoritative. Removing an entity keeps its existing files on disk and reports retained items. Changing a folder name creates the new folder and retains the old folder and output data; use Cut/Paste to move an existing entity with its contents. Changes made directly on disk are not automatically adopted; import an existing entity to add its actual hierarchy.

The tree distinguishes unsaved items, pending copies/moves, and missing saved definitions. Status refreshes while the page is open and after loading, saving, or discarding. Save can restore missing definitions with the same IDs. A missing workspace root directory must be restored or another location selected first.

Before changing the root folder, returning Home, or closing, unsaved changes prompt for **Save / Discard / Cancel**. Save continues only after success; Discard abandons the draft; Cancel keeps editing. Selecting the same folder or canceling the picker keeps the current draft. Choosing an empty root without editing it does not count as an edit.

## Types and persistence

`Sortopia.Workspaces` defines concrete `rootProject`, `branchProject`, `rootModule`, and `branchModule` types. Their child arrays are strongly typed: for example, `rootProject.Runs` is `rootRun[]`, and `branchModule.Projects` is `branchProject[]`. The `project` and `module` discriminated unions provide common views for storage and the editor. Their boundary factories validate type consistency. `workspace.Modules` accepts either module case. Array properties return copies.

Every entity has a UMX-tagged GUID ID: `workspaceId`, `moduleId`, `projectId`, or `runId`. Its definition filename is `<id>.msgpack`. IDs survive edits, moves, and missing-file restoration. Folder names exist only on workspaces, modules, and projects. The absolute workspace location stays in application settings, outside the definitions.

Names must be nonblank, and sibling display names must be distinct ignoring case. Sibling module/project folder names must also be distinct ignoring case. Runs in a project require distinct IDs because their files share one directory. Rejected edits remain visible for correction while the tree keeps the last accepted value; Save is disabled until corrected. Saved names and folder names stay reserved until their removal or rename has been saved.

`Sortopia.Workspaces.Serialization` writes version-4 MessagePack DTOs. Modules and projects explicitly persist `moduleKind` or `projectKind` as `root` or `branch`; runs persist `runKind`. Decode validates IDs, settings, and all type restrictions. Older schema versions are rejected; there is no legacy migration. Each container's definition includes a complete snapshot of its descendants.

```text
workspace-folder/
  <workspace-id>.msgpack
  modules/
    Root research/
      <root-module-id>.msgpack
      modules/
        Nested root module/
          <root-module-id>.msgpack
      projects/
        Baseline/
          <root-project-id>.msgpack
          runs/
            <root-run-id>.msgpack
    Branch research/
      <branch-module-id>.msgpack
      projects/
        Evolution/
          <branch-project-id>.msgpack
          runs/
            <branch-run-id>.msgpack
```

Definition limits are 64 nesting levels, 50,000 entities, and 16 MB per definition file. Multiple container definitions in one entity folder are reported as ambiguous. Unrelated files are not serialized; explicit folder imports and moves preserve their contents.
