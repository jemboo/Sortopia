namespace Sortopia.Models.Sorting
open FSharp.UMX

open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting.Simple


type sorterModel =
     | Simple of simpleSorterModel
     | SimpleR of simpleSorterModelR
     | SimpleS of simpleSorterModelS
     | Unknown


module SorterModel =

    let getChannelCount (model: sorterModel) : int<channelCount> =
        match model with
        | Simple sms -> sms |> SimpleSorterModel.getChannelCount
        | SimpleR sms -> sms |> SimpleSorterModelR.getChannelCount
        | SimpleS sms -> sms |> SimpleSorterModelS.getChannelCount
        | Unknown -> failwith "Unknown sorterModel"


    let getStageLength (model: sorterModel) : int<stageLength> =
        match model with
        | Simple sms -> sms |> SimpleSorterModel.getStageLength
        | SimpleR sms -> sms |> SimpleSorterModelR.getStageLength
        | SimpleS sms -> sms |> SimpleSorterModelS.getStageLength
        | Unknown -> failwith "Unknown sorterModel"

    let getCeLength (model: sorterModel) : int<ceLength> =
        match model with
        | Simple sms -> sms |> SimpleSorterModel.getCeLength
        | SimpleR sms -> sms |> SimpleSorterModelR.getCeLength
        | SimpleS sms -> sms |> SimpleSorterModelS.getCeLength
        | Unknown -> failwith "Unknown sorterModel"

    let getId (model: sorterModel) : Guid<sorterModelId> =
        match model with
        | Simple sms -> sms |> SimpleSorterModel.getId
        | SimpleR sms -> sms |> SimpleSorterModelR.getId
        | SimpleS sms -> sms |> SimpleSorterModelS.getId
        | Unknown -> failwith "Unknown sorterModel"


    let makeSorter (model: sorterModel) (maxCeCount: int<ceLength> option)  : sorter =
        match model with
        | Simple sms -> sms |> SimpleSorterModel.makeSorter maxCeCount
        | SimpleR sms -> sms |> SimpleSorterModelR.makeSorter maxCeCount
        | SimpleS sms -> sms |> SimpleSorterModelS.makeSorter maxCeCount
        | Unknown -> failwith "Unknown sorterModel"

    let asSimpleSorterModel (model: sorterModel) : simpleSorterModel =
        match model with
        | Simple sms -> sms
        | SimpleR _ -> invalidArg "model" "Expected Simple; use asSimpleSorterModelR to preserve activators."
        | SimpleS _ -> invalidArg "model" "Expected Simple; use asSimpleSorterModelS to preserve activators."
        | Unknown -> failwith "Unknown sorterModel"

    let asSimpleSorterModelR (model: sorterModel) : simpleSorterModelR =
        match model with
        | SimpleR sms -> sms
        | Simple _ | SimpleS _ -> invalidArg "model" "Expected SimpleR."
        | Unknown -> failwith "Unknown sorterModel"


    let asSimpleSorterModelS (model: sorterModel) : simpleSorterModelS =
        match model with
        | SimpleS sms -> sms
        | _ -> invalidArg "model" "Expected SimpleS."
