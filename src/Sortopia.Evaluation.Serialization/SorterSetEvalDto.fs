namespace Sortopia.Evaluation.Serialization

open System
open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Evaluation

[<MessagePackObject>]
type sorterSetEvalDto = {
    [<Key(0)>] SorterSetEvalId: Guid
    [<Key(1)>] SorterSetId: Guid
    [<Key(2)>] SorterTestsId: Guid
    [<Key(3)>] SorterEvals: sorterEvalDto array
}

module SorterSetEvalDto =

    let private checkIds (dto: sorterSetEvalDto) =
        if dto.SorterSetEvalId = Guid.Empty then
            failwith "SorterSetEvalId must not be empty"
        if dto.SorterSetId = Guid.Empty then
            failwith "SorterSetId must not be empty"
        if dto.SorterTestsId = Guid.Empty then
            failwith "SorterTestsId must not be empty"
        dto

    let fromDomain (sorterSetEval: sorterSetEval) : sorterSetEvalDto =
        { 
            SorterSetEvalId = %sorterSetEval.SorterSetEvalId
            SorterSetId = %sorterSetEval.SorterSetId
            SorterTestsId = %sorterSetEval.SorterTestId
            SorterEvals = sorterSetEval.SorterEvals |> Array.map SorterEvalDto.fromDomain
        } |> checkIds

    let toDomain (dto: sorterSetEvalDto) : sorterSetEval =
        let dto = checkIds dto
        sorterSetEval.create
            (UMX.tag<sorterSetEvalId> dto.SorterSetEvalId)
            (UMX.tag<sorterSetId> dto.SorterSetId)
            (UMX.tag<sortableTestsId> dto.SorterTestsId)
            (dto.SorterEvals |> Array.map SorterEvalDto.toDomain)
