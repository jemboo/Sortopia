namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX

open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Ce
open Sortopia.Models.Sorting.Simple.Rs
open Sortopia.Models.Sorting.Simple.Si
open Sortopia.Models.Sorting.Simple.Uf4
open Sortopia.Models.Sorting.Simple.Uf6


type simpleSorterModel =
     | Msce of msce
     | Mssi of mssi
     | Msrs of msrs
     | Msuf4 of msuf4
     | Msuf6 of msuf6


module SimpleSorterModel =

    /// Number of entries in the model's underlying array, before expanding to comparison elements.
    let getLength (model: simpleSorterModel) : int =
        match model with
        | Msce model -> model.CeCodes.Length
        | Mssi model -> model.Perm_Sis.Length
        | Msrs model -> model.Perm_Rss.Length
        | Msuf4 model -> model.TwoOrbitUnfolder4s.Length
        | Msuf6 model -> model.TwoOrbitUnfolder6s.Length

    let getId (model: simpleSorterModel) : Guid<sorterModelId> =
        match model with
        | Msce msce -> msce.Id
        | Mssi mssi -> mssi.Id
        | Msrs msrs -> msrs.Id
        | Msuf4 msuf4 -> msuf4.Id
        | Msuf6 msuf6 -> msuf6.Id

    let makeSorter (maxCeLength: int<ceLength> option) (model: simpleSorterModel)  : sorter =
        match model with
        | Msce msce -> msce.MakeSorter(maxCeLength)
        | Mssi mssi -> mssi.MakeSorter(maxCeLength)
        | Msrs msrs -> msrs.MakeSorter(maxCeLength)
        | Msuf4 msuf4 -> msuf4.MakeSorter(maxCeLength)
        | Msuf6 msuf6 -> msuf6.MakeSorter(maxCeLength)

    let getChannelCount (model: simpleSorterModel) : int<channelCount> =
        match model with
        | Msce msce -> msce.ChannelCount
        | Mssi mssi -> mssi.ChannelCount
        | Msrs msrs -> msrs.ChannelCount
        | Msuf4 msuf4 -> msuf4.ChannelCount
        | Msuf6 msuf6 -> msuf6.ChannelCount

    let getCeLength (model: simpleSorterModel) : int<ceLength> =
        match model with
        | Msce msce -> msce.CeLength
        | Mssi mssi -> mssi.CeLength
        | Msrs msrs -> msrs.CeLength
        | Msuf4 msuf4 -> msuf4.CeLength
        | Msuf6 msuf6 -> msuf6.CeLength

    let getStageLength (model: simpleSorterModel) : int<stageLength> =
        match model with
        | Msce msce -> msce.StageLength
        | Mssi mssi -> mssi.StageLength
        | Msrs msrs -> msrs.StageLength
        | Msuf4 msuf4 -> msuf4.StageLength
        | Msuf6 msuf6 -> msuf6.StageLength
