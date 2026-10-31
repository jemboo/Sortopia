namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce

/// Mutates the underlying model and independently applies boolean flips to activators.
type simpleSorterModelSMutator =
    private {
        modelMutator: simpleSorterModelMutator
        activatorMutationRate: float
        id: Guid<sorterModelMutatorId>
    }

    static member create (modelMutator: simpleSorterModelMutator) (activatorMutationRate: float) =
        if obj.ReferenceEquals(modelMutator, null) then nullArg "modelMutator"
        if not (Double.IsFinite activatorMutationRate) || activatorMutationRate < 0.0 || activatorMutationRate > 1.0 then
            invalidArg "activatorMutationRate" "Activator mutation rate must be finite and between 0 and 1."
        let id =
            [box "simpleSorterModelSMutator"; box (SimpleSorterModelMutator.getId modelMutator)
             box (BitConverter.DoubleToInt64Bits(if activatorMutationRate = 0.0 then 0.0 else activatorMutationRate))]
            |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelMutatorId>
        { modelMutator = modelMutator; activatorMutationRate = activatorMutationRate; id = id }

    member this.ModelMutator = this.modelMutator
    member this.ActivatorMutationRate = this.activatorMutationRate
    member this.Id = this.id

    member private this.RngFactory =
        match this.ModelMutator with
        | SmmMsceRandMutate m -> m.RngFactory
        | SmmMssiRandMutate m -> m.RngFactory
        | SmmMsrsRandMutate m -> m.RngFactory
        | SmmMsuf4RandMutate m -> m.RngFactory
        | SmmMsuf6RandMutate m -> m.RngFactory

    member this.MakeSorterModelId (parent: simpleSorterModelS) index modd =
        CommonMutator.makeSorterModelId (SimpleSorterModelS.getId parent) this.Id index modd

    member this.MakeSorterModelFromId (parent: simpleSorterModelS) (id: Guid<sorterModelId>) =
        let model, activators =
            match this.ModelMutator, parent.Model with
            | SmmMsceRandMutate mutator, simpleSorterModel.Msce source ->
                // Carry activators through insertion/deletion with their entries. New entries start inactive.
                let rng = mutator.RngFactory.Create %id
                let newCode () = Ce.generateCeCode %mutator.ExcludeSelfCe (int source.ChannelCount) rng.NextIndex
                let entries =
                    Array.zip source.CeCodes parent.Activators
                    |> IndelRates.mutate mutator.IndelRates (fun () -> newCode (), false)
                        (fun (_, activator) -> newCode (), activator) rng.NextFloat
                let codes, values = Array.unzip entries
                msce.create id source.ChannelCount codes |> simpleSorterModel.Msce, values
            | _ ->
                SimpleSorterModelMutator.makeMutantSorterModelFromId this.ModelMutator parent.Model id,
                parent.Activators
        // Separate the activator stream from model mutation; reconstructing by ID remains deterministic.
        let seed = [box "activators"; box id] |> GuidUtils.guidFromObjs
        let rng = this.RngFactory.Create seed
        let values = activators |> Array.map (fun value ->
            if float (rng.NextUInt()) / 4294967296.0 < this.ActivatorMutationRate then not value else value)
        simpleSorterModelS.create model values

    member this.MakeSorterModelFromIndexAndMod (parent: simpleSorterModelS) index modd =
        this.MakeSorterModelFromId parent (this.MakeSorterModelId parent index modd)
