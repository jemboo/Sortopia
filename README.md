# Sortopia

Sortopia has F# libraries targeting .NET 10 and a C# WPF desktop app for Windows. The migration covers the retained Core, Sorting, sorting-catalog, model and evaluation libraries and their tests. Open `Sortopia.sln` in Visual Studio with F# and .NET 10 support, or run these commands from the solution directory.

```powershell
dotnet build Sortopia.sln -c Release
dotnet test --solution Sortopia.sln -c Release --no-build
```

## Running the desktop app

In Visual Studio, set `Sortopia.WPF` as the startup project and press F5. From the solution directory, run the app with the following command.

```powershell
dotnet run --project src/Sortopia.WPF/Sortopia.WPF.csproj
```

The home page displays the Sortopia fisherman artwork with a column of translucent navigation buttons on the left. Hover over the button column or press Tab to focus it and reveal its buttons. Tab again to move through the buttons. They stay visible while the pointer or keyboard focus is inside the column and hide when both leave. Choose **Sandbox** to open the Sandbox page and **Back to Sortopia** to return. Both pages use the same reveal behavior. The image scales proportionally with the window, and the Sortopia icon is embedded in the window and executable. Both assets are packaged with the app.

The WPF shell uses MVVM. `MainWindowViewModel.CurrentPage` selects a page through typed XAML data templates. `HomePageViewModel` and `SandboxPageViewModel` expose navigation commands; their views contain no navigation logic. The shell retains the page view models when switching pages, so future page state can survive navigation. `Controls/TranslucentButtonStack` is a reusable UserControl: place command-bound `Button` elements inside it to populate its `Buttons` collection. It provides vertical layout, spacing, shared translucent styling, and hover/focus visibility without changing the page data context. The transparent group remains hit-testable and tabbable while its buttons are hidden, and Tab/Shift+Tab can leave the group normally. Shared button appearance lives in `Styles/Navigation.xaml`.

## Exploring sorting networks

On the home page, reveal the navigation buttons and choose **Sorting networks**. Select any of the 55 catalog entries, choose shuffled or reversed input, and play, pause, step or scrub through its execution. Switch between comparator and stage steps. Zoom and scroll larger networks; **Follow** keeps the animated values in view horizontally. Leaving the page pauses playback while retaining its selection and position.

Sorting is computed once when the network or input changes. Rendering and animation read immutable snapshots, recorded swap counts and token positions. See [history APIs and viewer architecture](docs/network-viewer.md), including the Archean code considered for the visual design.

Choose **Random Networks** for the separate random-generation and testing page. It supports `msce`, `msrs`, `mssi`, `msuf4` and `msuf6`, filters channel choices by method, and accepts CE count for `msce` or stage count for the other methods. Binary tests load failing inputs for playback; results distinguish exhaustive verification from sampling. See [random network controls and testing](docs/network-viewer.md#random-networks).

## Project runs

Choose **Open Project Folder** from Home and select the folder for your experiment. Sortopia opens **Project runs** for that folder. On the **Sortable tests** tab, choose the datasets to generate. Name the run and add one row for each dataset you want. A merge takes a channel count and a number of equal segments and produces byte SIMD tests (512-bit vectors). A prefix takes a catalog network and produces its residual binary tests (512-bit vectors). Choose **Run** to save the datasets together.

In **Manifest designer**, choose a completed run, select outputs, enter their slot indices, name the manifest and publish. Publishing creates a new manifest ID. Multiple manifests can refer to the same saved dataset. The optional **All outputs** manifest assigns consecutive indices in run order. Each run stores its inventory and datasets under `<project>/runs/<run-id>/` and its manifests in the adjacent `manifests/` subfolder. Reopen the project folder to see its saved runs.

On **Sorter model set**, enter the even channel count, stages, number of models, and first model index. **Generate set and manifest** saves one reproducible set of mssi models and publishes it at slot 0. Change the first index to generate a different range of models. Use **Manifest designer** to publish a different selection or index.

On **Sorter evaluation**, select a model manifest slot and a sortable tests manifest slot from this project, then choose **Evaluate models**. Each model runs against every saved input. Prefix-generated tests already contain the residual inputs, so the prefix contributes to the V2 metrics and only the generated model executes. The result inventory records both source run IDs, manifest IDs and indices, along with the pass count and smallest failure count. The result manifest points to the full per-model V2 evaluations.

## Choosing a project

| Project | What belongs here |
| --- | --- |
| Sortopia.Runs | F# run library. |
| Sortopia.Runs_bak | Direct sortable test generation, completed run inventories and indexed output manifests. |
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

### Sortable-tests DirectRun

Open a project folder in Sortopia and select **Sortable tests**. Give the run a name and add a merge or prefix dataset for each parameter set you want to execute.

1. **Save definition** writes `runs/<runId>/run.json` with the dataset recipes and their parameter IDs.
2. Select a saved definition and choose **Generate runParameter files**. Each dataset gets a JSON string map in `parameters/<parameterId>.json`. You can inspect these files and reopen the project between steps.
3. **Execute saved parameters** reads each parameter file and writes its `SortableTestsWithPrefix` Result under `results/SortableTestsWithPrefix/<parameterId>/<dataId>.mpk` in the same run folder. A retry reuses saved Results. Completion makes the run available in the manifest designer.

**Save and run** performs the three steps in sequence. The corresponding `RunLibrary` methods are `SaveSortableTestsDefinition`, `GenerateSortableTestsParameters`, and `ExecuteSortableTests`.

### Project files

`Sortopia.Storage.ProjectFileStore(projectFolder)` opens the project's `runs` folder. Use `SaveParameters` with a run ID, parameter ID and string map. Pass the same parameter reference to `SaveResult` with a typed payload. The returned `ResultRef` identifies the producer, output type and data ID. Each write creates a new file and rejects an existing address.

On reopening the project, `ListParameters(runId)` and `ListResults(runId)` enumerate the saved files. Pass a reference to `LoadParameters` or `LoadResult` to read it directly. Parameters use JSON and Results use the corresponding MessagePack DTO.

All sortable-test outputs use `SortableTestsWithPrefix(tests, prefix)`. The prefix is the ordered array of CEs already applied to the inputs. Use `[||]` when no prefix was applied.

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
| GeneSort.Project, FileDb, Dispatch and WPF | Port after the domain types and DTOs they compose. FileDb is the boundary for validating existing saved projects and run histories against real files. |

The ten test projects follow the library boundaries and cover the APIs retained for Dispatch. The solution builds from its own source tree. Port callers by changing their project references and opening the namespace that owns each type. The .NET types in the two solutions have distinct assembly identities.
