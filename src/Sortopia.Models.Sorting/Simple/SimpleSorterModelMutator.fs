namespace Sortopia.Models.Sorting.Simple

open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6


type simpleSorterModelMutator =
     | SmmMsceRandMutate of msceRandMutate
     | SmmMssiRandMutate of mssiRandMutate
     | SmmMsrsRandMutate of msrsRandMutate
     | SmmMsuf4RandMutate of msuf4RandMutate
     | SmmMsuf6RandMutate of msuf6RandMutate


module SimpleSorterModelMutator =

    let getId (model:simpleSorterModelMutator) : Guid<sorterModelMutatorId> =
        match model with
        | SmmMsceRandMutate msce -> msce.Id
        | SmmMssiRandMutate mssi -> mssi.Id
        | SmmMsrsRandMutate msrs -> msrs.Id
        | SmmMsuf4RandMutate msuf4 -> msuf4.Id
        | SmmMsuf6RandMutate msuf6 -> msuf6.Id


    let getMsceModelMutator
                (rngFactory: rngFactory)
                (excludeSelfCe: bool<excludeSelfCe>)
                (modificationRate: float<modificationRate>)
                (mutationRate: float<mutationRate>)
                (insertionRate: float<insertionRate>)
                (deletionRate: float<deletionRate>)
                (mutVariant : mutatorVariant) : simpleSorterModelMutator =
            msceRandMutate.create rngFactory
                    (indelRates.createMod
                            (%modificationRate, %mutationRate, %insertionRate, %deletionRate)
                    )
                    (%excludeSelfCe)
                    mutVariant
            |> simpleSorterModelMutator.SmmMsceRandMutate


    let getMssiModelMutator
                (rngFactory: rngFactory)
                (excludeSelfCe: bool<excludeSelfCe>)
                (modificationRate: float<modificationRate>)
                (orthoRate: float<orthoRate>)
                (paraRate: float<paraRate>)
                (mutVariant : mutatorVariant) : simpleSorterModelMutator =

            mssiRandMutate.create rngFactory
                    (opActionRates.createMod (%modificationRate, %orthoRate, %paraRate))
                    mutVariant
            |> simpleSorterModelMutator.SmmMssiRandMutate


    let getMsrsModelMutator
                (rngFactory: rngFactory)
                (excludeSelfCe: bool<excludeSelfCe>)
                (modificationRate: float<modificationRate>)
                (orthoRate: float<orthoRate>)
                (paraRate: float<paraRate>)
                (selfSymRate: float<selfSymRate>)
                (mutVariant : mutatorVariant) : simpleSorterModelMutator =
            msrsRandMutate.create rngFactory
                    (opsActionRates.createMod %modificationRate %orthoRate %paraRate %selfSymRate)
                    mutVariant
            |> simpleSorterModelMutator.SmmMsrsRandMutate


    let getMsuf4ModelMutator
                (channelCount: int<channelCount>)
                (rngFactory: rngFactory)
                (excludeSelfCe: bool<excludeSelfCe>)
                (seedModificationRate: float<seedModificationRate>)
                (modificationRate: float<modificationRate>)
                (orthoRate: float<orthoRate>)
                (paraRate: float<paraRate>)
                (selfSymRate: float<selfSymRate>)
                (mutVariant : mutatorVariant) : simpleSorterModelMutator =

            let opsSeedActionRates =
                opsActionRates.createMod %seedModificationRate %orthoRate %paraRate %selfSymRate
            let opsActionRates =
                opsActionRates.createMod %modificationRate %orthoRate %paraRate %selfSymRate
            let opsSeedTransitionRates =
                opsTransitionRates.createUniformFromRates opsSeedActionRates
            let opsTransitionRates =
                opsTransitionRates.createUniformFromRates opsActionRates

            let uf4MutRates =
                match mutVariant with
                | mutatorVariant.V1 ->
                        Uf4MutationRates.makeV1
                            %channelCount
                            %seedModificationRate
                            %modificationRate
                            %orthoRate
                            %paraRate
                            %selfSymRate
                | mutatorVariant.V2 ->
                        Uf4MutationRates.makeV2
                            %channelCount
                            %seedModificationRate
                            %modificationRate
                            %orthoRate
                            %paraRate
                            %selfSymRate

                | mutatorVariant.V3 ->
                        failwith "V3 mutator variant is not yet implemented for Msuf4RandMutate"


            msuf4RandMutate.create rngFactory uf4MutRates mutVariant
            |> simpleSorterModelMutator.SmmMsuf4RandMutate


    let getMsuf6ModelMutator
                (channelCount: int<channelCount>)
                (rngFactory: rngFactory)
                (excludeSelfCe: bool<excludeSelfCe>)
                (seedModificationRate: float<seedModificationRate>)
                (modificationRate: float<modificationRate>)
                (orthoRate: float<orthoRate>)
                (paraRate: float<paraRate>)
                (selfSymRate: float<selfSymRate>)
                (mutVariant : mutatorVariant) : simpleSorterModelMutator =

            let seed6ActionRates =
                seed6ActionRates.createUniform (%seedModificationRate)

            let seed6TransitionRates =
                    seed6TransitionRates.create
                        seed6ActionRates seed6ActionRates seed6ActionRates
                        seed6ActionRates seed6ActionRates seed6ActionRates
                        seed6ActionRates

            let opsActionRates =
                opsActionRates.createMod %modificationRate %orthoRate %paraRate %selfSymRate
            let opsTransitionRates =
                opsTransitionRates.createUniformFromRates opsActionRates

            msuf6RandMutate.create
                   rngFactory
                    (Uf6MutationRates.makeUniformFromRates
                                    (%channelCount)
                                    seed6TransitionRates
                                    opsTransitionRates)
                  mutVariant
            |> simpleSorterModelMutator.SmmMsuf6RandMutate


    let makeMutantSorterModelIdFromIndexAndMod
                (mutatorModel: simpleSorterModelMutator)
                (parentModel: simpleSorterModel)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>) : Guid<sorterModelId> =
        match (mutatorModel, parentModel) with
        | (SmmMsceRandMutate msce, Msce parent) -> msce.MakeSorterModelId parent index modd
        | (SmmMssiRandMutate mssi, Mssi parent) -> mssi.MakeSorterModelId parent index modd
        | (SmmMsrsRandMutate msrs, Msrs parent) -> msrs.MakeSorterModelId parent index modd
        | (SmmMsuf4RandMutate msuf4, Msuf4 parent) -> msuf4.MakeSorterModelId parent index modd
        | (SmmMsuf6RandMutate msuf6, Msuf6 parent) -> msuf6.MakeSorterModelId parent index modd
        | _ -> failwith "Mutator and parent model types do not match."


    let makeMutantSorterModelFromIndexAndMod
                (mutatorModel: simpleSorterModelMutator)
                (parentModel: simpleSorterModel)
                (index: int<mutationIndex>)
                (modd: int<mutationMod>): simpleSorterModel =
        match (mutatorModel, parentModel) with
        | (SmmMsceRandMutate msce, Msce parent) -> msce.MakeSorterModelFromIndexAndMod parent index modd |> simpleSorterModel.Msce
        | (SmmMssiRandMutate mssi, Mssi parent) -> mssi.MakeSorterModelFromIndexAndMod parent index modd |> simpleSorterModel.Mssi
        | (SmmMsrsRandMutate msrs, Msrs parent) -> msrs.MakeSorterModelFromIndexAndMod parent index modd |> simpleSorterModel.Msrs
        | (SmmMsuf4RandMutate msuf4, Msuf4 parent) -> msuf4.MakeSorterModelFromIndexAndMod parent index modd |> simpleSorterModel.Msuf4
        | (SmmMsuf6RandMutate msuf6, Msuf6 parent) -> msuf6.MakeSorterModelFromIndexAndMod parent index modd |> simpleSorterModel.Msuf6
        | _ -> failwith "Mutator and parent model types do not match."


    let makeMutantSorterModelFromId
                (mutatorModel: simpleSorterModelMutator)
                (parentModel: simpleSorterModel)
                (sorterModelId: Guid<sorterModelId>) : simpleSorterModel =

        match (mutatorModel, parentModel) with
        | (SmmMsceRandMutate msce, Msce parent) -> msce.MakeSorterModelFromId parent sorterModelId |> simpleSorterModel.Msce
        | (SmmMssiRandMutate mssi, Mssi parent) -> mssi.MakeSorterModelFromId parent sorterModelId |> simpleSorterModel.Mssi
        | (SmmMsrsRandMutate msrs, Msrs parent) -> msrs.MakeSorterModelFromId parent sorterModelId |> simpleSorterModel.Msrs
        | (SmmMsuf4RandMutate msuf4, Msuf4 parent) -> msuf4.MakeSorterModelFromId parent sorterModelId |> simpleSorterModel.Msuf4
        | (SmmMsuf6RandMutate msuf6, Msuf6 parent) -> msuf6.MakeSorterModelFromId parent sorterModelId |> simpleSorterModel.Msuf6
        | _ -> failwith "Mutator and parent model types do not match."


    // Helper to extract the ID from a parent model
    let private getParentId (parentModel: simpleSorterModel) : Guid<sorterModelId> =
        match parentModel with
        | Msce parent  -> parent.Id
        | Mssi parent  -> parent.Id
        | Msrs parent  -> parent.Id
        | Msuf4 parent -> parent.Id
        | Msuf6 parent -> parent.Id


    // creates a map from mutant sorterModeIds to their parent sorterModelIds for a given list of mutant sorterModelIds,
    // and a count of how many mutants were generated from each parent. [| 0 .. (count -1)|] are the indices used to
    // generate mutant sorterModelIds from each of the parent sorterModelIds.
    let makeMutantIdToParentIdMap
                (mutatorModel: simpleSorterModelMutator)
                (parentModels: simpleSorterModel [])
                (count: int)
                (modd: int<mutationMod>) : Map<Guid<sorterModelId>, Guid<sorterModelId>> =

        parentModels
        |> Array.collect (fun parentModel ->
            let parentId = getParentId parentModel
            Array.init count (fun index ->
                let mutantId = makeMutantSorterModelIdFromIndexAndMod
                                        mutatorModel
                                        parentModel
                                        (index |> UMX.tag<mutationIndex>)
                                        modd
                (mutantId, parentId)
            )
        )
        |> Map.ofArray

