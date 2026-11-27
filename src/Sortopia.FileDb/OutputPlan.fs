namespace Sortopia.FileDb

open System
open System.Buffers
open System.IO
open System.Security.Cryptography
open System.Threading
open Sortopia.Runs
open Sortopia.Functions

/// The same exact key is used for completion checks and publication.
type plannedOutput = { Key: dataKey; Validate: sortopiaStorageData -> unit }

type outputState = Pending = 0 | Partial = 1 | Complete = 2 | Invalid = 3

type outputInspection = {
    State: outputState
    Available: storedOutput list
    ExpectedCount: int
    Error: string
}

module OutputPlan =
    // A full validation can be reused within one execution only while every
    // output's bytes still match. Hash streams with bounded memory; dates and
    // file sizes cannot detect same-size corruption or replacement.
    let internal fingerprints (store: SortopiaFileDb) (plan: Map<runDataOutput, plannedOutput>) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        let paths = plan |> Map.toArray |> Array.map (fun (_, output) -> store.GetPath output.Key.Reference)
        if paths |> Array.exists (File.Exists >> not) then None
        else
            let buffer = ArrayPool<byte>.Shared.Rent(64 * 1024)
            try
                try
                    let hashes = paths |> Array.map (fun path ->
                        token.ThrowIfCancellationRequested()
                        use stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                        use hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
                        let mutable count = stream.Read(buffer, 0, buffer.Length)
                        while count <> 0 do
                            token.ThrowIfCancellationRequested()
                            hash.AppendData(buffer, 0, count)
                            count <- stream.Read(buffer, 0, buffer.Length)
                        hash.GetHashAndReset())
                    token.ThrowIfCancellationRequested()
                    Some hashes
                with :? IOException | :? UnauthorizedAccessException -> None
            finally ArrayPool<byte>.Shared.Return buffer

    let bind projectId databaseName isSetup functionName version (outputs: (runDataOutput * (sortopiaStorageData -> unit)) list) =
        outputs |> List.map (fun (output, validate) ->
            let keys = output.QueryParameters.ParamMap |> Map.toList |> List.map fst
            output, { Key = DataKey.create projectId databaseName isSetup functionName version output.DataType keys output.QueryParameters output.Dependencies
                      Validate = validate }) |> Map.ofList

    /// File integrity only; used to display availability without materializing
    /// every saved model and snapshot. Execution retains the full inspect path.
    let inspectIntegrity (store: SortopiaFileDb) (keys: dataKey array) (token: CancellationToken) =
        let available, errors = ResizeArray<storedOutput>(), ResizeArray<string>()
        for key in keys do
            token.ThrowIfCancellationRequested()
            match store.CheckIntegrity(key, token) with
            | Ok (Some reference) -> available.Add { Reference = reference; Reused = true }
            | Ok None -> ()
            | Error error -> errors.Add $"{key.Reference.DataType}: {error}"
        token.ThrowIfCancellationRequested()
        { State = if errors.Count > 0 then outputState.Invalid
                  elif available.Count = keys.Length then outputState.Complete
                  elif available.Count = 0 then outputState.Pending else outputState.Partial
          Available = List.ofSeq available; ExpectedCount = keys.Length; Error = String.Join(Environment.NewLine, errors) }

    /// Project query projections are explicit, independent of catalog parameter contributions.
    let create projectId databaseName isSetup functionName version
               (outputs: functionOutputDefinition list) (queryProjections: Map<sortopiaDataType, _>)
               parameters (inputs: storedDataReference list) =
        if (outputs |> List.map _.DataType |> List.sort) <> (queryProjections |> Map.toList |> List.map fst) then
            invalidArg "queryProjections" "Query projections must match the declared output types exactly."
        let expected = outputs |> List.collect _.Dependencies |> List.distinct |> List.sort
        if expected <> (inputs |> List.map _.DataType |> List.sort) then
            invalidArg "inputs" "Supply exactly one reference for each declared input type."
        let references = inputs |> List.map (fun reference -> reference.DataType, reference) |> Map.ofList
        outputs |> List.map (fun output ->
            let inputs = output.Dependencies |> List.map (fun input -> references[input])
            RunDataOutput.create output.DataType queryProjections[output.DataType] parameters inputs, ignore)
        |> bind projectId databaseName isSetup functionName version

    /// Existence alone is insufficient: TryLoad validates identity, schema,
    /// checksum and payload. Invalid data is never treated as a cache miss.
    let inspect (store: ISortopiaDb) (plan: Map<runDataOutput, plannedOutput>) (token: CancellationToken) =
        let available = ResizeArray<storedOutput>()
        let errors = ResizeArray<string>()
        for KeyValue(descriptor, output) in plan do
            token.ThrowIfCancellationRequested()
            match store.TryLoad(output.Key, token) with
            | Ok (Some value) ->
                try
                    output.Validate value.Value
                    available.Add { Reference = value.Reference; Reused = true }
                with
                | :? OperationCanceledException -> reraise ()
                | error -> errors.Add $"{descriptor.DataType}: {error.Message}"
            | Ok None -> ()
            | Error message -> errors.Add $"{descriptor.DataType}: {message}"
        token.ThrowIfCancellationRequested()
        { State = if errors.Count > 0 then outputState.Invalid
                  elif available.Count = plan.Count then outputState.Complete
                  elif available.Count = 0 then outputState.Pending else outputState.Partial
          Available = List.ofSeq available; ExpectedCount = plan.Count; Error = String.Join(Environment.NewLine, errors) }
