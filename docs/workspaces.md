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

## Project defaults and run inputs

Select a project to edit labeled fields for its description, results database, and execution function. Choose output types with checkboxes and use **Add query key** to add named rows. Branch projects also have setup database/function fields, setup output types, and setup query keys. Supply nonblank database and function names before saving a project. The setup query-key list applies to every declared setup output type.

Select a run to edit its description and execution parameters. Branch runs additionally have setup database/function overrides and setup parameters. Each optional field displays its effective value and a source label: **Inherited from [project]** or **Overridden for this run**. Inherited fields are read-only and follow project edits. Check **Override for this run** to edit a local value; uncheck it to restore inheritance. An overridden description may be blank. Explicit setup database/function overrides must be nonblank. The underlying run inputs retain `None` for inheritance and `Some value` for overrides.

The run's execution settings, output types, and query keys are shown as inherited, with **Edit project defaults** available to select the parent. Run type, names, and IDs come from the hierarchy. Output types use readable names in the editor; storage codes remain `3` (`SorterModelSet`) and `4` (`SorterSetEval`). There is no JSON editor.

Use **Add parameter** to add a key and candidate values, or **Add required parameters** to create rows for missing project query keys. Each value has its own text field and remove action. Commas, spaces, Unicode, and explicitly blank values are preserved; values are not parsed as a delimited list. Each parameter needs at least one value. Duplicate keys, missing required parameters, and blank required fields show inline errors and disable Save until corrected. **Discard changes** also clears invalid field edits.

The inspector scrolls through the full form; folder names and paths are available under **Location**. Selecting another tree item returns the inspector to the top. Configuration is validated before saving as MessagePack. The Workspace page defines runs; it does not execute them.

Changing project settings updates every run's inherited values when the hierarchy is rebuilt or saved. Explicit run overrides survive those changes. Runs preserve their original optional inputs through saves, copies, and moves, so inheritance remains active after reopening. Moving a run to a matching project uses the destination's defaults and validates the run's spans against its query keys.

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

Projects expose their shared properties directly and through `Configuration` (`rootProjectConfig` or `branchProjectConfig`). `RootRun.create parent inputs` and `BranchRun.create parent inputs` accept their concrete parent projects through `IRootProject` / `IBranchProject`; the contracts avoid a dependency from runs back to workspaces. Run input records include the existing run ID/name plus the optional overrides and parameter spans. A run's `Configuration` retains those inputs, while properties such as `Description` and `SetupFunctionName` return resolved values. Rebuilding a project rebinds its runs to its current settings.

The four shared names have distinct UMX tags: `resultsDatabaseName`, `executeFunctionName`, `setupDatabaseName`, and `setupFunctionName`. Setup query keys are a `string<paramKeyName> list`, just like output query keys.

Every entity has a UMX-tagged GUID ID: `workspaceId`, `moduleId`, `projectId`, or `runId`. Its definition filename is `<id>.msgpack`. IDs survive edits, moves, and missing-file restoration. Folder names exist only on workspaces, modules, and projects. The absolute workspace location stays in application settings, outside the definitions.

Names must be nonblank, and sibling display names must be distinct ignoring case. Sibling module/project folder names must also be distinct ignoring case. Runs in a project require distinct IDs because their files share one directory. Rejected edits remain visible for correction while the tree keeps the last accepted value; Save is disabled until corrected. Saved names and folder names stay reserved until their removal or rename has been saved.

`Sortopia.Workspaces.Serialization` writes version-5 MessagePack DTOs. Modules and projects explicitly persist `moduleKind` or `projectKind` as `root` or `branch`; runs persist `runKind`. Project definitions store the shared `configuration`; run definitions store only identity, optional overrides, and parameter spans. `WorkspaceCodec.decodeRun parent bytes` resolves a run against its parent project. Decode validates IDs, settings, and all type restrictions. Older schema versions are rejected; there is no legacy migration. Each container's definition includes a complete snapshot of its descendants.

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
