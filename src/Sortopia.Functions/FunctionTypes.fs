namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation

/// In-memory data accepted and returned by the initial catalog functions.
type functionData =
    | SortableTests of sortableTests * ce array
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval

type resultsFunction = parameterSet -> functionData list -> Result<functionData list, string>
type setupFunction = parameterSet -> functionData list -> Result<functionData list, string>

module internal FunctionParameters =
    let required (key: paramKey<'value>) (parameters: parameterSet) =
        parameters.TryFind key
        |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing or incorrectly typed parameter: {key.Name}.")
