namespace Sortopia.Models.Sorting

open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting.Simple


type sorterModelMutator =
     | Simple of simpleSorterModelMutator
     | SimpleR of simpleSorterModelRMutator
     | SimpleS of simpleSorterModelSMutator
     | Unknown


module SorterModelMutator =

    let getId (model:sorterModelMutator) : Guid<sorterModelMutatorId> =
        match model with
        | Simple ssm -> SimpleSorterModelMutator.getId ssm
        | SimpleR ssm -> ssm.Id
        | SimpleS ssm -> ssm.Id
        | Unknown -> failwith "Unknown sorterModel"

    let makeMutantIdToParentIdMap mutator (parents: sorterModel[]) count modd =
        parents |> Array.collect (fun parent ->
            let parentId = SorterModel.getId parent
            Array.init count (fun index ->
                CommonMutator.makeSorterModelId parentId (getId mutator) (UMX.tag index) modd, parentId))
        |> Map.ofArray


    let makeMutantSorterModelFromIndexAndMod
                    (sorterModelMutator: sorterModelMutator)
                    (parentModel: sorterModel)
                    (index: int<mutationIndex>)
                    (modd: int<mutationMod>) : sorterModel =

        match (sorterModelMutator, parentModel) with
        | (sorterModelMutator.Simple ssm, sorterModel.Simple parent) ->
                    SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod
                        ssm parent index modd
                    |> sorterModel.Simple
        | (sorterModelMutator.SimpleR mutator, sorterModel.SimpleR parent) ->
                    mutator.MakeSorterModelFromIndexAndMod parent index modd |> sorterModel.SimpleR
        | (sorterModelMutator.SimpleS mutator, sorterModel.SimpleS parent) ->
                    mutator.MakeSorterModelFromIndexAndMod parent index modd |> sorterModel.SimpleS
        | _ -> failwith "Mutator and parent model types do not match."


    let makeMutantSorterModelFromId
                    (mutatorModel: sorterModelMutator)
                    (parentModel: sorterModel)
                    (sorterModelId: Guid<sorterModelId>) : sorterModel =
        match (mutatorModel, parentModel) with
        | (Simple ssm, sorterModel.Simple parent) ->
                    SimpleSorterModelMutator.makeMutantSorterModelFromId ssm parent sorterModelId
                    |> sorterModel.Simple
        | (sorterModelMutator.SimpleR mutator, sorterModel.SimpleR parent) ->
                    mutator.MakeSorterModelFromId parent sorterModelId |> sorterModel.SimpleR
        | (sorterModelMutator.SimpleS mutator, sorterModel.SimpleS parent) ->
                    mutator.MakeSorterModelFromId parent sorterModelId |> sorterModel.SimpleS
        | _ -> failwith "Mutator and parent model types do not match."


