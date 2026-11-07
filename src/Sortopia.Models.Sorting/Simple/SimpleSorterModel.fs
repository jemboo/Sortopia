namespace Sortopia.Models.Sorting.Simple

open System
open FSharp.UMX
open Sortopia.Core

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

    let getPrefix (model: simpleSorterModel) : ce array =
        match model with
        | Msce model -> model.Prefix
        | Mssi model -> model.Prefix
        | Msrs model -> model.Prefix
        | Msuf4 model -> model.Prefix
        | Msuf6 model -> model.Prefix

    let getId (model: simpleSorterModel) : Guid<sorterModelId> =
        match model with
        | Msce msce -> msce.Id
        | Mssi mssi -> mssi.Id
        | Msrs msrs -> msrs.Id
        | Msuf4 msuf4 -> msuf4.Id
        | Msuf6 msuf6 -> msuf6.Id

    let makeSorterWithoutPrefix (maxCeLength: int<ceLength> option) (model: simpleSorterModel)  : sorter =
        match model with
        | Msce msce -> msce.MakeSorterWithoutPrefix(maxCeLength)
        | Mssi mssi -> mssi.MakeSorterWithoutPrefix(maxCeLength)
        | Msrs msrs -> msrs.MakeSorterWithoutPrefix(maxCeLength)
        | Msuf4 msuf4 -> msuf4.MakeSorterWithoutPrefix(maxCeLength)
        | Msuf6 msuf6 -> msuf6.MakeSorterWithoutPrefix(maxCeLength)

    let makeSorterWithPrefix (maxCeLength: int<ceLength> option) (model: simpleSorterModel)  : sorter =
        match model with
        | Msce msce -> msce.MakeSorterWithPrefix(maxCeLength)
        | Mssi mssi -> mssi.MakeSorterWithPrefix(maxCeLength)
        | Msrs msrs -> msrs.MakeSorterWithPrefix(maxCeLength)
        | Msuf4 msuf4 -> msuf4.MakeSorterWithPrefix(maxCeLength)
        | Msuf6 msuf6 -> msuf6.MakeSorterWithPrefix(maxCeLength)

    let getChannelCount (model: simpleSorterModel) : int<channelCount> =
        match model with
        | Msce msce -> msce.ChannelCount
        | Mssi mssi -> mssi.ChannelCount
        | Msrs msrs -> msrs.ChannelCount
        | Msuf4 msuf4 -> msuf4.ChannelCount
        | Msuf6 msuf6 -> msuf6.ChannelCount

    /// Explicitly replace a fixed prefix, retaining the suffix and deriving a new model identity.
    /// Mutators never call this: their offspring inherit the original prefix unchanged.
    let withPrefix (prefix: ce array) (model: simpleSorterModel) : simpleSorterModel =
        let width = getChannelCount model
        let prefix = ModelPrefix.snapshot width prefix
        if prefix = getPrefix model then model else
        let id =
            seq {
                yield box "Sortopia.Models.Sorting/replacePrefix/v1"
                yield box (UMX.untag (getId model))
                yield! ModelPrefix.hashValues prefix
            } |> GuidUtils.guidFromObjs |> UMX.tag<sorterModelId>
        match model with
        | Msce m -> msce.create id width prefix m.CeCodes |> Msce
        | Mssi m -> mssi.create id width prefix m.Perm_Sis |> Mssi
        | Msrs m -> msrs.create id width prefix m.Perm_Rss |> Msrs
        | Msuf4 m -> msuf4.create id width prefix m.TwoOrbitUnfolder4s |> Msuf4
        | Msuf6 m -> msuf6.create id width prefix m.TwoOrbitUnfolder6s |> Msuf6

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
