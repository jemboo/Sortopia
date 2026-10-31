module Sortopia.Models.Tests.Migration

open System
open FSharp.UMX
open Xunit
open Sortopia.Core
open Sortopia.Core.Serialization
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open Sortopia.Sorting.Networks
open Sortopia.Models.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6
open Sortopia.Models.Sorting.Serialization
open Sortopia.Models.Sorting.Serialization.Simple.Ce

let private modelId = UMX.tag<sorterModelId> (Guid.Parse "904bc889-6df3-4d9c-b3dc-c0fc7912d97a")

[<Fact>]
let model_arrays_are_owned_and_cannot_invalidate_activator_lengths () =
    let codes = [|1; 3|]
    let model = msce.create modelId 4<channelCount> codes
    let activated = simpleSorterModelS.create (simpleSorterModel.Msce model) [|true; true|]
    codes.[0] <- -1
    model.CeCodes.[1] <- -1
    Assert.Equal<int>([|1; 3|], model.CeCodes)
    Assert.Equal<ce>([|Ce.fromIndex 1; Ce.fromIndex 3|], activated.MakeSorter(None).Ces)
    let permutations = [|permSi.create [|1; 0; 3; 2|]|]
    let si = mssi.create modelId 4<channelCount> permutations
    permutations.[0] <- permSi.create [|3; 2; 1; 0|]
    si.Perm_Sis.[0] <- permutations.[0]
    Assert.Equal<int>([|1; 0; 3; 2|], si.Perm_Sis.[0].Array)

[<Fact>]
let unfolder_steps_participate_in_equality_and_are_immutable () =
    let kinds = Array.create 2 twoOrbitPairType.Ortho
    let step = twoOrbitUfStep.create kinds 4
    let steps = [|step|]
    let source = twoOrbitUf4.create twoOrbitPairType.Ortho steps
    let same = twoOrbitUf4.create twoOrbitPairType.Ortho [|twoOrbitUfStep.create (Array.create 2 twoOrbitPairType.Ortho) 4|]
    let different = twoOrbitUf4.create twoOrbitPairType.Ortho [|twoOrbitUfStep.create (Array.create 2 twoOrbitPairType.Para) 4|]
    let before = source.MakePerm_Si.Array
    kinds.[0] <- twoOrbitPairType.Para
    step.TwoOrbitPairTypes.[0] <- twoOrbitPairType.Para
    steps.[0] <- different.TwoOrbitUnfolderSteps.[0]
    source.TwoOrbitUnfolderSteps.[0] <- steps.[0]
    Assert.Equal(source, same)
    Assert.Equal(source.GetHashCode(), same.GetHashCode())
    Assert.NotEqual(source, different)
    Assert.Equal<int>(before, source.MakePerm_Si.Array)
    let six kind = twoOrbitUf6.create twoOrbitTripleType.Ortho1 [|twoOrbitUfStep.create (Array.create 3 kind) 6|]
    Assert.NotEqual(six twoOrbitPairType.Ortho, six twoOrbitPairType.Para)

[<Fact>]
let all_model_array_containers_snapshot_inputs_and_outputs () =
    let check (create: 'a[] -> 'model) (read: 'model -> 'a[]) (first: 'a) (second: 'a) =
        let entries = [|first|]
        let model = create entries
        entries.[0] <- second
        (read model).[0] <- second
        Assert.True((first = (read model).[0]))
    check (msrs.create modelId 4<channelCount>) (fun m -> m.Perm_Rss)
        (permRs.create [|1; 0; 3; 2|]) (permRs.create [|3; 2; 1; 0|])
    check (msuf4.create modelId 4<channelCount>) (fun m -> m.TwoOrbitUnfolder4s)
        (twoOrbitUf4.create twoOrbitPairType.Ortho [||]) (twoOrbitUf4.create twoOrbitPairType.Para [||])
    check (msuf6.create modelId 6<channelCount>) (fun m -> m.TwoOrbitUnfolder6s)
        (twoOrbitUf6.create twoOrbitTripleType.Ortho1 [||]) (twoOrbitUf6.create twoOrbitTripleType.Ortho2 [||])

[<Fact>]
let mutator_ids_include_variant_and_canonical_rates () =
    let rates = indelRates.create (0.25, 0.0, 0.0)
    let make variant = msceRandMutate.create rngFactory.LcgFactory rates (UMX.tag true) variant
    let ids = [V1; V2; V3] |> List.map (fun variant -> (make variant).Id)
    Assert.Equal(3, ids |> Set.ofList |> Set.count)
    let expected = GuidUtils.guidFromObjs [box "Sortopia.Models.Sorting/msceRandMutate/v1"; box "V1"; box rngFactory.LcgFactory; box rates; box true]
    Assert.Equal(expected, UMX.untag (make V1).Id)
    Assert.NotEqual((make V1).Id, (msceRandMutate.create rngFactory.LcgFactory (indelRates.create (0.3, 0.0, 0.0)) (UMX.tag true) V1).Id)
    let dto = make V2 |> MsceRandMutateDto.fromDomain |> MessagePackSetup.serialize
    let restored = MessagePackSetup.deserialize<msceRandMutateDto> dto |> MsceRandMutateDto.toDomain
    Assert.Equal((make V2).Id, restored.Id)

[<Fact>]
let prefix_and_merge_models_have_complete_value_equality () =
    let prefixId = prefixLibId.create 8<channelCount> 3<stageLength> PrefixA
    let a, b = msasPfx.create prefixId, msasPfx.create prefixId
    Assert.True((box a).Equals(box b))
    Assert.Equal(a.GetHashCode(), b.GetHashCode())
    let make dimension fill variant = msasM.create 16<channelCount> dimension fill variant
    let baseline = make 2<mergeDimension> NoSuffix VariantA
    Assert.NotEqual(baseline, make 4<mergeDimension> NoSuffix VariantA)
    Assert.NotEqual(baseline, make 2<mergeDimension> VV_1 VariantA)
    Assert.NotEqual(baseline, make 2<mergeDimension> NoSuffix Batcher)
    Assert.Equal(8<channelCount>, baseline.SorterLibId.ChannelCount)
    let expected = SorterDataParse.getCeArrayFromMergeLib (mergeLibId.create 16<channelCount> 2<mergeDimension> VariantA) |> Option.get |> Array.concat
    Assert.Equal<ce>(expected, baseline.CeArray.Value)

[<Fact>]
let input_models_reject_invalid_dimensions_and_unavailable_prefixes () =
    Assert.Throws<ArgumentException>(fun () -> msasM.create 8<channelCount> 0<mergeDimension> NoSuffix VariantA |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> msasO.create (permutation.create [|1; 0|]) 0 |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> msasPfx.create (prefixLibId.create 8<channelCount> 9<stageLength> PrefixC) |> ignore) |> ignore
    let full = msasF.create 4<channelCount>
    Assert.Equal(16<sortableCount>, (full.MakeSortableBoolTest (UMX.tag Guid.Empty) 4<channelCount>).SortableCount)
    Assert.Throws<ArgumentException>(fun () -> full.MakeSortableBoolTest (UMX.tag Guid.Empty) 5<channelCount> |> ignore) |> ignore

[<Fact>]
let models_reject_invalid_shapes_and_sets_reject_duplicate_ids () =
    Assert.Throws<ArgumentNullException>(fun () -> msce.create modelId 4<channelCount> null |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> mssi.create modelId 8<channelCount> [|permSi.create [|1; 0; 3; 2|]|] |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> msceRandGen.create rngFactory.LcgFactory 1<channelCount> (UMX.tag true) 1<ceLength> |> ignore) |> ignore
    let model = msce.create modelId 4<channelCount> [|1|] |> simpleSorterModel.Msce |> sorterModel.Simple
    Assert.Throws<ArgumentException>(fun () -> sorterModelSet.create (UMX.tag Guid.Empty) [|model; model|] 1<ceLength> |> ignore) |> ignore
    Assert.Throws<ArgumentException>(fun () -> sorterModelSet.create (UMX.tag Guid.Empty) [|model|] -1<ceLength> |> ignore) |> ignore
    Assert.Equal(sorterModelType.Unknown, SorterModelType.fromString (SorterModelType.toString sorterModelType.Unknown))
