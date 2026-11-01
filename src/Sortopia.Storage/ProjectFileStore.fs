namespace Sortopia.Storage

open System
open System.Collections.Generic
open System.IO
open System.Text.Json

type RunParameterRef = { RunId: Guid; ParameterId: Guid }

/// A project-relative address. The parameter reference identifies the producer.
type ResultRef = { Parameters: RunParameterRef; DataType: outputDataType; DataId: Guid }

type ProjectFileStore private (runsDirectory: DirectoryInfo) =
    let runsFolder = runsDirectory.FullName
    let runFolder (id: Guid) = Path.Combine(runsFolder, id.ToString("N"))
    let definitionFile id = Path.Combine(runFolder id, "run.json")
    let parameterFolder runId = Path.Combine(runFolder runId, "parameters")
    let parameterFile p = Path.Combine(parameterFolder p.RunId, p.ParameterId.ToString("N") + ".json")

    let resultTypeFolder runId (kind: outputDataType) =
        if not (Enum.IsDefined(kind)) then invalidArg "kind" "Unknown Result data type."
        Path.Combine(runFolder runId, "results", kind.ToString())

    let resultFile r =
        Path.Combine(resultTypeFolder r.Parameters.RunId r.DataType,
                     r.Parameters.ParameterId.ToString("N"), r.DataId.ToString("N") + ".mpk")

    let writeNew (path: string) (bytes: byte[]) =
        Directory.CreateDirectory(Path.GetDirectoryName(path)) |> ignore
        let temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"
        try
            File.WriteAllBytes(temp, bytes)
            File.Move(temp, path, overwrite = false)
        finally
            File.Delete(temp)

    let guidFiles folder extension =
        seq {
            if Directory.Exists(folder) then
                for path in Directory.EnumerateFiles(folder, "*" + extension) do
                    match Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N") with
                    | true, id -> yield id
                    | _ -> ()
        }

    new(projectFolder: string) = ProjectFileStore(DirectoryInfo(Path.Combine(Path.GetFullPath(projectFolder), "runs")))

    static member FromRunsFolder(folder: string) = ProjectFileStore(DirectoryInfo(folder))

    member _.SaveRunDefinition<'T>(runId: Guid, definition: 'T) =
        JsonSerializer.SerializeToUtf8Bytes(definition, JsonSerializerOptions(WriteIndented = true))
        |> writeNew (definitionFile runId)

    member _.LoadRunDefinition<'T>(runId: Guid) : 'T =
        use stream = File.OpenRead(definitionFile runId)
        JsonSerializer.Deserialize<'T>(stream)

    member _.ListRunDefinitionIds() =
        [| if Directory.Exists(runsFolder) then
               for folder in Directory.EnumerateDirectories(runsFolder) do
                   match Guid.TryParseExact(Path.GetFileName(folder), "N") with
                   | true, id when File.Exists(definitionFile id) -> yield id
                   | _ -> () |]
        |> Array.sort

    member _.SaveParameters(reference: RunParameterRef, values: Map<string, string>) =
        ArgumentNullException.ThrowIfNull(values)
        if values |> Map.exists (fun k v -> isNull k || isNull v) then
            invalidArg "values" "Run parameter keys and values must be strings."
        JsonSerializer.SerializeToUtf8Bytes(values) |> writeNew (parameterFile reference)

    member _.LoadParameters(reference: RunParameterRef) : Map<string, string> =
        use stream = File.OpenRead(parameterFile reference)
        let values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
        if isNull values || values.ContainsValue(null) then
            raise (InvalidDataException("Expected an object with string parameter values."))
        values |> Seq.map (fun pair -> pair.Key, pair.Value) |> Map.ofSeq

    member _.ListParameters(runId: Guid) =
        guidFiles (parameterFolder runId) ".json"
        |> Seq.sort
        |> Seq.map (fun id -> { RunId = runId; ParameterId = id })
        |> Seq.toArray

    member _.SaveResult(parameters: RunParameterRef, data: ResultData) =
        let path = parameterFile parameters
        if not (File.Exists(path)) then
            raise (FileNotFoundException("Save the producing run parameters before their Results.", path))
        let reference = { Parameters = parameters; DataType = data.DataType; DataId = data.Id }
        ResultCodec.encode data |> writeNew (resultFile reference)
        reference

    member _.LoadResult(reference: ResultRef) =
        let path = resultFile reference
        let data = File.ReadAllBytes(path) |> ResultCodec.decode reference.DataType
        if data.Id <> reference.DataId then
            raise (InvalidDataException($"Result ID does not match its file at '{path}'."))
        data

    member _.ListResults(runId: Guid) =
        [| for kind in Enum.GetValues<outputDataType>() do
               let folder = resultTypeFolder runId kind
               if Directory.Exists(folder) then
                   for owner in Directory.EnumerateDirectories(folder) do
                       match Guid.TryParseExact(Path.GetFileName(owner), "N") with
                       | true, parameterId ->
                           let parameters = { RunId = runId; ParameterId = parameterId }
                           for id in guidFiles owner ".mpk" do
                               yield { Parameters = parameters; DataType = kind; DataId = id }
                       | _ -> () |]
        |> Array.sort
