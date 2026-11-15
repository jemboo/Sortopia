namespace Sortopia.Models.Sorting.Simple

open Sortopia.Sorting

/// Existing 99% callers share the consolidated probability tables and estimation policy.
module SimpleSorterStageCounts =
    type recommendation = SimpleSorterStageLengths.recommendation

    let tryFullSort99 model channels = SimpleSorterStageLengths.tryFullSort99 model channels
    let tryHalfMerge99 model channels = SimpleSorterStageLengths.tryHalfMerge99 model channels

    /// Unprefixed full sorting. IsEstimate=false denotes a reference row, not a probability guarantee.
    let fullSortDefault model channels = SimpleSorterStageLengths.recommend 99 channels 0<stageLength> model
    let halfMergeDefault model channels = SimpleSorterStageLengths.recommendHalfMerge99 model channels
