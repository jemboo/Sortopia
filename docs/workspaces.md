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

Defaults begin on top-level modules; the workspace itself has no configuration defaults. Root modules and projects share Description, ResultsFunctionName, ResultsSortopiaDataTypes, ResultsQueryParameterKeys, and ResultsParameterDefaults. Branch modules and projects also share SetupFunctionName, SetupDataTypes, and SetupParameterDefaults. Database names belong only to projects: each root project defines ResultsDatabaseName, and each branch project defines both ResultsDatabaseName and SetupDatabaseName. These are direct fields with initial values `Results` and `Setup`, with no inheritance toggle. Modules have no database properties, and runs display their project’s database names as read-only values with a link to that project. Each setup data-type entry combines its exact type/text pair, SourceProjectId, and its own QueryParameterKeys. The three logical views (setup types, source IDs, and query-key map) are derived from this single list.

Each property follows the nearest ancestor with a local override. The inspector displays its effective value and the name of the ancestor supplying it. Click an **Inherited from [ancestor name]** link to select the module or project defining that property. Check **Override on this module** or **Override on this project** to edit a local value; uncheck it to restore inheritance. New modules and projects inherit every property. With no ancestor override, descriptions and lists are empty and function names are unset. Organizational modules can leave functions unset; projects need resolved, nonblank database and function names before saving.

Choose each results data type with a case selector and free-form text field. Use **Add query key** to add named rows. Results query keys and each setup output's query keys use editable dropdowns containing registered parameter keys and custom keys from inherited and local defaults for that phase. Suggestions update as defaults change; you can also type a custom key, and an entered key stays intact if its default is renamed or removed. Data-type and query-key list overrides replace the entire inherited list; an explicitly empty list clears it. An explicitly blank description also overrides its parent. Enabling these overrides initially copies the inherited value into the editor. Turning an override off and back on preserves the draft local value during the current edit session.

Select a run to edit its description and execution parameters. Branch runs additionally have a setup function override and setup parameters. Database names always come from the project. Each optional field displays its effective value and a source label: **Inherited from [ancestor name]** or **Overridden for this run**. Before the first save, inherited fields follow the draft project settings. After sealing, runs inherit the fixed project settings. Check **Override for this run** to edit a local value; uncheck it to restore inheritance. An overridden description may be blank. An explicit setup function override must be nonblank. The underlying run inputs retain `None` for inheritance and `Some value` for overrides.

The run's execution settings, output types, and query keys are shown as inherited. **Edit parent defaults** selects the containing project or module, and is available even while a required inherited value is missing. Run type and parent ID come from the containing project. Data types persist as case/text pairs. There is no JSON editor.

Use **Add default parameter** on a project or module to supply one typed value per key. The editable key picker includes registered keys and keys from inherited and local defaults for that phase. Suggestions update when defaults change. Selecting an inherited custom key initially uses its inherited type; you may still change a custom key’s type. Known keys use their existing `ParamKeys` type; custom keys support text, integer, unsigned 64-bit integer, floating point, Boolean, GUID, sorter library ID, prefix library ID, and merge library ID. Defaults use the same key-specific editors and validation as run spans, including catalog value dropdowns and numeric limits. Boolean values use a True/False selector. Numeric and GUID fields retain invalid drafts and show validation errors. Numbers use invariant formatting, floating values must be finite, and integer bounds are checked. Unconstrained text preserves spaces, commas, Unicode, line breaks, and blank values. Changing a default's key preserves its value and revalidates it. Invalid local or inherited defaults appear in the save explanation and block the affected run preview until corrected; **Override here** uses the same editor for local overrides.

`SorterLibId`, `PrefixLibId`, and `MergeLibId` are distinct typed parameter values backed by the existing catalog ID records. Their shared editor is used for module/project defaults, run spans, and custom keys assigned one of these types. Choose a catalog entry to populate its fields, or enter channel count and variant directly. Prefix IDs also require a positive stage length; merge IDs require a positive merge dimension that divides the channel count exactly. Merge catalog suggestions update with the channel count. Invalid field drafts and incompatible values retained after a type change remain visible and block saving. A structurally valid ID may have no catalog entry; lookup availability is checked by the function using it.

Parameter defaults inherit **by key**. A child adding `StageLength = 1` keeps its parent's `ChannelCount = 16`; a child setting `ChannelCount = 8` replaces only that key. Results and setup defaults merge independently. The inspector shows inherited defaults alongside local defaults, with a clickable source for each inherited value. Choose **Override here** to copy just that key into a local editor. Removing a local row restores its inherited value, if any. An empty local list inherits all parent defaults. Only local bindings are saved as overrides, so other inherited values continue to follow ancestor edits until a project is sealed.

Runs receive the resolved default lists from their parent project. Use **Add parameter** to add a span of candidate values, or **Add required parameters** to create rows for query keys that have neither a default nor a span. The parameter-key dropdown lists registered keys, defaults and query keys for that phase, and existing span keys. You can also enter a custom key. Span values use the default's type, or the known key's type when there is no default. Unregistered keys without a default are text.

Selecting a key chooses its editor and validation. Booleans use True/False; data formats, RNG types, model types and evaluator types offer value dropdowns using their domain parsers. Numeric fields retain invariant numeric validation; ChannelCount and SorterCount must be at least 1, while StageLength and Repl may be 0. GUIDs and free-form text retain their respective editors. Library IDs use structured editors as described below; catalog availability and constraints involving several parameters are checked by execution. Changing the key preserves entered values and immediately flags incompatible ones. Each span needs at least one value; blank values are valid only for unconstrained text. Errors identify the key, clear the affected parameter-set preview, and block Save and Execute until corrected. Results and setup span editors use the same rules, with independent inherited defaults. **Discard changes** also clears invalid field edits.

Parameter sets contain all combinations of the run's span values. A span replaces the default for the same key; all other defaults stay fixed. For example, defaults `ChannelCount = 8, Seed = 7` with a `Seed` span of `11, 13` produce two sets: `{ChannelCount = 8, Seed = 11}` and `{ChannelCount = 8, Seed = 13}`. No spans produces one set containing the defaults (an empty set if both lists are empty). Branch setup and execution sets are generated independently. Runs display the inherited values, identify defaults overridden by spans, and show the number of parameter combinations.

Run editors show a **Results parameter sets** table; branch runs also show a separate **Setup parameter sets** table. Each row is a generated combination in execution order, using the effective inherited defaults and local spans. Column headers show the key, type, and whether the value comes from a default or a run span. Previews update with edits, show the first 50 combinations and total count, and never allocate the full product just to display it. Invalid parameters or missing required query values replace the affected preview with an error. An empty input produces one empty set. Previewing does not execute the run.

The workspace has three resizable columns: the hierarchy, a compact read-only configuration summary, and the editor for the selected setting. Drag either divider, or focus it and use the arrow keys, to adjust the widths. The summary shows current values, inherited/local/fixed state, validation issues, and run execution status. Select a setting’s linked descriptor to open just that setting on the right. Source links select the defining module or project and keep the same setting open. Save-error links open the corresponding setting directly. Each entity remembers its selected setting while navigating the current draft. Parameter lists, setup outputs, generated-set previews, and execution reports remain grouped as individual items; edits update the summary without discarding drafts. Each column scrolls independently.

Results and setup functions use catalog-name comboboxes. Enable a local override to choose a function; inherited and sealed values remain read-only and retain their source links. Results choices come from `FunctionCatalogs.resultsFunctionCatalog`, and setup choices come from `FunctionCatalogs.setupFunctionCatalog`. Selecting a name changes only the function assignment, leaving data types, query keys, and parameter defaults under your control.

The catalogs are plain immutable maps in `Sortopia.Functions`, keyed by `string<resultsFunctionName>` and `string<setupFunctionName>`. `ResultsFunctionNames` and `SetupFunctionNames` hold the typed name variables. To add an entry, write an independent function and add its name/function pair to the corresponding map in `FunctionCatalogs.fs`.

The initial functions adapt GeneSort's standalone computation to Sortopia's current types. Results functions accept one typed `parameterSet` and a list of `functionData`, returning `Result<functionData list, string>`. Setup functions also receive `ISetupDataContext`: call `GetOrCreate(outputKind, sourceKinds, factory)` separately for each produced datum. The factory runs only when that datum is missing. Declare the source kinds actually used by the factory so their stored identities participate in its key; pure generators use an empty list. There is no registration lifecycle. Each catalog has a simple name-to-version map; bump an entry's version when its computation changes.

### Execute a run

Save the workspace, select a run, and choose **Execute** in its Execution section. Execution uses the saved run's fixed project defaults and local spans, expanding all parameter sets (not just the first 50 shown in the preview). Unsaved changes, input errors, missing saved components, and unknown catalog names disable Execute with an explanation.

Root runs process each results parameter set. Branch runs first prepare every setup parameter set, resolving source projects with `workspace.FindProject` and loading their saved results. After all setup sets succeed, each setup output batch is paired with every results parameter set: 2 setup sets × 3 results sets means 2 setup sets followed by 6 results sets. Matching data is loaded before generation, including when only some setup outputs are cached. Results functions also skip generation when all their outputs already exist. A setup failure prevents all results processing; any other failure stops remaining work and identifies the function and parameter set.

Execution runs off the UI thread and disables editing and duplicate execution. **Cancel operation** stops between function calls; the current synchronous catalog call must return first. Progress counts processed parameter sets. The Execution section retains the last attempt for each run while the workspace is open, including completed setup/results sets and their typed outputs, even after a failure or cancellation. The expandable output summary shows the first 100 sets, with saved/reused indicators and data IDs. Executing again replaces that run's displayed report.

Execute uses `FileRunExecution.execute` in `Sortopia.FileDb`. Published data remains available after closing the app, for subsequent runs and downstream projects. Run definitions are unchanged. `RunExecution.execute` remains available for callers wanting transient execution with an explicit input-data list.

### File-based data storage

`ISortopiaDb` provides `TryLoad`, `Load`, `FindResults`, and `GetOrCreate`. Each project stores immutable data at `data/{setup|results}/<query-id>/<data-id>.msgpack`. Database names are logical identity scopes, not filesystem paths. Payloads use explicit versioned DTOs and MessagePack LZ4 block-array compression; envelopes contain full canonical keys and a SHA-256 integrity checksum. This uses existing dependencies, with no SQL database or separate mutable index.

Each setup output projects its parameter set onto that entry's `QueryParameterKeys`. IDs also include the project ID, database, phase, exact data type/text, function name/version, and declared source-data dependencies. Canonical keys preserve value types, use ordinal key ordering, and are independent of culture and string formatting. Run IDs, execution IDs, and unselected parameters do not participate. Thus varying an unrelated span reuses the same file, including across runs and app restarts. Results use `ResultsQueryParameterKeys` and the corresponding setup output references.

**Query keys are a computation contract:** include every parameter that can vary and change that output. A missing selected key is an error; an empty key list intentionally creates one datum per remaining identity scope. The store cannot infer which parameters an arbitrary function uses. This is projection-based reuse, not global deduplication of identical bytes produced by different projects or different keys.

Source lookup uses the source project's fixed results database, function/version, exact data type/text and **results** query keys, projected from the consumer's setup parameters. Execute source runs first. Missing, corrupt, or ambiguous source results stop execution with an error. If multiple upstream setup inputs yielded the same results query projection, lookup refuses to choose arbitrarily; distinguish those cases through the producer's results parameters/query keys. The current codecs support `SortableTests` (including prefix CEs), `SorterModelSet`, and `SorterSetEval`, with one output per kind for current catalog functions. Other declarations produce a clear unsupported-type error.

Writers coordinate through a project-local file lock and check again after acquiring it, so concurrent creators invoke a shared factory once. Files are flushed to a temporary sibling and published without overwriting existing data. Readers ignore incomplete temporary files. Corruption is reported instead of silently regenerating data. Failure or cancellation retains already published outputs. Each finished attempt also saves a small `executions/<execution-id>.msgpack` receipt containing parameters, output references, reuse flags and status; it does not duplicate payloads. Receipts are not yet loaded into the UI's history. A process crash may leave published data without a receipt, which later executions can still reuse.

The initial codecs read whole objects and limit both files and uncompressed payloads to 256 MiB. This store is intended for a local workspace filesystem. Moving an existing project preserves its IDs and data. Importing a copy assigns new project IDs, so copied historical data retains its original identity and is not reused by the new project; executing the copy creates data in its new scope.

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

When an edit cannot be saved, a summary beside **Save workspace** lists the affected item’s full hierarchy path, the editor section, and specific instructions. Select an issue to select its workspace/module/project/run, open the relevant section, and scroll to it. Missing functions explain which override checkbox to enable and which catalog to choose from. Missing database names direct you to the project. Parameter errors name the key. Invalid names and folders remain editable while the tree retains the last valid value. The disabled Save button also explains its state in a tooltip, including an unavailable folder or no unsaved changes.

Save creates the required folders and writes all definitions transactionally, with the workspace snapshot written last. Failed or canceled operations restore previous definitions and locations. External changes detected since loading or during a save prevent overwriting those changes. Recovery failures are reported explicitly.

The workspace definition is authoritative. Removing an entity keeps its existing files on disk and reports retained items. Changing an editable entity's folder name creates the new folder and retains the old folder and output data; use Cut/Paste to move an existing entity with its contents. Changes made directly on disk are not automatically adopted; import an existing entity to add its actual hierarchy.

The tree distinguishes unsaved items, pending copies/moves, and missing saved definitions. Status refreshes while the page is open and after loading, saving, or discarding. Save can restore missing definitions with the same IDs. A missing workspace root directory must be restored or another location selected first.

Before changing the root folder, returning Home, or closing, unsaved changes prompt for **Save / Discard / Cancel**. Save continues only after success; Discard abandons the draft; Cancel keeps editing. Selecting the same folder or canceling the picker keeps the current draft. Choosing an empty root without editing it does not count as an edit.

## Types and persistence

`Sortopia.Workspaces` defines concrete `rootProject`, `branchProject`, `rootModule`, and `branchModule` types. Their child arrays are strongly typed: for example, `rootProject.Runs` is `rootRun[]`, and `branchModule.Projects` is `branchProject[]`. The `project` and `workspaceModule` discriminated unions provide common views for storage and the editor. Their boundary factories validate type consistency. `workspace.Modules` accepts either `workspaceModule.Root` or `workspaceModule.Branch`. Array properties return copies.

Modules and projects expose their effective shared properties directly and through `Configuration` (`rootProjectConfig` or `branchProjectConfig`). `LocalOverrides` uses `rootDefaultOverrides` or `branchDefaultOverrides`: each property is optional, with `None` meaning inherit and `Some` meaning local, including `Some []` and `Some ""`. `Defaults.resolveRoot` / `resolveBranch` apply local values over the inherited configuration. For parameter defaults, `Defaults.mergeParameterDefaults` replaces matching keys, retains other inherited bindings in order, and appends new local keys. Both `None` and `Some []` inherit all parameter defaults; duplicate keys within a local list are invalid. Other list properties use whole-list replacement. `createInherited` constructs an entity with its parent configuration and local overrides; `rebind` resolves it and its descendants against a new parent. The existing project `create` factory makes every supplied configuration property local; the module `create` factory starts with all properties inherited.

`RootRun.create parent inputs` and `BranchRun.create parent inputs` accept their concrete parent projects through `IRootProject` / `IBranchProject`; the contracts avoid a dependency from runs back to workspaces. Run input records contain Id, RunName, ParentProjectId, DescriptionOverride, and ResultsParameterSpans. Branch inputs also contain SetupFunctionNameOverride and SetupParameterSpans. Both database names come from the project. The parent must be sealed when constructing a runtime run; `project.seal` creates that immutable parent snapshot and `project.withRuns` attaches the validated runs. `RunDto.toDomain` prepares a sealed snapshot without mutating the draft parent. A run retains its inputs in `Configuration` and fixed settings in `ProjectConfiguration`. Branch runs also retain resolved source project snapshots, so queries need no further workspace lookup.

The four shared names have distinct UMX tags: `resultsDatabaseName`, `resultsFunctionName`, `setupDatabaseName`, and `setupFunctionName`. Shared configuration is factored into `resultsConfig` and `setupConfig`. A `setupDataTypeConfig` contains `DataType: sortopiaDataType`, `SourceProjectId: Guid<projectId>`, and `QueryParameterKeys: string<paramKeyName> list`. `setupConfig.DataTypes` is the ordered list; its `SortopiaDataTypes`, `SourceProjectIds`, and `QueryParameterKeys` views derive from those entries. Results query keys remain one list.

`workspace.ProjectIndex` indexes projects globally by ID, and `FindProject` returns an optional project. Workspace creation/loading validates references and resolves branch source snapshots through `BranchRunSources.resolve`. `QueryParams.forSource` uses the source project's ID, results database, and results query keys. `forSetup` uses the parent project's ID, effective setup database, and the selected entry's query keys; `forResults` uses the parent project's results metadata. Query IDs use the stable project ID, exact type/text pair, and selected parameter strings, rather than the display name.

`sortopiaDataType` is a discriminated union with ten cases: `Workspace`, `Run`, `WorkspaceModule`, `RunParameters`, `SorterPoolSet`, `SorterSet`, `SorterModelSet`, `SortableTests`, `SorterSetEval`, and `TextReport`. Every case carries a free-form `string`; empty strings, whitespace, Unicode, and line breaks are preserved. Execution and setup output lists use this type, as do result and setup queries. Each output row has a case selector and a text field. Lists can contain the same case with different text; an identical case/text pair cannot appear twice. Inherited lists show both values and retain their clickable source. Query matching and query IDs include both the case and the exact text.

`parameterBinding` is the general pairing of a `string<paramKeyName>` with a typed `parameterValue`. It can represent a supplied parameter, a default, or an override; the containing property or operation determines its role. `ParameterBinding.create key value` constructs a binding from a typed key, `fromValue name value` accepts an already typed union value, and `fromText name kind text` validates editor or persisted input. All constructors enforce the known key's type and valid values. `ParameterBinding.validateUnique` validates collections with one binding per key. `parameterBindingDto` and `WorkspaceParameterBinding` provide the corresponding storage and editor representations.

`parameterSet` stores a `Map<string<paramKeyName>, parameterBinding>` exposed by `ParamMap`. `parameterSet.fromBindings bindings` creates a set directly; `parameterSet.create map` accepts a typed map and validates that each key matches its binding. `set.WithBinding(binding)` adds or replaces a binding while preserving the original set. `WithValue` accepts a typed key/value or a key name with a `parameterValue`; `None` removes the key. Binding construction, validation, and set updates retain the typed values without formatting or reparsing them.

`set.TryFind(name)` returns a binding. `set.TryFind(typedKey)` reads the underlying CLR value directly, returning `None` for a missing key or mismatched type; numeric-looking text remains text. String conversion is explicit: `TryFindText(name)` formats one value, and `ToStringMap()` projects the whole set to canonical invariant strings. Queries format only their selected keys; DTO serialization formats bindings at the storage boundary. Library IDs remain typed through bindings, parameter sets, and catalog function calls. File-store identity uses their type tags and stable field bytes, so alternate input formatting produces the same data ID.

`ParameterSets.create bindings spans` expands bindings and spans into `parameterSet[]`. It parses span text using the matching binding's type or the known key's type, and stores each candidate as a typed binding. Matching spans replace bindings and other bindings remain fixed. Combinations exceeding the maximum .NET array length are rejected before allocation.

`ParameterSets.preview maxCount bindings spans` shares the same validation and combination generator, materializing only the requested prefix. All candidate values are validated, including values beyond the visible rows. The editor retains typed parameter sets and formats values only for display.

`RunParameterSets.create run` returns a `runParameterSets` record with `Results: parameterSet array` and `Setup: parameterSet array option`. Root runs return `Setup = None`; branch runs return `Some setupSets`. Results and setup combinations are expanded independently from their respective defaults and spans, retaining typed bindings. No spans produces one set of defaults, including one empty set when there are no defaults. The function uses the run's resolved settings and does not need a workspace lookup.

`ResultsParameterDefaults` is a `parameterBinding list` on root/branch modules, projects, and runs. Branch types additionally expose `SetupParameterDefaults`. These properties use bindings as defaults, merging by key through modules and draft projects. Runs receive the sealed project's resolved defaults; their spans replace matching keys. `RootRun.makeResultsParameterSets`, `BranchRun.makeResultsParameterSets`, and `BranchRun.makeSetupParameterSets` use the general binding APIs. `ParamKeys.all` supplies metadata derived from the typed key declarations.

Every entity has a UMX-tagged GUID ID: `workspaceId`, `moduleId`, `projectId`, or `runId`. Its definition filename is `<id>.msgpack`. IDs survive edits, moves, and missing-file restoration. Folder names exist only on workspaces, modules, and projects. The absolute workspace location stays in application settings, outside the definitions.

Names must be nonblank, and sibling display names must be distinct ignoring case. Sibling module/project folder names must also be distinct ignoring case. Runs in a project require distinct IDs because their files share one directory. Rejected edits remain visible for correction while the tree keeps the last accepted value; Save is disabled until corrected. Saved names and folder names stay reserved until their removal or rename has been saved.

`Sortopia.Workspaces.Serialization` writes schema version 11 MessagePack DTOs. Library parameter values use distinct type tags and canonical identifier text at this storage boundary; they are parsed back into typed records on load. Data types are `{ kind, value }` pairs. Setup entries are `{ dataType, sourceProjectId, queryParameterKeys }`. Projects persist `isSealed`, effective configuration, local overrides, and their runs. Modules retain database-free local overrides and effective snapshots in their own module settings DTO. Project database names are stored locally and remain fixed after sealing. For shared settings, null override fields inherit and empty arrays are explicit empty lists. Draft projects resolve against the actual hierarchy; sealed projects load their fixed configuration snapshots. Run files contain IDs, description/setup-function overrides, and spans; they contain no database override. `WorkspaceCodec.decodeRunById workspace bytes` finds the stored ParentProjectId, verifies a sealed parent of the correct family, and resolves source snapshots; `decodeRun parent bytes` supports callers already holding a sealed parent. Cross-project validation occurs after the hierarchy loads. Unknown or older schema versions are rejected, with no legacy migration. Each container definition includes its descendants.

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
