namespace Sortopia.FileDb

open System
open System.Threading
open Sortopia.Runs
open Sortopia.Functions

/// The same exact key is used for completion checks and publication.
type plannedOutput = { Key: dataKey; Dependencies: storedDataReference list }

type outputState = Pending = 0 | Partial = 1 | Complete = 2 | Invalid = 3

type outputInspection = {
    State: outputState
    Available: storedOutput list
    ExpectedCount: int
    Error: string
}

module OutputPlan =
    let create projectId databaseName isSetup functionName version
               (outputs: Map<sortopiaDataType, _>) (dependencies: Map<sortopiaDataType, sortopiaDataType list>)
               parameters (inputs: storedDataReference list) =
        if (outputs |> Map.toList |> List.map fst) <> (dependencies |> Map.toList |> List.map fst) then
            invalidArg "dependencies" "Output dependencies must match the declared output types exactly."
        let expected = dependencies |> Map.toList |> List.collect snd |> List.distinct |> List.sort
        if expected <> (inputs |> List.map _.DataType |> List.sort) then
            invalidArg "inputs" "Supply exactly one reference for each declared input type."
        let references = inputs |> List.map (fun reference -> reference.DataType, reference) |> Map.ofList
        outputs |> Map.map (fun dataType keys ->
            let inputs = dependencies[dataType] |> List.map (fun input -> references[input]) |> List.sort
            { Key = DataKey.create projectId databaseName isSetup functionName version dataType keys parameters inputs
              Dependencies = inputs })

    /// Existence alone is insufficient: TryLoad validates identity, schema,
    /// checksum and payload. Invalid data is never treated as a cache miss.
    let inspect (store: ISortopiaDb) (plan: Map<sortopiaDataType, plannedOutput>) (token: CancellationToken) =
        let available = ResizeArray<storedOutput>()
        let errors = ResizeArray<string>()
        for KeyValue(dataType, output) in plan do
            token.ThrowIfCancellationRequested()
            match store.TryLoad(output.Key, token) with
            | Ok (Some value) -> available.Add { Reference = value.Reference; Reused = true }
            | Ok None -> ()
            | Error message -> errors.Add $"{dataType}: {message}"
        token.ThrowIfCancellationRequested()
        { State = if errors.Count > 0 then outputState.Invalid
                  elif available.Count = plan.Count then outputState.Complete
                  elif available.Count = 0 then outputState.Pending else outputState.Partial
          Available = List.ofSeq available; ExpectedCount = plan.Count; Error = String.Join(Environment.NewLine, errors) }
