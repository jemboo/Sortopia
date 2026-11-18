// Run against either build using dotnet fsi --lib:<CLI Release directory> Evaluation.fsx -- ...
// Reads saved inputs/results; writes only the explicitly selected JSON report.
#r "FSharp.UMX.dll"
#r "Sortopia.Core.dll"
#r "Sortopia.Core.Serialization.dll"
#r "Sortopia.Sorting.dll"
#r "Sortopia.Models.Sorting.dll"
#r "Sortopia.Evaluation.dll"
#r "Sortopia.Runs.dll"
#r "Sortopia.Functions.dll"
#r "Sortopia.Workspaces.dll"
#r "Sortopia.FileDb.dll"

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open System.Runtime.Intrinsics
open System.Text.Json
open FSharp.UMX
open Sortopia.Core
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Runs
open Sortopia.Functions
open Sortopia.FileDb
open Sortopia.Evaluation

let arg name =
    fsi.CommandLineArgs |> Array.tryFindIndex ((=) name) |> Option.map (fun i -> fsi.CommandLineArgs[i + 1])
    |> Option.defaultWith (fun () -> failwith $"Missing {name}.")
let folder, outputPath, label = arg "--project", arg "--output", arg "--label"
let token = CancellationToken.None
let item = RunParameterFiles.listWorkItems (Path.Combine(folder, "runParameters")) token |> Array.exactlyOne
let store = SortopiaFileDb(folder, item.ProjectId) :> ISortopiaDb
let phases = ResizeArray<obj>()
let phase name action =
    let allocated = GC.GetTotalAllocatedBytes true
    let timer = Stopwatch.StartNew()
    let result = action ()
    timer.Stop()
    phases.Add(box {| Phase = name; Milliseconds = timer.Elapsed.TotalMilliseconds; AllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocated |})
    printfn "%s %s %.1f ms" label name timer.Elapsed.TotalMilliseconds
    result
let load reference = match store.Load(reference, token) with Ok data -> data.Value | Error error -> failwith error
let source = phase "Load source model set" (fun () ->
    match item.SetupInputs |> List.find (fun reference -> reference.DataType.IsSorterModelSet) |> load with
    | functionData.SorterModelSet value -> value | _ -> failwith "Expected models.")
let tests, prefix = phase "Load tests" (fun () ->
    match item.SetupInputs |> List.find (fun reference -> reference.DataType.IsSortableTests) |> load with
    | functionData.SortableTests (value, prefix) -> value, prefix | _ -> failwith "Expected tests.")
let bools = match tests with sortableTests.Bools value -> value | _ -> failwith "This benchmark expects the saved Boolean test source."
let width = bools.ChannelCount
let prefixBlock = ceBlock.create (UMX.tag Guid.Empty) width prefix
let converted = phase "Prepare SIMD inputs" (fun () ->
    let arrays = bools.SortableBinaryArrays
    let ints = arrays |> Array.map (fun a -> sortableIntArray.create(a.Values |> Array.map (fun b -> if b then 1 else 0), width, 2<symbolSetSize>))
    [| "Bitv512", (SortableBitv512Tests.fromBoolArrays bools.Id width arrays |> sortableTests.Bitv512)
       "Uint8v256", (SortableUint8v256Tests.fromIntArrays bools.Id width ints |> sortableTests.Uint8v256)
       "Uint8v512", (SortableUint8v512Tests.fromIntArrays bools.Id width ints |> sortableTests.Uint8v512) |])
let seeds = phase "Select 32 seeds" (fun () -> source.SorterModels |> Array.take 32)
let plan = FileResultsHost.plan item.ProjectId item.Results item.Parameters item.SetupInputs
let final = match load plan[sortopiaDataType.SorterPoolSet ""].Key.Reference with functionData.SorterPoolSet value -> value | _ -> failwith "Expected completed SGD pools."
let networks models (cutoff: int) = models |> Array.map (fun model ->
    let sorter = SorterModel.makeSorterWithoutPrefix model (Some (UMX.tag cutoff))
    ceBlock.create (UMX.tag (UMX.untag sorter.SorterId)) width sorter.Ces)
let initialNetworks = phase "Build initial networks" (fun () -> networks seeds (int source.RawCeLength))
let finalNetworks = phase "Build final networks" (fun () -> networks (final.Pools[0].Members |> Array.map _.Model) (int (float final.Pools[0].RawCeLength * 1.1)))
let p = item.Parameters
let required key = FunctionParameters.required key p
let mutator = SimpleSorterModelMutator.getMsuf4ModelMutator width
                  (required ParamKeys.rngTypeKey |> RngType.fromString |> RngFactory.create) (UMX.tag true)
                  (required ParamKeys.seedModificationRateKey |> UMX.tag) (required ParamKeys.modificationRateKey |> UMX.tag)
                  (required ParamKeys.orthoRateKey |> UMX.tag) (required ParamKeys.paraRateKey |> UMX.tag) (required ParamKeys.selfSymRateKey |> UMX.tag)
                  (required ParamKeys.mutatorVariantKey |> MutatorVariant.fromString) |> sorterModelMutator.Simple
phase "Mutate 32 seeds" (fun () -> seeds |> Array.map (fun model -> SorterModelMutator.makeMutantSorterModelFromIndexAndMod mutator model (UMX.tag 0) (UMX.tag 0))) |> ignore

let benchmarkProcess = Process.GetCurrentProcess()
let measure action =
    let warmup = Stopwatch.StartNew()
    action ()
    warmup.Stop()
    let iterations = max 1 (min 30 (int (Math.Ceiling(120.0 / max 0.1 warmup.Elapsed.TotalMilliseconds))))
    Array.init 3 (fun _ ->
        let before = GC.GetTotalAllocatedBytes true
        let cpu = benchmarkProcess.TotalProcessorTime
        let timer = Stopwatch.StartNew()
        for _ in 1 .. iterations do action ()
        timer.Stop()
        {| Milliseconds = timer.Elapsed.TotalMilliseconds / float iterations
           AllocatedBytes = float (GC.GetTotalAllocatedBytes(true) - before) / float iterations
           CpuMilliseconds = (benchmarkProcess.TotalProcessorTime - cpu).TotalMilliseconds / float iterations |})
    |> Array.sortBy _.Milliseconds |> fun samples -> samples[1]
let rows = ResizeArray<obj>()
for name, nets in [|"Initial32", initialNetworks; "Final32", finalNetworks|] do
    let oracle = CeBlockOps.evalWithSorterTestsWithOptions (ParallelOptions(MaxDegreeOfParallelism = 1)) tests prefixBlock nets (UMX.tag false)
    for format, inputs in Array.append [|"Bools", tests|] converted do
        for workers in [1; 2; 4; 8; 16] do
            let options = ParallelOptions(MaxDegreeOfParallelism = workers)
            let evaluate () = CeBlockOps.evalWithSorterTestsWithOptions options inputs prefixBlock nets (UMX.tag false)
            let actual = evaluate ()
            for expected, value in Array.zip oracle actual do
                if expected.UseCountArray <> value.UseCountArray || expected.UnsortedCount <> value.UnsortedCount then
                    failwith "Evaluation changed."
            let timing = measure (fun () -> evaluate () |> ignore)
            rows.Add(box {| Workload = name; Format = format; Workers = workers; Timing = timing |})
            printfn "%s %s %s workers=%d %.3f ms %.0f bytes" label name format workers timing.Milliseconds timing.AllocatedBytes
let bitTests = converted |> Array.pick (function _, sortableTests.Bitv512 value -> Some value | _ -> None)
let large = sortableBitv512Tests.create bitTests.Id width (Array.init 64 (fun _ -> bitTests.SimdSortBlocks) |> Array.concat) |> sortableTests.Bitv512
for workers in [1; 2; 4; 8; 16] do
    let timing = measure (fun () -> CeBlockOps.evalWithSorterTestsWithOptions (ParallelOptions(MaxDegreeOfParallelism = workers)) large prefixBlock initialNetworks[..0] (UMX.tag false) |> ignore)
    rows.Add(box {| Workload = "OneNetworkLargeTests"; Format = "Bitv512"; Workers = workers; Timing = timing |})
    printfn "%s OneNetworkLargeTests workers=%d %.3f ms" label workers timing.Milliseconds
let report = {| Label = label; ProcessorCount = Environment.ProcessorCount; Vector512Accelerated = Vector512.IsHardwareAccelerated
                Vector256Accelerated = Vector256.IsHardwareAccelerated; Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
                SourceModels = source.Count; TestCount = int bools.SortableCount; Phases = phases.ToArray(); Evaluations = rows.ToArray() |}
File.WriteAllText(outputPath, JsonSerializer.Serialize(report, JsonSerializerOptions(WriteIndented = true)))
