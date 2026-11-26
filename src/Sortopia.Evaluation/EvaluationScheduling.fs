namespace Sortopia.Evaluation

open System
open System.Threading
open System.Threading.Tasks

/// A phase owns its workers until all of its parallel work has joined.
type IEvaluationCpuLease =
    inherit IDisposable
    abstract WorkerCount: int

/// A running file's share of the batch CPU budget.
type IEvaluationCpuBudget =
    inherit IDisposable
    abstract Acquire: requestedWorkers: int * cancellationToken: CancellationToken -> IEvaluationCpuLease

type private BudgetedParallelOptions(budget: IEvaluationCpuBudget) =
    inherit ParallelOptions()
    member _.Budget = budget

module EvaluationScheduling =
    let options maxWorkers budget token =
        let options =
            match budget with
            | Some budget -> BudgetedParallelOptions(budget) :> ParallelOptions
            | None -> ParallelOptions()
        options.MaxDegreeOfParallelism <- maxWorkers
        options.CancellationToken <- token
        options

    /// Snapshot the allocation at a phase boundary. Passing ordinary options to
    /// nested helpers prevents double acquisition and changing a running loop.
    let internal run workCount (options: ParallelOptions) action =
        options.CancellationToken.ThrowIfCancellationRequested()
        match options with
        | :? BudgetedParallelOptions as scheduled when workCount > 0 ->
            let maximum = if options.MaxDegreeOfParallelism < 1 then Environment.ProcessorCount else options.MaxDegreeOfParallelism
            use lease = scheduled.Budget.Acquire(min workCount maximum, options.CancellationToken)
            action (ParallelOptions(MaxDegreeOfParallelism = lease.WorkerCount,
                                    CancellationToken = options.CancellationToken, TaskScheduler = options.TaskScheduler))
        | _ -> action options
