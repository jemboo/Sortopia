namespace Sortopia.Evaluation.Sgd

open System
open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Evaluation

/// A completed, evaluated member. MutationIndex advances even when its children lose.
type sorterPoolMember = {
    Id: Guid
    Model: sorterModel
    MutationIndex: int
    MutationMod: int
    Birthday: int
    ParentId: Guid option
    Evaluation: sorterEval
}

type sorterPool = {
    Id: Guid
    RawCeLength: int
    Members: sorterPoolMember array
}

type sorterPoolSummary = {
    Generation: int
    PoolIndex: int
    MemberCount: int
    SuccessfulCount: int
    RawCeLength: int
    MinimumCeLength: int
    MinimumStageLength: int
    BestScore: float
}

/// Final population and scalar history; model prefixes and mutation cursors are preserved.
type sorterPoolSet = {
    Generation: int
    TestsId: Guid<sortableTestsId>
    Pools: sorterPool array
    History: sorterPoolSummary array
}

module SorterPoolSet =
    let validate (value: sorterPoolSet) =
        if value.Generation < 0 || %value.TestsId = Guid.Empty then invalidArg "value" "Invalid SGD generation or tests identity."
        if isNull value.Pools || value.Pools.Length = 0 || isNull value.History then invalidArg "value" "An SGD result needs pools and history."
        let unique name ids =
            if ids |> Array.contains Guid.Empty || (Array.distinct ids).Length <> ids.Length then
                invalidArg "value" $"Invalid or duplicate {name} identities."
        unique "pool" (value.Pools |> Array.map _.Id)
        for pool in value.Pools do
            if pool.RawCeLength < 0 || isNull pool.Members || pool.Members.Length = 0 then invalidArg "value" "Invalid SGD pool."
            unique "member" (pool.Members |> Array.map _.Id)
            for memberValue in pool.Members do
                if memberValue.MutationIndex < 0 || memberValue.Birthday < 0 || memberValue.Birthday > value.Generation then
                    invalidArg "value" "Invalid SGD mutation cursor or birthday."
                if SorterModel.getChannelCount memberValue.Model <> SorterEval.getChannelCount memberValue.Evaluation then
                    invalidArg "value" "SGD model and evaluation widths differ."
        for summary in value.History do
            if summary.Generation < 0 || summary.Generation > value.Generation || summary.PoolIndex < 0 || summary.PoolIndex >= value.Pools.Length
               || summary.MemberCount <= 0 || summary.SuccessfulCount < 0 || summary.SuccessfulCount > summary.MemberCount
               || summary.RawCeLength < 0 || summary.MinimumCeLength < 0 || summary.MinimumStageLength < 0 || not (Double.IsFinite summary.BestScore) then
                invalidArg "value" "Invalid SGD history."
        value
