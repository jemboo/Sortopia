# Sortopia

Sortopia has F# libraries targeting .NET 10 and a C# WPF desktop app for Windows. The migration covers the retained Core, Sorting and sorting-catalog libraries and their tests. Open `Sortopia.sln` in Visual Studio with F# and .NET 10 support, or run these commands from the solution directory.

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

## Choosing a project

| Project | What belongs here |
| --- | --- |
| Sortopia.WPF | Windows desktop shell, MVVM page navigation, sorting-network visualization and trace playback. |
| Sortopia.Core | Arrays, collections, stable IDs, random sources, sampling, combinatorics and computation builders. Core has no project references. |
| Sortopia.Permutations | Permutations, involutions, orbits, merge lattices, unfolders and their generation and mutation rates. These types refer to one another and share mathematical invariants. |
| Sortopia.Sorting | Sorting networks, canonical stages, scalar and SIMD input datasets, generation and partitioning. |
| Sortopia.Sorting.Catalog | Known sorting networks, prefixes, merge-input constructions and validated catalog identifiers. |
| Sortopia.Sorting.Serialization | Explicit MessagePack contracts and validation for networks and all six input representations. |
| Sortopia.Reporting | Tabular records, rank labels and report construction. Reference it when producing or consuming reports. |
| Sortopia.Core.Serialization | MessagePack options and the RNG factory wire contract. |
| Sortopia.Permutations.Serialization | DTOs and conversions for permutations, unfolders and rate models. |

The three evaluators in GeneSort.SortingOps each defined an array-content comparer for deduplication. Their shared operation now has a home in `Sortopia.Core.ArrayContentComparer<'T>`. When migrating an evaluator, use this comparer for its array keys. The key's contents must stay fixed while it is in the collection.

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
| GeneSort.Model.Sortable, Model.Sorting, SortingOps and Eval | Port domain models and algorithms after their inputs. Keep compare-exchange conventions, SIMD lane counts and evaluation metrics with the evaluation code. |
| GeneSort.Project, FileDb, Dispatch and WPF | Port after the domain types and DTOs they compose. FileDb is the boundary for validating existing saved projects and run histories against real files. |

The eight test projects follow the library boundaries and cover the APIs retained for Dispatch. The solution builds from its own source tree. Port callers by changing their project references and opening the namespace that owns each type. The .NET types in the two solutions have distinct assembly identities.
