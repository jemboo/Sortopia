
namespace Sortopia.Models.Sorting.Serialization.Simple.Si

open System
open FSharp.UMX
open Sortopia.Models.Sorting.Serialization
open Sortopia.Core.Serialization
open Sortopia.Permutations.Serialization
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple.Si

type mssiDto =
    { id: Guid
      channelCount: int
      permSiDtos: permSiDto array
      prefixCeCodes: int array }

module MssiDto =

    let fromDomain (mssi: mssi) : mssiDto =
        let mssiDtos = mssi.Perm_Sis |> Array.map PermSiDto.fromDomain
        { id = %mssi.Id
          channelCount = %mssi.ChannelCount
          permSiDtos = mssiDtos
          prefixCeCodes = PrefixDto.encode mssi.Prefix }

    let toDomain (mssiDto: mssiDto) : mssi =
        let perm_Sis = mssiDto.permSiDtos |> Array.map (PermSiDto.toDomain)
        mssi.create
            (UMX.tag<sorterModelId> mssiDto.id)
            (UMX.tag<channelCount> mssiDto.channelCount) (PrefixDto.decode mssiDto.prefixCeCodes)
            perm_Sis
