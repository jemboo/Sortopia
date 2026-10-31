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


type simpleSorterModelGen =
     | SmmMsceRandGen of msceRandGen
     | SmmMssiRandGen of mssiRandGen
     | SmmMsrsRandGen of msrsRandGen
     | SmmMsuf4RandGen of msuf4RandGen
     | SmmMsuf6RandGen of msuf6RandGen


module SimpleSorterModelGen =

    let makeUniform
            (rngFactory: rngFactory)
            (channelCount: int<channelCount>)
            (stageLength: int<stageLength>)
            (simpleorterModelType: simpleSorterModelType)
            (excludeSelfCe: bool<excludeSelfCe>) =
        match simpleorterModelType with
        | simpleSorterModelType.Msce ->
            let ceLength = stageLength |> StageLength.toCeLength channelCount
            SmmMsceRandGen (msceRandGen.create rngFactory channelCount excludeSelfCe ceLength)
        | simpleSorterModelType.Mssi ->
            SmmMssiRandGen (mssiRandGen.create rngFactory channelCount stageLength)
        | simpleSorterModelType.Msrs ->
            let opsGenRates = opsGenRates.createUniform()
            SmmMsrsRandGen (msrsRandGen.create rngFactory channelCount opsGenRates stageLength)
        | simpleSorterModelType.Msuf4 ->
            let uf4GenRates = Uf4GenRates.makeUniform %channelCount
            SmmMsuf4RandGen (msuf4RandGen.create rngFactory channelCount stageLength uf4GenRates)
        | simpleSorterModelType.Msuf6 ->
            let uf6GenRates = Uf6GenRates.makeUniform %channelCount
            SmmMsuf6RandGen (msuf6RandGen.create rngFactory channelCount stageLength uf6GenRates)


    let getId (model:simpleSorterModelGen) : Guid<sorterModelGenId> =
        match model with
        | SmmMsceRandGen msce -> msce.Id
        | SmmMssiRandGen mssi -> mssi.Id
        | SmmMsrsRandGen msrs -> msrs.Id
        | SmmMsuf4RandGen msuf4 -> msuf4.Id
        | SmmMsuf6RandGen msuf6 -> msuf6.Id


    let getChannelCount (model: simpleSorterModelGen) : int<channelCount> =
        match model with
        | SmmMsceRandGen msce -> msce.ChannelCount
        | SmmMssiRandGen mssi -> mssi.ChannelCount
        | SmmMsrsRandGen msrs -> msrs.ChannelCount
        | SmmMsuf4RandGen msuf4 -> msuf4.ChannelCount
        | SmmMsuf6RandGen msuf6 -> msuf6.ChannelCount


    let getCeLength (model: simpleSorterModelGen) : int<ceLength> =
        match model with
        | SmmMsceRandGen msce -> msce.CeLength
        | SmmMssiRandGen mssi -> mssi.CeLength
        | SmmMsrsRandGen msrs -> msrs.CeLength
        | SmmMsuf4RandGen msuf4 -> msuf4.CeLength
        | SmmMsuf6RandGen msuf6 -> msuf6.CeLength


    let makeSorterModelFromIndex (index: int<sortingGenIndex>)  (model: simpleSorterModelGen) : simpleSorterModel =
        match model with
        | SmmMsceRandGen msce -> msce.MakeSorterModelFromIndex index |> simpleSorterModel.Msce
        | SmmMssiRandGen mssi -> mssi.MakeSorterModelFromIndex index |> simpleSorterModel.Mssi
        | SmmMsrsRandGen msrs -> msrs.MakeSorterModelFromIndex index |> simpleSorterModel.Msrs
        | SmmMsuf4RandGen msuf4 -> msuf4.MakeSorterModelFromIndex index |> simpleSorterModel.Msuf4
        | SmmMsuf6RandGen msuf6 -> msuf6.MakeSorterModelFromIndex index |> simpleSorterModel.Msuf6


    let makeSorterModelFromId (id: Guid<sorterModelId>) (model: simpleSorterModelGen) : simpleSorterModel =
        match model with
        | SmmMsceRandGen msce -> msce.MakeSorterModelFromId id |> simpleSorterModel.Msce
        | SmmMssiRandGen mssi -> mssi.MakeSorterModelFromId id |> simpleSorterModel.Mssi
        | SmmMsrsRandGen msrs -> msrs.MakeSorterModelFromId id |> simpleSorterModel.Msrs
        | SmmMsuf4RandGen msuf4 -> msuf4.MakeSorterModelFromId id |> simpleSorterModel.Msuf4
        | SmmMsuf6RandGen msuf6 -> msuf6.MakeSorterModelFromId id |> simpleSorterModel.Msuf6


    let makeSorterModelsFromIndexSpan (firstIndex:int<sorterCount>)
                       (count: int<sorterCount>)
                       (model: simpleSorterModelGen) : simpleSorterModel[] =
        [| for i in 0 .. %count - 1 ->
                makeSorterModelFromIndex
                        ((%firstIndex + i) |> UMX.tag<sortingGenIndex>)
                        model
        |]


    let makeSorterModelsFromIds (ids:Guid<sorterModelId> seq)
                       (model: simpleSorterModelGen) : simpleSorterModel[] =
        [| for id in ids -> makeSorterModelFromId id model |]


