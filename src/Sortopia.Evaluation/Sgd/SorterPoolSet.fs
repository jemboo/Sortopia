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

/// Population at one generation; model prefixes and mutation cursors are preserved.
/// Sampled history is stored separately as SorterPoolSummarySet results.
type sorterPoolSet = {
    Generation: int
    TestsId: Guid<sortableTestsId>
    Pools: sorterPool array
}

module SorterPoolSet =
    let validate (value: sorterPoolSet) =
        if value.Generation < 0 || %value.TestsId = Guid.Empty then invalidArg "value" "Invalid SGD generation or tests identity."
        if isNull value.Pools || value.Pools.Length = 0 then invalidArg "value" "An SGD result needs pools."
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
        value
