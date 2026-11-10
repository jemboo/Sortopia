namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Sorting.Inputs
open Sortopia.Models.Sorting
open Sortopia.Evaluation
open FSharp.UMX

[<Measure>] type sortopiaDataId

/// A portable reference; paths are resolved from the workspace's project IDs.
type storedDataReference = {
    ProjectId: Guid<projectId>
    Id: Guid<sortopiaDataId>
    QueryId: Guid<sortopiaDataId>
    IsSetup: bool
    DataType: sortopiaDataType
}

type storedOutput = { Reference: storedDataReference; Reused: bool }

/// In-memory data accepted and returned by the initial catalog functions.
type functionData =
    | SortableTests of sortableTests * ce array
    | SorterModelSet of sorterModelSet
    | SorterSetEval of sorterSetEval

type resultsFunction = parameterSet -> functionData list -> Result<functionData list, string>

/// A function declares which upstream types affect an output's identity. Pure
/// generators use an empty dependency list. Each factory runs only on a miss.
type ISetupDataContext =
    abstract GetOrCreate: outputKind: string * sourceKinds: string list * factory: (unit -> Result<functionData, string>) -> Result<functionData, string>

type setupFunction = ISetupDataContext -> parameterSet -> functionData list -> Result<functionData list, string>

module SetupDataContext =
    let transient = { new ISetupDataContext with member _.GetOrCreate(_, _, factory) = factory () }

module FunctionData =
    let kind = function
        | functionData.SortableTests _ -> "SortableTests"
        | functionData.SorterModelSet _ -> "SorterModelSet"
        | functionData.SorterSetEval _ -> "SorterSetEval"

module internal FunctionParameters =
    let required (key: paramKey<'value>) (parameters: parameterSet) =
        parameters.TryFind key
        |> Option.defaultWith (fun () -> invalidArg "parameters" $"Missing or incorrectly typed parameter: {key.Name}.")
