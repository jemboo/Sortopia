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

## Module defaults, project defaults, and run inputs

Defaults begin on top-level modules; the workspace itself has no configuration defaults. Root modules and root projects share description, results database, execution function, output types, output query keys, and execution parameter defaults. Branch modules and branch projects also have setup database/function fields, setup output types, setup query keys, and a separate list of setup parameter defaults. The setup query-key list applies to every declared setup output type.

Each property follows the nearest ancestor with a local override. The inspector displays its effective value and the name of the ancestor supplying it. Click an **Inherited from [ancestor name]** link to select the module or project defining that property. Check **Override on this module** or **Override on this project** to edit a local value; uncheck it to restore inheritance. New modules and projects inherit every property. With no ancestor override, database names begin as `Results` and `Setup`, descriptions and lists are empty, and function names are unset. Organizational modules can leave functions unset; projects need resolved, nonblank database and function names before saving.

Choose output types with checkboxes and use **Add query key** to add named rows. A list override replaces the entire inherited list; an explicitly empty list clears it. An explicitly blank description also overrides its parent. Enabling an override initially copies the inherited value into the editor. Turning it off and back on preserves the draft local value during the current edit session.

Select a run to edit its description and execution parameters. Branch runs additionally have setup database/function overrides and setup parameters. Each optional field displays its effective value and a source label: **Inherited from [ancestor name]** or **Overridden for this run**. Inherited fields are read-only and follow project and module edits. Check **Override for this run** to edit a local value; uncheck it to restore inheritance. An overridden description may be blank. Explicit setup database/function overrides must be nonblank. The underlying run inputs retain `None` for inheritance and `Some value` for overrides.

The run's execution settings, output types, and query keys are shown as inherited. **Edit parent defaults** selects the containing project or module, and is available even while a required inherited value is missing. Run type, names, and IDs come from the hierarchy. Output types use readable names in the editor; storage codes remain `3` (`SorterModelSet`) and `4` (`SorterSetEval`). There is no JSON editor.

Use **Add default parameter** on a project or module to supply one typed value per key. Choose a known key from the editable picker or enter a custom key. Known keys use their existing `ParamKeys` type; custom keys let you choose text, integer, unsigned 64-bit integer, floating point, Boolean, or GUID. Boolean values use a True/False selector. Numeric and GUID fields retain invalid drafts and show validation errors. Numbers use invariant formatting, floating values must be finite, and integer bounds are checked. Text preserves spaces, commas, Unicode, line breaks, and blank values.

Runs receive the resolved default lists from their parent project. Use **Add parameter** to add a span of candidate values, or **Add required parameters** to create rows for query keys that have neither a default nor a span. Span values use the default's type, or the known key's type when there is no default. Unregistered keys without a default are text. Each span needs at least one value; blank values are valid only for text. Duplicate keys, invalid typed values, missing required parameters, and blank required fields show inline errors and disable Save until corrected. **Discard changes** also clears invalid field edits.

Parameter sets contain all combinations of the run's span values. A span replaces the default for the same key; all other defaults stay fixed. For example, defaults `ChannelCount = 8, Seed = 7` with a `Seed` span of `11, 13` produce two sets: `{ChannelCount = 8, Seed = 11}` and `{ChannelCount = 8, Seed = 13}`. No spans produces one set containing the defaults (an empty set if both lists are empty). Branch setup and execution sets are generated independently. Runs display the inherited values, identify defaults overridden by spans, and show the number of parameter combinations.

The inspector scrolls through the full form; folder names and paths are available under **Location**. Selecting another tree item returns the inspector to the top. Configuration is validated before saving as MessagePack. The Workspace page defines runs; it does not execute them.

Changing module or project settings immediately updates every descendant that still inherits the property, including source labels and validation. Local overrides survive those changes. Saves, copies, and moves preserve which properties are local, so inheritance remains active after reopening. Moving or importing a subtree uses its destination's ancestor defaults. Moving a run to a matching project uses that project's defaults and validates the run's spans against its query keys.

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

Modules and projects expose their effective shared properties directly and through `Configuration` (`rootProjectConfig` or `branchProjectConfig`). `LocalOverrides` uses `rootDefaultOverrides` or `branchDefaultOverrides`: each property is optional, with `None` meaning inherit and `Some` meaning local, including `Some []` and `Some ""`. `Defaults.resolveRoot` / `resolveBranch` apply local values over the inherited configuration. `createInherited` constructs an entity with its parent configuration and local overrides; `rebind` resolves it and its descendants against a new parent. The existing project `create` factory makes every supplied configuration property local; the module `create` factory starts with all properties inherited.

`RootRun.create parent inputs` and `BranchRun.create parent inputs` accept their concrete parent projects through `IRootProject` / `IBranchProject`; the contracts avoid a dependency from runs back to workspaces. Run input records include the existing run ID/name plus the optional overrides and parameter spans. A run's `Configuration` retains those inputs, while properties such as `Description` and `SetupFunctionName` return resolved values. Rebuilding a project rebinds its runs to its current settings.

The four shared names have distinct UMX tags: `resultsDatabaseName`, `executeFunctionName`, `setupDatabaseName`, and `setupFunctionName`. Setup query keys are a `string<paramKeyName> list`, just like output query keys.

`OutputParameterDefaults` is a `parameterDefault list` on root/branch modules, projects, and runs. Branch types additionally expose `SetupParameterDefaults`. Each default stores a `string<paramKeyName>` and a typed `parameterValue` union. `ParameterDefault.create key value` constructs one from a typed key; `ParameterDefault.fromText name kind text` validates editor or persisted input. `ParamKeys.all` supplies metadata derived from the typed key declarations. `ParameterSets.create defaults spans`, `RootRun.makeParameterSets`, `BranchRun.makeParameterSets`, and `BranchRun.makeSetupParameterSets` return `parameterSet[]`. Values are formatted canonically at this boundary and remain compatible with `parameterSet.TryFind` using typed keys. Combinations exceeding the maximum .NET array length are rejected before allocation.

Every entity has a UMX-tagged GUID ID: `workspaceId`, `moduleId`, `projectId`, or `runId`. Its definition filename is `<id>.msgpack`. IDs survive edits, moves, and missing-file restoration. Folder names exist only on workspaces, modules, and projects. The absolute workspace location stays in application settings, outside the definitions.

Names must be nonblank, and sibling display names must be distinct ignoring case. Sibling module/project folder names must also be distinct ignoring case. Runs in a project require distinct IDs because their files share one directory. Rejected edits remain visible for correction while the tree keeps the last accepted value; Save is disabled until corrected. Saved names and folder names stay reserved until their removal or rename has been saved.

`Sortopia.Workspaces.Serialization` writes version-7 MessagePack DTOs. Modules and projects explicitly persist `moduleKind` or `projectKind` as `root` or `branch`; runs persist `runKind`. Modules and projects store `localOverrides` separately from their effective `configuration` snapshot, including typed parameter default arrays (`keyName`, `valueType`, and a formatted `value`). Null fields in `localOverrides` inherit; empty arrays are explicit empty overrides. Workspace decoding resolves top-down from the actual hierarchy and ignores cached effective values. Standalone module/project decoding uses its snapshot as temporary inherited context so it can be imported, then attaching it resolves against the destination. Run definitions store only identity, optional overrides, and parameter spans. `WorkspaceCodec.decodeRun parent bytes` resolves a run against its parent project. Decode validates IDs, settings, parameter types, and all type restrictions. Older schema versions are rejected; there is no legacy migration. Each container's definition includes a complete snapshot of its descendants.

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
