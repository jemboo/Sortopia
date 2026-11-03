namespace Sortopia.Models.Sorting.Simple

open Sortopia.Models.Sorting
open Sortopia.Sorting

/// Stage counts at 99% sorter success, from GeneSort commit 79fbce09,
/// GeneSort.Project/Params/StageLength.fs, rows marked SortProb 0.99.
module SimpleSorterStageCounts =
    // Each pair is (channels, stages), ordered by channel count for each method.
    let private fullSort =
        Map.ofList [
            Msce,  [ 4, 6;  6, 20; 8, 40; 12, 71; 16, 112; 24, 187 ]
            Msrs,  [ 4, 12; 6, 19; 8, 51; 12, 77; 16, 149; 24, 291 ]
            Mssi,  [ 4, 12; 6, 19; 8, 49; 12, 75; 16, 133; 24, 215 ]
            Msuf4, [ 4, 13; 8, 51; 16, 180 ]
            Msuf6, [ 6, 19; 12, 79; 24, 388 ]
        ]

    let private halfMerge =
        Map.ofList [
            Msce,  [ 32, 290; 48, 445; 64, 645; 96, 933;  128, 1404; 192, 2220; 256, 2700; 384, 4793 ]
            Msrs,  [ 32, 351; 48, 615; 64, 853; 96, 1482; 128, 1824; 192, 2976; 256, 3717; 384, 5873 ]
            Mssi,  [ 32, 289; 48, 467; 64, 714; 96, 981;  128, 1404; 192, 2132; 256, 3304; 384, 4884 ]
            Msuf4, [ 32, 398; 64, 1901; 128, 6818; 256, 21553 ]
            Msuf6, [ 48, 984; 96, 4600; 192, 16480; 384, 46606 ]
        ]

    let private tryMeasured table model (channels: int<channelCount>) =
        Map.find model table
        |> List.tryFind (fun (width, _) -> width = int channels)
        |> Option.map (fun (_, stages) -> stages * 1<stageLength>)

    /// Sort arbitrary binary inputs at the given width.
    let tryFullSort99 model channels = tryMeasured fullSort model channels

    /// Merge two already-sorted halves of the given total width.
    let tryHalfMerge99 model channels = tryMeasured halfMerge model channels

    type recommendation = { Stages: int<stageLength>; IsEstimate: bool }

    /// Interpolate between adjacent measured widths, or extend the nearest end segment.
    let private recommend table model channels =
        match tryMeasured table model channels with
        | Some stages -> { Stages = stages; IsEstimate = false }
        | None ->
            let width = int channels
            let segments = List.pairwise table[model]
            let (w1, s1), (w2, s2) =
                segments
                |> List.tryFind (fun (_, (upper, _)) -> width <= upper)
                |> Option.defaultWith (fun () -> List.last segments)
            let estimate = float s1 + float (width - w1) * float (s2 - s1) / float (w2 - w1)
            { Stages = max 1 (int (ceil estimate)) * 1<stageLength>; IsEstimate = true }

    let fullSortDefault model channels = recommend fullSort model channels

    let halfMergeDefault model channels = recommend halfMerge model channels
