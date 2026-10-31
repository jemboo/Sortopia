namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Generates a simple model with independent boolean activators.
type simpleSorterModelSGen =
    private {
        modelGen: simpleSorterModelGen
        activationProbability: float
        id: Guid<sorterModelGenId>
    }

    static member create (modelGen: simpleSorterModelGen) (activationProbability: float) =
        if obj.ReferenceEquals(modelGen, null) then nullArg "modelGen"
        if not (Double.IsFinite activationProbability) || activationProbability < 0.0 || activationProbability > 1.0 then invalidArg "activationProbability" "Activation probability must be finite and between 0 and 1."
        let id =
            [box "simpleSorterModelSGen"; box (SimpleSorterModelGen.getId modelGen)
             box (BitConverter.DoubleToInt64Bits(if activationProbability = 0.0 then 0.0 else activationProbability))]
            |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>
        { modelGen = modelGen; activationProbability = activationProbability; id = id }

    member this.ModelGen = this.modelGen
    member this.ActivationProbability = this.activationProbability
    member this.Id = this.id
    member this.ChannelCount = SimpleSorterModelGen.getChannelCount this.ModelGen
    /// Maximum CE capacity; the generated sorter's actual length depends on its activators.
    member this.CeLength = SimpleSorterModelGen.getCeLength this.ModelGen

    member private this.RngFactory =
        match this.ModelGen with
        | SmmMsceRandGen g -> g.RngFactory
        | SmmMssiRandGen g -> g.RngFactory
        | SmmMsrsRandGen g -> g.RngFactory
        | SmmMsuf4RandGen g -> g.RngFactory
        | SmmMsuf6RandGen g -> g.RngFactory

    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index

    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) =
        let model = SimpleSorterModelGen.makeSorterModelFromId id this.ModelGen
        let seed = [box "activators"; box id] |> GuidUtils.guidFromObjs
        let rng = this.RngFactory.Create seed
        let nextActivator () = float (rng.NextUInt()) / 4294967296.0 < this.ActivationProbability
        let activators = Array.init (SimpleSorterModel.getLength model) (fun _ -> nextActivator ())
        simpleSorterModelS.create model activators

    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) =
        this.MakeSorterModelFromId (this.MakeSorterModelId index)
