namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Generates a simple model with independent, uniformly distributed bounded activators.
type simpleSorterModelRGen =
    private {
        modelGen: simpleSorterModelGen
        aMax: int
        id: Guid<sorterModelGenId>
    }

    static member create (modelGen: simpleSorterModelGen) (aMax: int) =
        if obj.ReferenceEquals(modelGen, null) then nullArg "modelGen"
        if aMax < 0 then invalidArg "aMax" "aMax must be nonnegative."
        let id =
            [box "simpleSorterModelRGen"; box (SimpleSorterModelGen.getId modelGen); box aMax]
            |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>
        { modelGen = modelGen; aMax = aMax; id = id }

    member this.ModelGen = this.modelGen
    member this.AMax = this.aMax
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
        // Use rejection sampling over uint32 to avoid bias and overflow even for aMax = Int32.MaxValue.
        let range = 2UL * uint64 this.AMax + 1UL
        let limit = 0x100000000UL - (0x100000000UL % range)
        let nextActivator () =
            if this.AMax = 0 then 0
            else
                let mutable sample = uint64 (rng.NextUInt())
                while sample >= limit do
                    sample <- uint64 (rng.NextUInt())
                int (int64 (sample % range) - int64 this.AMax)
        let activators = Array.init (SimpleSorterModel.getLength model) (fun _ -> nextActivator ())
        simpleSorterModelR.create model this.AMax activators

    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) =
        this.MakeSorterModelFromId (this.MakeSorterModelId index)
