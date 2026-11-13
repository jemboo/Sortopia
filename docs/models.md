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

Input models retain full binary datasets, permutation orbits, merge inputs and prefix-reduced inputs, including their original supported scalar/SIMD formats and partitioning entry point. The prefix-input model returns its reduced dataset directly in Boolean or BitVector512 form. Unsupported format combinations remain explicit failures; this migration does not add a sortable-model DTO contract.

## Configurable generation

Each generator module in `Sortopia.Models.Sorting.Simple` has its own typed initializer, shared by SGD and non-UI callers. All take an RNG factory, channel count, fixed prefix, and suffix stage length, followed by the options specific to that model:

- `MsceRandGen.create`: an `excludeSelfCe` flag.
- `MssiRandGen.create`: no additional probability settings.
- `MsrsRandGen.create`: one `opsGenRates` value for Ortho, Para, and SelfReflection probabilities.
- `Msuf4RandGen.create`: an `opsGenRates` seed and an `opsGenRates list` for the unfolding levels.
- `Msuf6RandGen.create`: a `seed6GenRates` seed (Ortho 1–2, Para 1–4, SelfReflection) and an `opsGenRates list` for the unfolding levels.

Each initializer returns its concrete generator type. A single probability configuration is reused unchanged in every suffix stage. UF4 and UF6 keep separate controls for each unfolding level *within* a stage; their `unfoldingWidths` functions describe those levels in generation order. For example, UF6 at width 24 needs levels 6 → 12 and 12 → 24, regardless of the number of stages.

Use `opsGenRates.create` and `seed6GenRates.create` for normalized probabilities. For relative weights, `OpsGenerationProbabilities.fromWeights ortho para selfReflection` and `Seed6GenerationProbabilities.fromWeights ortho1 ortho2 para1 para2 para3 para4 selfReflection` normalize named modes independently for each distribution. Zero excludes a mode. Weights must be finite and nonnegative, with at least one positive value; finite weights whose sum would overflow are supported. The initializers validate typed probability values, unfolding-level counts, channel counts, prefixes, positive suffix stage lengths, and suffix-size overflow before generation.

For example, this generates UF6 suffixes with Para 4 seeds, an entirely para first unfolding, and an entirely ortho second unfolding:

```fsharp
let seed = seed6GenRates.create(0., 0., 0., 0., 0., 1., 0.)
let firstUnfolding = opsGenRates.create(0., 1., 0.)
let secondUnfolding = opsGenRates.create(1., 0., 0.)
let generator =
    Msuf6RandGen.create rngFactory.SmxFactory 24<channelCount> [||]
        5<stageLength> seed [firstUnfolding; secondUnfolding]
let model = generator.MakeSorterModelFromIndex (UMX.tag 0)
```

All five stages use that same seed and pair of unfolding distributions. RNG choice, suffix stages, and fixed prefixes work across all five representations. Wrap a concrete generator in its `simpleSorterModelGen` case to use it with `simpleSorterModelRGen.create` or `simpleSorterModelSGen.create`. `SimpleSorterModelGen.makeUniform` remains a convenience dispatcher to the model-specific initializers with equal probabilities; existing valid uniform configurations retain their generator IDs and seeded output.

SGD's `SimpleGenerationSettings` uses typed OPS and six-channel seed editors, preserves existing unfolding controls when width changes, and calls the appropriate model-specific initializer. Weight normalization lives in the model library. Its sliders, RNG labels, prefix picker, seeded replay, and sampling snapshots retain their existing behavior. Invalid weights or RNG selections disable sampling. The UI explicitly labels the controls as weights shared by every stage.

## Fixed prefixes

Each concrete simple model (`msce`, `mssi`, `msrs`, `msuf4`, `msuf6`) and generator owns a `prefix: ce array`. Constructors take it immediately after the channel count, for example `msce.create id channelCount prefix ceCodes` and `SimpleSorterModelGen.makeUniform rng channelCount prefix stages modelType excludeSelfCe`. Empty prefixes are allowed; null arrays and endpoints outside the model width are rejected. Inputs and exposed `Prefix` arrays are copied. Prefix contents participate in model and generator equality. SimpleR/S and their generators expose the underlying prefix without storing another copy.

`MakeSorterWithoutPrefix maxCeLength` expands the suffix, filters inactive model entries where applicable, and limits the resulting suffix CEs. `MakeSorterWithPrefix maxCeLength` prepends the complete fixed prefix to that same limited suffix. The prefix is never truncated or filtered by activators. `None` takes the whole suffix, `Some 0` takes none, and negative limits are rejected. Model `CeLength`, `StageLength`, and underlying entry counts continue to describe the suffix representation; they are not complete-network property evaluations.

The shared model modules and `SorterModel` expose corresponding `makeSorterWithoutPrefix` and `makeSorterWithPrefix` functions. Model sets have `makeSorterSetWithoutPrefix` and `makeSorterSetWithPrefix`. Every mutation path inherits the parent's prefix unchanged, including insertion/deletion and activated-model mutations. Mutators do not have their own prefix configuration.

Use the suffix form only when the input dataset has already been sorted by the identical ordered prefix. When passing several suffix sorters to one evaluation batch, they must share that prefix and input width. Dataset values alone cannot prove which prefix was applied, so the caller must retain that provenance separately. Pass the prefix to the evaluator as already-applied metadata; do not apply the full sorter again to those presorted inputs. Deduplicating presorted inputs preserves sorting correctness but changes input multiplicities and therefore aggregate failure and usage counts.

Use the full form for complete-sorter properties and fingerprints. Compute stage depth from the combined CE sequence, rather than adding prefix and suffix stage counts. `RandomNetwork.toStagesWithPrefix` retains the prefix before the suffix's native stages; `toStagesWithoutPrefix` exposes only those suffix stages. Search then compacts the combined network for scoring, keeps all fixed-prefix CEs even when unused, and removes only unused suffix CEs.

Sampling and Evolution expose a prefix selector with `None` and the catalog entries for the current channel count. Sampling snapshots the selection into its generator. Evolution inherits the sampled prefix; changing it between runs retains the suffix, derives a new model ID through `SimpleSorterModel.withPrefix`, reevaluates the parent, and resets the generation count. Cancellation retains the previous parent and selection. Mutation itself continues to inherit the prefix unchanged.

Both pages prepare one input context per run and reuse it for every `MakeSorterWithoutPrefix` evaluation. With a catalog prefix and `all`, scalar checks use `msasPfx.MakeSortableBoolTest` and SIMD checks use `msasPfx.MakeSortableBitv512Test`. Custom prefixes use the same distinct-output generator. Sampled inputs are presorted and deduplicated once; the original requested draw count is retained separately so Evolution recreates the same context. Each distinct output contributes once to failure and suffix CE-use counts, without multiplicity weighting. Prefix CE-use slots are unmeasured placeholders; fixed-prefix markers retain those CEs for display and sorter properties. These pages request counts only and do not retain counterexamples. Prepared inputs reject a mismatched model width, prefix, or requested sample coverage.

Display networks are constructed from `MakeSorterWithPrefix`; full-network compaction supplies stage counts, CE counts, and fingerprints. Usage and fixed-prefix markers are remapped when independent CEs change order, so used-CE and compact views retain the prefix even where its swap count is zero. Self-inverse model expansion treats fixed points as idle channels, including odd channel counts.

## Corrections

- All five sorting model array containers copy their input arrays and return copies. Unfolder steps and Uf4/Uf6 step arrays also own their contents, so nested changes cannot mutate a model through its dependencies.
- Uf4/Uf6 equality now includes the unfolder steps, matching the existing content-based hash code.
- `msasPfx.Equals(obj)` compares the correct type. Prefix construction rejects unavailable catalog entries.
- Merge-input equality and hashing include width, dimension, suffix and library variant. Its component sorter key uses the segment width, and `CeArray` expands that component over all segments.
- Merge dimensions are checked before division. Orbit limits must be positive. Full-input generation rejects a width different from its model. Self-inverse permutation entries must match the model width. CE generation rejects excluding self-comparisons at width one. Rank-swap generators require a positive stage length. Model sets reject negative CE limits and duplicate IDs.
- `SorterModelType.Unknown` now round-trips through its text representation.

## IDs and saved data

Affected generator and mutator identities now serialize the actual rate records using `IStableSerializable`; they no longer use process-local `GetHashCode()` values as persistent identity. Every simple mutator ID includes its variant. The affected discriminators have the form `Sortopia.Models.Sorting/<type>/v1`.

Simple generator IDs now use `Sortopia.Models.Sorting/<type>/v2` and include the prefix length followed by its ordered canonical CE codes. This changes generator IDs even for empty prefixes. Index-generated model IDs and their random streams consequently change too. Generation from an explicitly supplied model ID still seeds the suffix from that supplied ID. Generator DTOs recompute the current ID from their configuration, including DTOs that carry an older saved generator ID.

Materialized sorter IDs use `Sortopia.Models.Sorting/materializedSorter/v1`, the source model ID, width, prefix, form, and actual limited suffix. Full and suffix forms with nonempty prefixes have distinct IDs; limits producing identical suffixes produce identical IDs, and empty-prefix forms have the same ID. These IDs are no longer casts of model IDs. Keep the source model association when joining evaluation and full-sorter property results; do not cast a materialized sorter ID back to a model ID.

This deliberately changes the derived IDs and therefore random sequences for affected generators and mutators. Replaying a legacy configuration in Sortopia is deterministic within Sortopia but is not a promise of the same legacy random stream. Stored concrete model IDs are retained by DTO conversion. Do not resume a GeneSort evolutionary run across this boundary without an explicit history migration.

Existing DTO record fields, model union case order and numeric model-set keys are retained. Use `fromDomain`, `MessagePackSetup.serialize`, `MessagePackSetup.deserialize<...>` and `toDomain`; conversion invokes the domain constructors. No global serializer configuration is needed. Some malformed legacy data accepted by the old constructors is now rejected. Byte-for-byte compatibility of every legacy model payload has not been certified; FileDb migration remains the place to validate real saved projects.

Model and generator DTOs add `prefixCeCodes: int array`. Missing legacy fields decode as an empty prefix; populated fields are decoded and validated by the domain constructors. This does not couple sortable datasets with CE arrays or introduce a new dataset DTO. Regression tests cover reading the preceding CE model/generator record shapes, prefix round trips for all five families and activated wrappers, and scalar/SIMD presorted evaluation equivalence.

## Verification

The consolidated suite retains the 110 substantive model tests and adds regressions for array ownership, nested unfolder equality, rate-content identity, mutator variants, shared-serializer round-trips, input-model equality, merge expansion, invalid shapes and duplicate IDs. An old serialization fixture used a four-channel permutation in a sixteen-channel model; it now uses a consistent width. A mutation test now checks for changes over a deterministic sample rather than assuming every random mutation changes its input.

Run the solution's normal build and test commands from the repository root. On hosts where the `dotnet test` named-pipe handshake is unavailable, each built test executable can also be run directly, for example:

```powershell
dotnet tests/Sortopia.Models.Tests/bin/Release/net10.0/Sortopia.Models.Tests.dll --progress off
```
