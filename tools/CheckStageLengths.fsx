// Run from the repository after building Sortopia.Models.Tests in Release:
// dotnet fsi tools/CheckStageLengths.fsx
#I "../tests/Sortopia.Models.Tests/bin/Release/net10.0"
#r "FSharp.UMX.dll"
#r "Sortopia.Core.dll"
#r "Sortopia.Permutations.dll"
#r "Sortopia.Sorting.Catalog.dll"
#r "Sortopia.Sorting.dll"
#r "Sortopia.Models.Sorting.dll"
open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Sorting
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple

// Exact all-input checks at four channels; independent deterministic model IDs.
// This measures whole-network success, not the fraction of inputs sorted correctly.
let trials = 10000
for kind in [simpleSorterModelType.Msce; simpleSorterModelType.Mssi; simpleSorterModelType.Msrs; simpleSorterModelType.Msuf4] do
    let generator = SimpleSorterModelGen.makeUniform rngFactory.SmxFactory 4<channelCount> [||] 100<stageLength> kind (UMX.tag true)
    let measure index =
        let id = Guid(index, 2026s, 1115s, [|1uy;2uy;3uy;4uy;5uy;6uy;7uy;8uy|]) |> UMX.tag<sorterModelId>
        let model = SimpleSorterModelGen.makeSorterModelFromId id generator
        let stages = (RandomNetwork.toStagesWithoutPrefix model).Stages
        let rows = [|0xAAAAUL; 0xCCCCUL; 0xF0F0UL; 0xFF00UL|]
        let mutable comparisons, depth, answer = 0, 0, 0
        for stage in stages do
            depth <- depth + 1
            for ce in stage.Ces do
                comparisons <- comparisons + 1
                let lo, hi = int ce.LowChannel, int ce.HighChannel
                let a, b = rows[lo], rows[hi]
                rows[lo] <- a &&& b
                rows[hi] <- a ||| b
                if answer = 0 && ([0..2] |> List.forall (fun i -> rows[i] &&& ~~~rows[i+1] = 0UL)) then
                    answer <- if kind = simpleSorterModelType.Msce then (comparisons + 1) / 2 else depth
        if answer = 0 then failwith "Calibration budget exhausted."
        answer
    let lengths = Array.init trials measure |> Array.sort
    printfn "%A: p50=%d p90=%d p99=%d (n=%d, exhaustive inputs)" kind lengths[4999] lengths[8999] lengths[9899] trials
