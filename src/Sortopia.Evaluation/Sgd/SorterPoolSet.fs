namespace Sortopia.Evaluation.Sgd

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Evaluation

/// A completed, evaluated member. MutationIndex advances even when its children lose.
type sorterPoolMember = {
    Id: Guid<sorterPoolMemberId>
    Model: sorterModel
    MutationIndex: int
    MutationMod: int
    Birthday: int
    ParentId: Guid<sorterPoolMemberId> option
    Evaluation: sorterEval
}

type sorterPool = {
    Id: Guid<sorterPoolId>
    RawCeLength: int
    Members: sorterPoolMember array
}

/// Population at one generation; model prefixes and mutation cursors are preserved.
/// Sampled history is stored separately as SorterPoolSummarySet results.
type sorterPoolSet = {
    Generation: int
    TestsId: Guid<sortableTestsId>
    Pools: sorterPool array
}

module SorterPoolSet =
    /// Compact legacy evaluations without changing models, identities or cursors.
    let toV1 (value: sorterPoolSet) =
        let pools = value.Pools |> Array.map (fun pool ->
            if pool.Members |> Array.forall (fun m -> match m.Evaluation with sorterEval.V1 _ -> true | _ -> false) then pool
            else
                let members = pool.Members |> Array.map (fun m -> { m with Evaluation = SorterEval.toV1 m.Evaluation })
                { pool with Members = members })
        { value with Pools = pools }

    let validate (value: sorterPoolSet) =
        if value.Generation < 0 || %value.TestsId = Guid.Empty then invalidArg "value" "Invalid SGD generation or tests identity."
        if isNull value.Pools || value.Pools.Length = 0 then invalidArg "value" "An SGD result needs pools."
        let unique name ids =
            if ids |> Array.contains Guid.Empty || (Array.distinct ids).Length <> ids.Length then
                invalidArg "value" $"Invalid or duplicate {name} identities."
        unique "pool" (value.Pools |> Array.map (fun pool -> %pool.Id))
        for pool in value.Pools do
            if pool.RawCeLength < 0 || isNull pool.Members || pool.Members.Length = 0 then invalidArg "value" "Invalid SGD pool."
            unique "member" (pool.Members |> Array.map (fun memberValue -> %memberValue.Id))
            for memberValue in pool.Members do
                if memberValue.MutationIndex < 0 || memberValue.Birthday < 0 || memberValue.Birthday > value.Generation then
                    invalidArg "value" "Invalid SGD mutation cursor or birthday."
                if SorterModel.getChannelCount memberValue.Model <> SorterEval.getChannelCount memberValue.Evaluation then
                    invalidArg "value" "SGD model and evaluation widths differ."
        value
