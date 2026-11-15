namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

/// Generates a simple model with each bounded activator starting at AInitial.
type simpleSorterModelRGen =
    private {
        modelGen: simpleSorterModelGen
        aMax: int
        aInitial: int
        id: Guid<sorterModelGenId>
    }

    static member create (modelGen: simpleSorterModelGen) (aMax: int) (aInitial: int) =
        if obj.ReferenceEquals(modelGen, null) then nullArg "modelGen"
        if aMax < 0 then invalidArg "aMax" "aMax must be nonnegative."
        if aInitial < -aMax || aInitial > aMax then invalidArg "aInitial" "AInitial must be within [-AMax, AMax]."
        let id =
            [box "simpleSorterModelRGen"; box (SimpleSorterModelGen.getId modelGen); box aMax; box aInitial]
            |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelGenId>
        { modelGen = modelGen; aMax = aMax; aInitial = aInitial; id = id }

    member this.Prefix = SimpleSorterModelGen.getPrefix this.modelGen
    member this.ModelGen = this.modelGen
    member this.AMax = this.aMax
    member this.AInitial = this.aInitial
    member this.Id = this.id
    member this.ChannelCount = SimpleSorterModelGen.getChannelCount this.ModelGen
    /// Maximum CE capacity; the generated sorter's actual length depends on its activators.
    member this.CeLength = SimpleSorterModelGen.getCeLength this.ModelGen

    member this.MakeSorterModelId (index: int<sortingGenIndex>) : Guid<sorterModelId> =
        CommonGen.makeSorterModelId this.Id index

    member this.MakeSorterModelFromId (id: Guid<sorterModelId>) =
        let model = SimpleSorterModelGen.makeSorterModelFromId id this.ModelGen
        let activators = Array.create (SimpleSorterModel.getLength model) this.AInitial
        simpleSorterModelR.create model this.AMax activators

    member this.MakeSorterModelFromIndex (index: int<sortingGenIndex>) =
        this.MakeSorterModelFromId (this.MakeSorterModelId index)
