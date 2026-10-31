
namespace Sortopia.Models.Sorting.Serialization.Simple.Uf6

open System
open FSharp.UMX
open Sortopia.Permutations.Serialization.TwoOrbitUnfolder
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Uf6

type msuf6Dto =
    { id: Guid
      channelCount: int
      twoOrbitUf6Dtos: twoOrbitUf6Dto array }

    static member Create(id: Guid, channelCount: int, twoOrbitUnfolder6s: twoOrbitUf6Dto array) : msuf6Dto =
        if isNull twoOrbitUnfolder6s then
            raise (ArgumentNullException(nameof twoOrbitUnfolder6s, "TwoOrbitUnfolder6s array cannot be null"))
        elif twoOrbitUnfolder6s.Length < 1 then
            raise (ArgumentException($"Must have at least 1 TwoOrbitUnfolder6, got {twoOrbitUnfolder6s.Length}", nameof twoOrbitUnfolder6s))
        elif channelCount < 1 then
            raise (ArgumentException($"ChannelCount must be at least 1, got {channelCount}", nameof channelCount))
        elif twoOrbitUnfolder6s |> Array.exists (fun tou -> (tou |> TwoOrbitUf6Dto.getOrder) <> channelCount) then
            raise (ArgumentException($"All TwoOrbitUnfolder6 must have order {channelCount}", nameof twoOrbitUnfolder6s))
        else
            { id = id
              channelCount = channelCount
              twoOrbitUf6Dtos = twoOrbitUnfolder6s }

module Msuf6Dto =

    let fromDomain (msuf6: msuf6) : msuf6Dto =
        { id = %msuf6.Id
          channelCount = %msuf6.ChannelCount
          twoOrbitUf6Dtos = msuf6.TwoOrbitUnfolder6s |> Array.map TwoOrbitUf6Dto.fromDomain }

    let toDomain (dto: msuf6Dto) : msuf6 =
        let twoOrbitUnfolder6s =
            dto.twoOrbitUf6Dtos
            |> Array.map TwoOrbitUf6Dto.toDomain

        msuf6.create
            (UMX.tag<sorterModelId> dto.id)
            (UMX.tag<channelCount> dto.channelCount)
            twoOrbitUnfolder6s

