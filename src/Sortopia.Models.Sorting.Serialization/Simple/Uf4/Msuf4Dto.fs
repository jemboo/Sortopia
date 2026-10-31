
namespace Sortopia.Models.Sorting.Serialization.Simple.Uf4

open System
open FSharp.UMX
open Sortopia.Permutations.Serialization.TwoOrbitUnfolder
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Uf4

type msuf4Dto =
    { id: Guid
      channelCount: int
      twoOrbitUf4Dtos: twoOrbitUf4Dto array }

    static member Create(id: Guid, channelCount: int, twoOrbitUnfolder4s: twoOrbitUf4Dto array) : msuf4Dto =
        if isNull twoOrbitUnfolder4s then
            raise (ArgumentNullException(nameof twoOrbitUnfolder4s, "TwoOrbitUnfolder4s array cannot be null"))
        elif twoOrbitUnfolder4s.Length < 1 then
            raise (ArgumentException($"Must have at least 1 TwoOrbitUnfolder4, got {twoOrbitUnfolder4s.Length}", nameof twoOrbitUnfolder4s))
        elif channelCount < 1 then
            raise (ArgumentException($"ChannelCount must be at least 1, got {channelCount}", nameof channelCount))
        elif twoOrbitUnfolder4s |> Array.exists (fun tou -> (tou |> TwoOrbitUf4Dto.getOrder) <> channelCount) then
            raise (ArgumentException($"All TwoOrbitUnfolder4 must have order {channelCount}", nameof twoOrbitUnfolder4s))
        else
            { id = id
              channelCount = channelCount
              twoOrbitUf4Dtos = twoOrbitUnfolder4s }

module Msuf4Dto =

    let fromDomain (msuf4: msuf4) : msuf4Dto =
        { id = %msuf4.Id
          channelCount = %msuf4.ChannelCount
          twoOrbitUf4Dtos = msuf4.TwoOrbitUnfolder4s |> Array.map TwoOrbitUf4Dto.fromDomain }

    let toDomain (dto: msuf4Dto) : msuf4 =
        let twoOrbitUnfolder4s =
            dto.twoOrbitUf4Dtos
            |> Array.map TwoOrbitUf4Dto.toDomain

        msuf4.create
            (UMX.tag<sorterModelId> dto.id)
            (UMX.tag<channelCount> dto.channelCount)
            twoOrbitUnfolder4s

