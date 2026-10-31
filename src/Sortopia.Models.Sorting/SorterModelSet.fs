namespace Sortopia.Models.Sorting

open System
open FSharp.UMX

open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting.Simple


type sorterModelSet =
      private
        {
          id: Guid<sorterModelSetId>
          sortingMap: Map<Guid<sorterModelId>, sorterModel>
          rawCeLength: int<ceLength>
        }
        with
        static member create
            (id : Guid<sorterModelSetId>)
            (models : sorterModel[])
            (maxCeLength: int<ceLength>): sorterModelSet =
            if isNull models then nullArg "models"
            if int maxCeLength < 0 then invalidArg "maxCeLength" "CE limit must be nonnegative."
            let modelMap =
                models
                |> Array.map (fun sm -> (SorterModel.getId sm, sm))
                |> Map.ofArray

            if modelMap.Count <> models.Length then invalidArg "models" "Model IDs must be unique."
            { id = id; sortingMap = modelMap; rawCeLength = maxCeLength }

        member this.Count with get() = this.sortingMap.Count
        member this.Id with get() = this.id
        member this.RawCeLength with get() = this.rawCeLength
        member this.SorterModels with get() : sorterModel array =
                    this.sortingMap |> Map.toArray |> Array.map snd
        static member empty: sorterModelSet =
            { id = Guid.Empty |> UMX.tag; sortingMap = Map.empty; rawCeLength = 0<ceLength> }


module SorterModelSet =

    let getChannelCount (model: sorterModel) : int<channelCount> =
        SorterModel.getChannelCount model


    let getStageLength (model: sorterModel) : int<stageLength> =
        SorterModel.getStageLength model


    let getId (model: sorterModel) : Guid<sorterModelId> =
        SorterModel.getId model


    let makeSorterSet (id:Guid<sorterSetId>)
                      (maxCeCount: int<ceLength> option)
                      (sorterModelSet: sorterModelSet) : sorterSet =
        let sorters =
            sorterModelSet.SorterModels
            |> Array.map (fun sm -> SorterModel.makeSorter sm maxCeCount)
        sorterSet.create id sorters

