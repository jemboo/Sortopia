namespace Sortopia.Evaluation

open System.Threading.Tasks

module internal EvaluationParallel =
    /// One owner per output slot. Callers finish this phase before starting another
    /// parallel phase, so mutation, materialization and evaluation share one budget.
    let mapIndexed (options: ParallelOptions) transform (items: 'a[]) =
        options.CancellationToken.ThrowIfCancellationRequested()
        if items.Length <= 1 || options.MaxDegreeOfParallelism = 1 then
            items |> Array.mapi (fun index item ->
                options.CancellationToken.ThrowIfCancellationRequested()
                transform index item)
        else
            let results = Array.zeroCreate items.Length
            Parallel.For(0, items.Length, options, fun index ->
                results[index] <- transform index items[index]) |> ignore
            results

    let map options transform items = mapIndexed options (fun _ item -> transform item) items
