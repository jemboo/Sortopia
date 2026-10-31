# Model libraries

The GeneSort model migration separates model definitions from generated networks, input datasets and serialization.

| Legacy project | Sortopia destination |
| --- | --- |
| GeneSort.Model.Sorting.V1 | `Sortopia.Models.Sorting` |
| GeneSort.Model.Sortable.V1 | `Sortopia.Models.Inputs` |
| GeneSort.Model.Sorting.Mp.V1 | `Sortopia.Models.Sorting.Serialization` |
| GeneSort.Model.Test, Model.Test.V1, Model.Mp.Test | `Sortopia.Models.Tests` |
| GeneSort.Model.Sortable.Mp.V1 | No library: the original contains only a console greeting and no DTOs. |

The model libraries are ordinary libraries, with no console entry points. The commented-out orbit generator and placeholder `Assert.True(true)` test are omitted. Test-only unfolder builders live in the tests. The mixed `Sorting.Simple.V1` and `Sorting.V1.Simple` namespaces are consolidated under `Sortopia.Models.Sorting.Simple`. Serialization depends on sorting models and permutation serialization; it does not need the sorting-dataset serialization assembly.

## Retained functionality

Sorting models retain CE, self-inverse permutation, reflection-symmetric permutation, Uf4 and Uf6 representations, their generators and mutators, model sets and the Simple/SimpleR/SimpleS unions. SimpleR uses bounded signed activators; SimpleS uses boolean activators. Inactive entries are filtered before CE conversion, preserving support for dormant entries that cannot themselves be expanded. Models can retain their original IDs, and model sets may contain different widths. Duplicate IDs are rejected rather than silently overwriting entries.

Input models retain full binary datasets, permutation orbits, merge inputs and prefix-reduced inputs, including their original supported scalar/SIMD formats and partitioning entry point. Unsupported format combinations remain explicit failures; this migration does not add a sortable-model DTO contract.

## Corrections

- All five sorting model array containers copy their input arrays and return copies. Unfolder steps and Uf4/Uf6 step arrays also own their contents, so nested changes cannot mutate a model through its dependencies.
- Uf4/Uf6 equality now includes the unfolder steps, matching the existing content-based hash code.
- `msasPfx.Equals(obj)` compares the correct type. Prefix construction rejects unavailable catalog entries.
- Merge-input equality and hashing include width, dimension, suffix and library variant. Its component sorter key uses the segment width, and `CeArray` expands that component over all segments.
- Merge dimensions are checked before division. Orbit limits must be positive. Full-input generation rejects a width different from its model. Self-inverse permutation entries must match the model width. CE generation rejects excluding self-comparisons at width one. Rank-swap generators require a positive stage length. Model sets reject negative CE limits and duplicate IDs.
- `SorterModelType.Unknown` now round-trips through its text representation.

## IDs and saved data

Affected generator and mutator identities now serialize the actual rate records using `IStableSerializable`; they no longer use process-local `GetHashCode()` values as persistent identity. Every simple mutator ID includes its variant. The affected discriminators have the form `Sortopia.Models.Sorting/<type>/v1`.

This deliberately changes the derived IDs and therefore random sequences for affected generators and mutators. Replaying a legacy configuration in Sortopia is deterministic within Sortopia but is not a promise of the same legacy random stream. Stored concrete model IDs are retained by DTO conversion. Do not resume a GeneSort evolutionary run across this boundary without an explicit history migration.

Existing DTO record fields, model union case order and numeric model-set keys are retained. Use `fromDomain`, `MessagePackSetup.serialize`, `MessagePackSetup.deserialize<...>` and `toDomain`; conversion invokes the domain constructors. No global serializer configuration is needed. Some malformed legacy data accepted by the old constructors is now rejected. Byte-for-byte compatibility of every legacy model payload has not been certified; FileDb migration remains the place to validate real saved projects.

## Verification

The consolidated suite retains the 110 substantive model tests and adds regressions for array ownership, nested unfolder equality, rate-content identity, mutator variants, shared-serializer round-trips, input-model equality, merge expansion, invalid shapes and duplicate IDs. An old serialization fixture used a four-channel permutation in a sixteen-channel model; it now uses a consistent width. A mutation test now checks for changes over a deterministic sample rather than assuming every random mutation changes its input.

Run the solution's normal build and test commands from the repository root. On hosts where the `dotnet test` named-pipe handshake is unavailable, each built test executable can also be run directly, for example:

```powershell
dotnet tests/Sortopia.Models.Tests/bin/Release/net10.0/Sortopia.Models.Tests.dll --progress off
```
