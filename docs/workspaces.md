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

The inspector edits display names and module/project folder names while the entity is editable. A sealed project keeps its name, folder name, and configuration. New folder names follow their display names until saved or manually edited. Saved display-name edits keep folder names fixed. Names may be descriptive, such as `Search: prefixes`, while folder names must be portable directory segments, such as `prefix-search`.

Runs have no folder name. A run is stored directly as `runs/<run-id>.msgpack`. The **Full path** field shows the selected folder or run file and supports text selection/copy. **Open folder** opens that folder, or the containing `runs` directory for a run.

## Module defaults, project defaults, and run inputs

Defaults begin on top-level modules; the workspace itself has no configuration defaults. Root modules and projects share Description, ResultsDatabaseName, ResultsFunctionName, ResultsSortopiaDataTypes, ResultsQueryParameterKeys, and ResultsParameterDefaults. Branch modules and projects also have SetupDatabaseName, SetupFunctionName, SetupDataTypes, and SetupParameterDefaults. Each setup data-type entry combines its exact type/text pair, SourceProjectId, and its own QueryParameterKeys. The three logical views (setup types, source IDs, and query-key map) are derived from this single list.

Each property follows the nearest ancestor with a local override. The inspector displays its effective value and the name of the ancestor supplying it. Click an **Inherited from [ancestor name]** link to select the module or project defining that property. Check **Override on this module** or **Override on this project** to edit a local value; uncheck it to restore inheritance. New modules and projects inherit every property. With no ancestor override, database names begin as `Results` and `Setup`, descriptions and lists are empty, and function names are unset. Organizational modules can leave functions unset; projects need resolved, nonblank database and function names before saving.

Choose each results data type with a case selector and free-form text field. Use **Add query key** to add named rows. Data-type and query-key list overrides replace the entire inherited list; an explicitly empty list clears it. An explicitly blank description also overrides its parent. Enabling these overrides initially copies the inherited value into the editor. Turning an override off and back on preserves the draft local value during the current edit session.

Select a run to edit its description and execution parameters. Branch runs additionally have setup database/function overrides and setup parameters. Each optional field displays its effective value and a source label: **Inherited from [ancestor name]** or **Overridden for this run**. Before the first save, inherited fields follow the draft project settings. After sealing, runs inherit the fixed project settings. Check **Override for this run** to edit a local value; uncheck it to restore inheritance. An overridden description may be blank. Explicit setup database/function overrides must be nonblank. The underlying run inputs retain `None` for inheritance and `Some value` for overrides.

The run's execution settings, output types, and query keys are shown as inherited. **Edit parent defaults** selects the containing project or module, and is available even while a required inherited value is missing. Run type and parent ID come from the containing project. Data types persist as case/text pairs. There is no JSON editor.

Use **Add default parameter** on a project or module to supply one typed value per key. Choose a known key from the editable picker or enter a custom key. Known keys use their existing `ParamKeys` type; custom keys let you choose text, integer, unsigned 64-bit integer, floating point, Boolean, or GUID. Boolean values use a True/False selector. Numeric and GUID fields retain invalid drafts and show validation errors. Numbers use invariant formatting, floating values must be finite, and integer bounds are checked. Text preserves spaces, commas, Unicode, line breaks, and blank values.

Parameter defaults inherit **by key**. A child adding `StageLength = 1` keeps its parent's `ChannelCount = 16`; a child setting `ChannelCount = 8` replaces only that key. Results and setup defaults merge independently. The inspector shows inherited defaults alongside local defaults, with a clickable source for each inherited value. Choose **Override here** to copy just that key into a local editor. Removing a local row restores its inherited value, if any. An empty local list inherits all parent defaults. Only local bindings are saved as overrides, so other inherited values continue to follow ancestor edits until a project is sealed.

Runs receive the resolved default lists from their parent project. Use **Add parameter** to add a span of candidate values, or **Add required parameters** to create rows for query keys that have neither a default nor a span. Span values use the default's type, or the known key's type when there is no default. Unregistered keys without a default are text. Each span needs at least one value; blank values are valid only for text. Duplicate keys, invalid typed values, missing required parameters, and blank required fields show inline errors and disable Save until corrected. **Discard changes** also clears invalid field edits.

Parameter sets contain all combinations of the run's span values. A span replaces the default for the same key; all other defaults stay fixed. For example, defaults `ChannelCount = 8, Seed = 7` with a `Seed` span of `11, 13` produce two sets: `{ChannelCount = 8, Seed = 11}` and `{ChannelCount = 8, Seed = 13}`. No spans produces one set containing the defaults (an empty set if both lists are empty). Branch setup and execution sets are generated independently. Runs display the inherited values, identify defaults overridden by spans, and show the number of parameter combinations.

Run editors show a **Results parameter sets** table; branch runs also show a separate **Setup parameter sets** table. Each row is a generated combination in execution order, using the effective inherited defaults and local spans. Column headers show the key, type, and whether the value comes from a default or a run span. Previews update with edits, show the first 50 combinations and total count, and never allocate the full product just to display it. Invalid parameters or missing required query values replace the affected preview with an error. An empty input produces one empty set. Previewing does not execute the run.

Module, project, and run editors group description, results settings, parameter defaults, spans, and previews into expanders, with corresponding setup sections for branch types. Each entity remembers its expanded sections while navigating the current draft. Errors remain visible in section headers even when their fields are collapsed.

Results and setup functions use catalog-name comboboxes. Enable a local override to choose a function; inherited and sealed values remain read-only and retain their source links. Results choices come from `FunctionCatalogs.resultsFunctionCatalog`, and setup choices come from `FunctionCatalogs.setupFunctionCatalog`. Selecting a name changes only the function assignment, leaving data types, query keys, and parameter defaults under your control.

The catalogs are plain immutable maps in `Sortopia.Functions`, keyed by `string<resultsFunctionName>` and `string<setupFunctionName>`. `ResultsFunctionNames` and `SetupFunctionNames` hold the typed name variables. To add an entry, write an independent function and add its name/function pair to the corresponding map in `FunctionCatalogs.fs`.

The initial functions adapt GeneSort's standalone computation to Sortopia's current types. They accept one typed `parameterSet` and a list of in-memory `functionData`, returning `Result<functionData list, string>`. There is no registration lifecycle.

### Execute a run

Save the workspace, select a run, and choose **Execute** in its Execution section. Execution uses the saved run's fixed project defaults and local spans, expanding all parameter sets (not just the first 50 shown in the preview). Unsaved changes, input errors, missing saved components, and unknown catalog names disable Execute with an explanation.

Root runs call their results function once per results parameter set. Branch runs first call their selected setup function for every setup parameter set. After all setup calls succeed, each setup output batch is passed to the results function with every results parameter set: 2 setup sets × 3 results sets means 2 setup calls followed by 6 results calls. Outputs from different setup sets stay separate. A setup failure prevents all results calls; any other failure stops remaining calls and identifies the function and parameter set.

Execution runs off the UI thread and disables editing and duplicate execution. **Cancel operation** stops between function calls; the current synchronous catalog call must return first. Progress shows completed calls. The Execution section retains the last attempt for each run while the workspace is open, including completed setup/results calls and their typed outputs, even after a failure or cancellation. The expandable output summary shows the first 100 completed calls. Executing again replaces that run's previous report.

Outputs are currently held in memory, without writing to the configured databases or modifying run definitions. The UI starts with no external input data; source-project data loading is not connected yet. Functions requiring unavailable inputs report a specific error. `RunExecution.execute` also accepts an explicit typed input-data list for callers that already have data; branch results receive that list plus the corresponding setup output batch.

| Catalog | Name | Function and required inputs |
| --- | --- | --- |
| Results | `sortable-tests.publish` | Generate tests with their prefix CEs; requires `SortableDataFormat` and exactly one of `MergeLibId` or `PrefixLibId`. |
| Results | `sorter-eval.evaluate` | Evaluate one supplied sorter model set against one supplied tests/prefix pair; requires `ChannelCount`, `SorterEvalType`, and `CollectSortableTests`. |
| Setup | `sortable-tests-with-prefix.standard` | Generate full tests with an empty prefix; requires `ChannelCount` and `SortableDataFormat`. |
| Setup | `sorter-model-set.uniform` | Generate models with an empty prefix; requires `ChannelCount`, `StageLength`, `SimpleSorterModelType`, `RngType`, `ExcludeSelfCe`, `SorterCount`, and `Repl`. Replication selects a deterministic span of model indices. |

For example, `SortableDataFormat = Bools`, `SimpleSorterModelType = Mssi`, `RngType = SplitMix64`, and `SorterEvalType = V1` select existing Sortopia formats and algorithms. GeneSort attribution and its license are retained alongside the ported functions.

The inspector scrolls through the full form; folder names and paths are available under **Location**. Selecting another tree item returns the inspector to the top. Configuration is validated before saving as MessagePack.

Changing module settings updates descendant modules and draft projects that inherit each property. A project is sealed when its first valid run is saved: all effective settings become local, fixed values in the same transaction as the run. Failed saves leave the draft editable. Removing the last run does not unseal the project. Sealed projects keep their settings across ancestor edits, moves, and imports. **Create new version** creates an editable copy with a new project ID and no runs. Existing runs keep their parent project ID; create a new run to use another project.

## Setup outputs and source projects

Each setup row describes data **produced by the setup function**. Its source picker lists projects providing exactly the same `sortopiaDataType`, including the free-form string. A project can provide several types, and several entries may select that project. There is exactly one entry per type/text pair. Sources may be root or branch projects anywhere in the workspace. A draft consumer can refer to a draft source; its source must be sealed before the consumer saves runs. Missing projects, mismatched types, duplicate entries, and cyclic project dependencies are rejected.

The row's query keys identify the produced setup data. Fetching source data uses the source project's **results** query keys and results database instead. Setup parameter sets must supply both sets of keys through defaults or spans. The inspector shows source query requirements and links to the source project. The whole setup-entry list inherits or overrides as one property, so the types, IDs, and key lists cannot drift apart.

## Reorganizing and importing

Use **Cut** and **Paste**, the tree's context menu, or **Ctrl+X / Ctrl+V** while the tree has focus. Text fields retain normal text-editing shortcuts. **Cancel cut** or **Escape** clears the cut selection. Cut alone does not change the draft. Paste is disabled for incompatible types, including a project selected as the parent of another project. Its tooltip explains a type mismatch. Cycles, sibling collisions, and occupied destinations are rejected.

A successful paste changes the draft. Saving moves folders and their contents while preserving entity IDs and sealed project settings. Runs can be reordered within their project but cannot be moved to a different project. Discard cancels pending moves. Moving an item within its current parent reorders its typed collection.

**Add module…** and **Add project…** import existing saved entities. Modules can be added to workspaces or matching modules; projects can be added only to matching modules. Imports require valid definition files identifying their root/branch type. All descendants must match. Plain folders without definitions cannot establish an entity's type.

The picker starts in the selected parent's matching container or its nearest existing ancestor. An external import is copied only on save, including output files and empty folders. Copying assigns fresh IDs and filenames to the copied entities and descendants. Internal source references and run parent IDs are remapped together; external source IDs are preserved and validated in the destination workspace. Source files stay unchanged. An entity already at the exact destination is attached in place and keeps its IDs. Existing destinations are not merged or overwritten.

Imports read the actual disk hierarchy, ignoring stale child lists in parent snapshots. Wrong types, malformed settings, forbidden child containers, and name/folder collisions leave the draft unchanged. Changes to an import source require removing the pending import and adding it again. Linked paths and overlapping source/destination trees are rejected. A child moved out of an unsaved import is copied once to its new destination, excluding it from the parent copy.

## Saving and disk status

Save creates the required folders and writes all definitions transactionally, with the workspace snapshot written last. Failed or canceled operations restore previous definitions and locations. External changes detected since loading or during a save prevent overwriting those changes. Recovery failures are reported explicitly.

The workspace definition is authoritative. Removing an entity keeps its existing files on disk and reports retained items. Changing an editable entity's folder name creates the new folder and retains the old folder and output data; use Cut/Paste to move an existing entity with its contents. Changes made directly on disk are not automatically adopted; import an existing entity to add its actual hierarchy.

The tree distinguishes unsaved items, pending copies/moves, and missing saved definitions. Status refreshes while the page is open and after loading, saving, or discarding. Save can restore missing definitions with the same IDs. A missing workspace root directory must be restored or another location selected first.

Before changing the root folder, returning Home, or closing, unsaved changes prompt for **Save / Discard / Cancel**. Save continues only after success; Discard abandons the draft; Cancel keeps editing. Selecting the same folder or canceling the picker keeps the current draft. Choosing an empty root without editing it does not count as an edit.

## Types and persistence

`Sortopia.Workspaces` defines concrete `rootProject`, `branchProject`, `rootModule`, and `branchModule` types. Their child arrays are strongly typed: for example, `rootProject.Runs` is `rootRun[]`, and `branchModule.Projects` is `branchProject[]`. The `project` and `workspaceModule` discriminated unions provide common views for storage and the editor. Their boundary factories validate type consistency. `workspace.Modules` accepts either `workspaceModule.Root` or `workspaceModule.Branch`. Array properties return copies.

Modules and projects expose their effective shared properties directly and through `Configuration` (`rootProjectConfig` or `branchProjectConfig`). `LocalOverrides` uses `rootDefaultOverrides` or `branchDefaultOverrides`: each property is optional, with `None` meaning inherit and `Some` meaning local, including `Some []` and `Some ""`. `Defaults.resolveRoot` / `resolveBranch` apply local values over the inherited configuration. For parameter defaults, `Defaults.mergeParameterDefaults` replaces matching keys, retains other inherited bindings in order, and appends new local keys. Both `None` and `Some []` inherit all parameter defaults; duplicate keys within a local list are invalid. Other list properties use whole-list replacement. `createInherited` constructs an entity with its parent configuration and local overrides; `rebind` resolves it and its descendants against a new parent. The existing project `create` factory makes every supplied configuration property local; the module `create` factory starts with all properties inherited.

`RootRun.create parent inputs` and `BranchRun.create parent inputs` accept their concrete parent projects through `IRootProject` / `IBranchProject`; the contracts avoid a dependency from runs back to workspaces. Run input records contain Id, RunName, ParentProjectId, DescriptionOverride, and ResultsParameterSpans. Branch inputs also contain SetupDatabaseNameOverride, SetupFunctionNameOverride, and SetupParameterSpans. The parent must be sealed when constructing a runtime run; `project.seal` creates that immutable parent snapshot and `project.withRuns` attaches the validated runs. `RunDto.toDomain` prepares a sealed snapshot without mutating the draft parent. A run retains its inputs in `Configuration` and fixed settings in `ProjectConfiguration`. Branch runs also retain resolved source project snapshots, so queries need no further workspace lookup.

The four shared names have distinct UMX tags: `resultsDatabaseName`, `resultsFunctionName`, `setupDatabaseName`, and `setupFunctionName`. Shared configuration is factored into `resultsConfig` and `setupConfig`. A `setupDataTypeConfig` contains `DataType: sortopiaDataType`, `SourceProjectId: Guid<projectId>`, and `QueryParameterKeys: string<paramKeyName> list`. `setupConfig.DataTypes` is the ordered list; its `SortopiaDataTypes`, `SourceProjectIds`, and `QueryParameterKeys` views derive from those entries. Results query keys remain one list.

`workspace.ProjectIndex` indexes projects globally by ID, and `FindProject` returns an optional project. Workspace creation/loading validates references and resolves branch source snapshots through `BranchRunSources.resolve`. `QueryParams.forSource` uses the source project's ID, results database, and results query keys. `forSetup` uses the parent project's ID, effective setup database, and the selected entry's query keys; `forResults` uses the parent project's results metadata. Query IDs use the stable project ID, exact type/text pair, and selected parameter strings, rather than the display name.

`sortopiaDataType` is a discriminated union with ten cases: `Workspace`, `Run`, `WorkspaceModule`, `RunParameters`, `SorterPoolSet`, `SorterSet`, `SorterModelSet`, `SortableTests`, `SorterSetEval`, and `TextReport`. Every case carries a free-form `string`; empty strings, whitespace, Unicode, and line breaks are preserved. Execution and setup output lists use this type, as do result and setup queries. Each output row has a case selector and a text field. Lists can contain the same case with different text; an identical case/text pair cannot appear twice. Inherited lists show both values and retain their clickable source. Query matching and query IDs include both the case and the exact text.

`parameterBinding` is the general pairing of a `string<paramKeyName>` with a typed `parameterValue`. It can represent a supplied parameter, a default, or an override; the containing property or operation determines its role. `ParameterBinding.create key value` constructs a binding from a typed key, `fromValue name value` accepts an already typed union value, and `fromText name kind text` validates editor or persisted input. All constructors enforce the known key's type and valid values. `ParameterBinding.validateUnique` validates collections with one binding per key. `parameterBindingDto` and `WorkspaceParameterBinding` provide the corresponding storage and editor representations.

`parameterSet` stores a `Map<string<paramKeyName>, parameterBinding>` exposed by `ParamMap`. `parameterSet.fromBindings bindings` creates a set directly; `parameterSet.create map` accepts a typed map and validates that each key matches its binding. `set.WithBinding(binding)` adds or replaces a binding while preserving the original set. `WithValue` accepts a typed key/value or a key name with a `parameterValue`; `None` removes the key. Binding construction, validation, and set updates retain the typed values without formatting or reparsing them.

`set.TryFind(name)` returns a binding. `set.TryFind(typedKey)` reads the underlying CLR value directly, returning `None` for a missing key or mismatched type; numeric-looking text remains text. String conversion is explicit: `TryFindText(name)` formats one value, and `ToStringMap()` projects the whole set to canonical invariant strings. Queries format only their selected keys; DTO serialization formats bindings at the storage boundary.

`ParameterSets.create bindings spans` expands bindings and spans into `parameterSet[]`. It parses span text using the matching binding's type or the known key's type, and stores each candidate as a typed binding. Matching spans replace bindings and other bindings remain fixed. Combinations exceeding the maximum .NET array length are rejected before allocation.

`ParameterSets.preview maxCount bindings spans` shares the same validation and combination generator, materializing only the requested prefix. All candidate values are validated, including values beyond the visible rows. The editor retains typed parameter sets and formats values only for display.

`RunParameterSets.create run` returns a `runParameterSets` record with `Results: parameterSet array` and `Setup: parameterSet array option`. Root runs return `Setup = None`; branch runs return `Some setupSets`. Results and setup combinations are expanded independently from their respective defaults and spans, retaining typed bindings. No spans produces one set of defaults, including one empty set when there are no defaults. The function uses the run's resolved settings and does not need a workspace lookup.

`ResultsParameterDefaults` is a `parameterBinding list` on root/branch modules, projects, and runs. Branch types additionally expose `SetupParameterDefaults`. These properties use bindings as defaults, merging by key through modules and draft projects. Runs receive the sealed project's resolved defaults; their spans replace matching keys. `RootRun.makeResultsParameterSets`, `BranchRun.makeResultsParameterSets`, and `BranchRun.makeSetupParameterSets` use the general binding APIs. `ParamKeys.all` supplies metadata derived from the typed key declarations.

Every entity has a UMX-tagged GUID ID: `workspaceId`, `moduleId`, `projectId`, or `runId`. Its definition filename is `<id>.msgpack`. IDs survive edits, moves, and missing-file restoration. Folder names exist only on workspaces, modules, and projects. The absolute workspace location stays in application settings, outside the definitions.

Names must be nonblank, and sibling display names must be distinct ignoring case. Sibling module/project folder names must also be distinct ignoring case. Runs in a project require distinct IDs because their files share one directory. Rejected edits remain visible for correction while the tree keeps the last accepted value; Save is disabled until corrected. Saved names and folder names stay reserved until their removal or rename has been saved.

`Sortopia.Workspaces.Serialization` writes schema version 9 MessagePack DTOs. Data types are `{ kind, value }` pairs. Setup entries are `{ dataType, sourceProjectId, queryParameterKeys }`. Projects persist `isSealed`, effective configuration, local overrides, and their runs. Modules retain inheritable local overrides and effective snapshots. Null override fields inherit; empty arrays are explicit empty lists. Draft projects resolve against the actual hierarchy; sealed projects load their fixed configuration snapshots. Run files contain IDs, optional overrides, and spans. `WorkspaceCodec.decodeRunById workspace bytes` finds the stored ParentProjectId, verifies a sealed parent of the correct family, and resolves source snapshots; `decodeRun parent bytes` supports callers already holding a sealed parent. Cross-project validation occurs after the hierarchy loads. Unknown or older schema versions are rejected, with no legacy migration. Each container definition includes its descendants.

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
