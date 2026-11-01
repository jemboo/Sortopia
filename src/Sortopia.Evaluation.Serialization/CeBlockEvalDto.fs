namespace Sortopia.Evaluation.Serialization

open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Evaluation

[<MessagePackObject>]
type ceBlockEvalDto = {
    [<Key(0)>] Prefix: ceBlockDto
    [<Key(1)>] CeBlock: ceBlockDto
    [<Key(2)>] CeUseCounts: int array
    [<Key(3)>] UnsortedCount: int
    [<Key(4)>] SortableTests: sortableTestsDto option
}

module CeBlockEvalDto =

    let fromDomain (eval: ceBlockEval) : ceBlockEvalDto =
        {
            Prefix = CeBlockDto.fromDomain eval.Prefix
            CeBlock = CeBlockDto.fromDomain eval.CeBlock
            // We store the raw array from the container
            CeUseCounts = eval.UseCountArray
            UnsortedCount = %eval.UnsortedCount
            SortableTests = 
                eval.SortableTests
                |> Option.map SortableTestsDto.fromDomain
        }

    let toDomain (dto: ceBlockEvalDto) : ceBlockEval =
        let prefix = CeBlockDto.toDomain dto.Prefix
        let ceb = CeBlockDto.toDomain dto.CeBlock
        let counts = ceUseCounts.CreateFromArray dto.CeUseCounts
        let tests = dto.SortableTests |> Option.map SortableTestsDto.toDomain
        let unsortedCount = dto.UnsortedCount |> UMX.tag<sortableCount>
        
        // The factory handles recreating the Lazy usedCes and stageSequence
        ceBlockEval.create prefix ceb counts unsortedCount tests