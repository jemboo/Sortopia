namespace Sortopia.Evaluation.Serialization

open MessagePack
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Serialization.Inputs
open Sortopia.Evaluation

[<MessagePackObject>]
type ceBlockEvalDto = {
    [<Key(0)>] CeBlock: ceBlockDto
    [<Key(1)>] CeUseCounts: int array
    [<Key(2)>] UnsortedCount: int
    [<Key(3)>] SortableTests: sortableTestsDto option
}

module CeBlockEvalDto =

    let fromDomain (eval: ceBlockEval) : ceBlockEvalDto =
        {
            CeBlock = CeBlockDto.fromDomain eval.CeBlock
            // We store the raw array from the container
            CeUseCounts = eval.UseCountArray
            UnsortedCount = %eval.UnsortedCount
            SortableTests = 
                eval.SortableTests
                |> Option.map SortableTestsDto.fromDomain
        }

    let toDomain (dto: ceBlockEvalDto) : ceBlockEval =
        let ceb = CeBlockDto.toDomain dto.CeBlock
        let counts = ceUseCounts.CreateFromArray dto.CeUseCounts
        let tests = dto.SortableTests |> Option.map SortableTestsDto.toDomain
        let unsortedCount = dto.UnsortedCount |> UMX.tag<sortableCount>
        
        // The factory recreates the cached used CEs.
        ceBlockEval.create ceb counts unsortedCount tests
