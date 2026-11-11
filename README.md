# Sortopia

Sortopia has F# domain, serialization, and persistence libraries targeting .NET 10, plus a C# WPF desktop app for Windows. The migration covers the retained Core, Sorting, sorting-catalog, model and evaluation libraries and their tests. Open `Sortopia.sln` in Visual Studio with F# and .NET 10 support, or run these commands from the solution directory.

```powershell
dotnet build Sortopia.sln -c Release
dotnet test --solution Sortopia.sln -c Release --no-build
```

## Running the desktop app

In Visual Studio, set `Sortopia.WPF` as the startup project and press F5. From the solution directory, run the app with the following command.

```powershell
dotnet run --project src/Sortopia.WPF/Sortopia.WPF.csproj
```

The home page displays the Sortopia fisherman artwork with navigation buttons on the left. Choose **Workspace** to set your Workspace folder, then **Browse…** to select it. The selected path is saved automatically in `%LOCALAPPDATA%/Sortopia/workspace-folder.txt` and restored on the next launch. Existing saved folder choices are retained after the rename. Choosing a folder keeps you on the Workspace page; use **Back to Sortopia** to return home. The image scales proportionally with the window, and the Sortopia icon is embedded in the window and executable. Both assets are packaged with the app.

The Workspace page edits modules, projects, and runs, saves them in typed folders, and imports existing modules/projects from disk. Save or discard edits with the controls beside the workspace name. See [workspace hierarchy and saving](docs/workspaces.md) for the layout and workflow.

The WPF shell uses MVVM. `MainWindowViewModel.CurrentPage` selects a page through typed XAML data templates. `HomePageViewModel` and `WorkspacePageViewModel` expose navigation commands; their views contain no navigation logic. The shell retains the page view models when switching pages. Shared appearance lives in `Styles/WorkspaceTheme.xaml`.

`src/Sortopia.WPF/Views` and `ViewModels` follow the same Pages → SubPages → Controls hierarchy. Top-level pages own their subpages, and reusable controls live in a shared location:

| Folder | Responsibility |
| --- | --- |
| `Pages/Home` | Home page and navigation commands. |
| `Pages/Networks`, `Pages/RandomNetworks` | Separate top-level pages for catalog playback and random networks. |
| `Pages/Workspace` | Workspace hierarchy editor, saved folder setting, and module/project imports. |
| `Pages/Sgd` | SGD page, workflow navigation, and operation coordination. |
| `Pages/Sgd/SubPages` | Setup, Sampling, and Evolution subpages owned by the SGD page. |
| `Shared/Controls` | Reusable results pane, graph, network diagram, parameter editor, layout selector, generation inputs, and navigation control. The matching view-model folder holds shared playback, results, annotations, history, and editor settings. |

The shell is `Views/MainWindow.xaml`, paired with `ViewModels/MainWindowViewModel.cs`; `ViewModelBase` remains at the view-model root. Namespaces follow the folder paths, such as `Sortopia.WPF.Views.Pages.Sgd.SubPages` and `Sortopia.WPF.ViewModels.Pages.Sgd.SubPages`. Controls use `Sortopia.WPF.Views.Shared.Controls`, with their supporting view models in `Sortopia.WPF.ViewModels.Shared.Controls`. Keep both files of each XAML/code-behind pair together. Add each new subpage under its owning page's `SubPages` folder, and place reusable controls in `Shared/Controls`.

## Exploring sorting networks

On the home page, choose **Sorting networks**. Select any of the 55 catalog entries, choose shuffled or reversed input, and play, pause, step or scrub through its execution. Switch between comparator and stage steps. Zoom and scroll larger networks; **Follow** keeps the animated values in view horizontally. Leaving the page pauses playback while retaining its selection and position.

Sorting is computed once when the network or input changes. Rendering and animation read immutable snapshots, recorded swap counts and token positions. See [history APIs and viewer architecture](docs/network-viewer.md), including the Archean code considered for the visual design.

Choose **Random Networks** for the separate random-generation and testing page. It supports `msce`, `msrs`, `mssi`, `msuf4` and `msuf6`, filters channel choices by method, and accepts CE count for `msce` or stage count for the other methods. Binary tests load failing inputs for playback; results distinguish exhaustive verification from sampling. See [random network controls and testing](docs/network-viewer.md#random-networks).

## Choosing a project

| Project | What belongs here |
| --- | --- |
| Sortopia.Runs | Run configurations, output categories, parameter generation and result-query definitions. |
| Sortopia.Workspaces | Workspaces with root/branch modules and projects containing matching run types. |
| Sortopia.Workspaces.Serialization | Versioned MessagePack definitions for workspaces, modules, projects and runs. |
| Sortopia.Workspaces.Persistence | Workspace file discovery, loading, transactional saves, imports and moves; a .NET library with no WPF dependency. |
| Sortopia.Functions | Independent results/setup function catalogs and the run execution loop. |
| Sortopia.FileDb | Project-local compressed MessagePack data, stable typed query IDs, source resolution and persistent run execution. |
| Sortopia.WPF | Windows desktop shell, MVVM page navigation, sorting-network visualization and trace playback. |
| Sortopia.Core | Arrays, collections, stable IDs, random sources, sampling, combinatorics and computation builders. Core has no project references. |
| Sortopia.Permutations | Permutations, involutions, orbits, merge lattices, unfolders and their generation and mutation rates. These types refer to one another and share mathematical invariants. |
| Sortopia.Sorting | Sorting networks, canonical stages, scalar and SIMD input datasets, generation and partitioning. |
| Sortopia.Sorting.Catalog | Known sorting networks, prefixes, merge-input constructions and validated catalog identifiers. |
| Sortopia.Sorting.Serialization | Explicit MessagePack contracts and validation for networks and all six input representations. |
| Sortopia.Models.Sorting | Sorting-model representations, generators, mutators, activation and model sets. |
| Sortopia.Models.Inputs | Full, orbit, merge and prefix-reduced input-test models and partitioning. |
| Sortopia.Models.Sorting.Serialization | Model, generator, mutator and model-set DTO conversion. |
| Sortopia.Evaluation | Scalar, packed and SIMD network evaluation, CE usage, result versions, scoring and set evaluation. |
| Sortopia.Evaluation.Serialization | Exact evaluation DTOs and MessagePack conversion. |
| Sortopia.Reporting | Tabular records, rank labels and report construction. Reference it when producing or consuming reports. |
| Sortopia.Core.Serialization | MessagePack options and the RNG factory wire contract. |
| Sortopia.Permutations.Serialization | DTOs and conversions for permutations, unfolders and rate models. |

The SIMD evaluators share `Sortopia.Core.ArrayContentComparer<'T>` for deduplication. The key's contents must stay fixed while it is in the collection.

## Value equality and persistent IDs

Rate records use F# structural equality in dictionaries and sets. Rate arrays and permutations snapshot their inputs and return copies from their array properties. Use indexed access when reading individual elements in a loop.

For a reproducible model ID, pass the model discriminator and all identifying fields to `GuidUtils.guidFromObjs`. For example, `GuidUtils.guidFromObjs [box "example/v1"; box rates; box flag]` writes the actual rate content. Each rate writes its type tag and ordered fields, arrays include their length, and signed zeros share one encoding. Collection hash codes are process-local implementation details. Permutation IDs use the first 16 bytes of SHA-256 over little-endian Int32 elements, interpreted in .NET Guid byte order.

## Persisting values

Use `fromDomain` to create a DTO, then encode it with `MessagePackSetup.serialize`. On read, choose the DTO type explicitly and call its `toDomain` function. For example, a consumer referencing `Sortopia.Core.Serialization` can round-trip a deterministic RNG factory as follows.

```fsharp
open Sortopia.Core
open Sortopia.Core.Serialization

let bytes = rngFactory.SmxFactory |> RngFactoryDto.fromDomain |> MessagePackSetup.serialize
let factory = MessagePackSetup.deserialize<rngFactoryDto> bytes |> RngFactoryDto.toDomain
```

The serializer supplies its resolver and security options per call. Applications can use their own MessagePack settings alongside Sortopia. RNG factory IDs, algorithm numbers and DTO field names are persisted data. Preserve them when renaming domain types. A factory payload identifies the generator algorithm. It does not capture the state of a running generator.

The serialization test projects include bytes written by GeneSort.Core.Mp for the Smx factory and the permutation `[2; 0; 1]`. Tests read those bytes, check the domain value and compare the new encoding byte for byte. They also exercise invalid domain payloads. The sorting migration introduces a new wire contract with explicit numeric keys and a versioned dataset envelope. There is no legacy sorting-payload reader. See [sorting contracts and migration notes](docs/sorting.md) for the field layouts and validation rules.

## Continuing the GeneSort migration

| Legacy area | Migration decision |
| --- | --- |
| GeneSort.Core and its test project | Shared utilities go to Core. Permutation algorithms and their tests move together to Permutations. Report types move to Reporting. The old console entry point ran a report against a hard-coded local directory. That example, the unused SIMD sandbox and uncompiled draft files remain in GeneSort. |
| GeneSort.Core.Mp and its test project | The two Serialization libraries own DTO conversion. Use explicit options in place of the old global `configure()` call. The legacy Uf4/Uf6 model DTO namespaces are consolidated under `Sortopia.Permutations.Serialization.RatesAndOps`. |
| GeneSort.Sorting, Sorting.Mp and their tests | Migrated into Sorting and Sorting.Serialization, retaining the production dependency surface used by Dispatch.V1. Networks and inputs have separate namespaces. Unused report projections, console experiments, bitonic helpers, alternate packing APIs and SIMD golden hashes are omitted. |
| GeneSort.SortingLib | Migrated into Sorting.Catalog with all 49 networks and 10 prefixes, strict parsers, validated keys and versioned stable identities. See [catalog contracts and usage](docs/sorting-catalog.md). |
| GeneSort.Model.Sortable, Model.Sorting and Model.Sorting.Mp | Migrated into Models.Inputs, Models.Sorting and Models.Sorting.Serialization, with consolidated model tests. See [model contracts and migration notes](docs/models.md) for identity changes and validation fixes. The empty sortable-serialization stub is omitted. |
| GeneSort.SortingOps and SortingOps.Mp | Migrated into Evaluation and Evaluation.Serialization with consolidated tests, exact CE usage DTOs, scalar/SIMD count parity, immutable result snapshots and deterministic sequence fingerprints. See [evaluation contracts and migration notes](docs/evaluation.md). |
| GeneSort.Eval | Migrate its higher-level evaluation orchestration after checking callers against the evaluation contracts. |
| GeneSort.Project, FileDb, Dispatch and WPF | Sortopia has its own workspace hierarchy, function catalogs, file-based execution store and WPF editors. Existing GeneSort projects and histories are not imported. See [workspaces](docs/workspaces.md). |

The test projects follow the library boundaries and cover the APIs retained for Dispatch. `Sortopia.Workspaces.Persistence.Tests` exercises workspace storage without loading WPF; UI workflows remain covered by `Sortopia.WPF.Tests`. The solution builds from its own source tree. Port callers by changing their project references and opening the namespace that owns each type. The .NET types in the two solutions have distinct assembly identities.
