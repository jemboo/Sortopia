namespace Sortopia.Core.Serialization

open System
open MessagePack
open FSharp.UMX
open Sortopia.Core

type rngTypeDto =
    | Lcg = 0
    | Net = 1
    | Smx = 2

module RngTypeDto =
    let fromDomain (rngType: rngType) : rngTypeDto =
        match rngType with
        | Lcg -> rngTypeDto.Lcg
        | Net -> rngTypeDto.Net
        | Smx -> rngTypeDto.Smx
    
    let toDomain (dto: rngTypeDto) : rngType =
        match dto with
        | rngTypeDto.Lcg -> Lcg
        | rngTypeDto.Net -> Net
        | rngTypeDto.Smx -> Smx
        | _ -> failwith "Invalid rngTypeDto value"

[<MessagePackObject>]
type rngFactoryDto = {
    [<Key("id")>] id: string
    [<Key("rngTypeDto")>] rngTypeDto: rngTypeDto
}

module RngFactoryDto =
    let fromDomain (factory: rngFactory) : rngFactoryDto =
        { id = (%factory.Id).ToString()
          rngTypeDto = RngTypeDto.fromDomain factory.RngType }
    
    let toDomain (dto: rngFactoryDto) : rngFactory =
        let rngType = RngTypeDto.toDomain dto.rngTypeDto
        let factory = rngFactory.getFactory rngType
        
        // Validate that the ID matches the expected factory ID
        let expectedId = %factory.Id
        let providedId = Guid.Parse(dto.id)
        
        if expectedId <> providedId then
            failwithf "Factory ID mismatch: expected %A but got %A for rngType %A" 
                expectedId providedId rngType
        
        factory

