namespace Sortopia.FileDb

open System
open System.IO
open System.Threading
open FSharp.UMX
open Sortopia.Runs
open Sortopia.Functions

/// Plan cleanup without reading bulky payloads or requiring an installed executor.
/// Project data is shared: retain queries used by surviving runs and parameter files.
module RunDataCleanup =
    let plan folder projectId (removed: Set<Guid<runId>>) keepData (token: CancellationToken) =
        let deleted = ResizeArray<string>()
        let references = Collections.Generic.HashSet<Guid<sortopiaDataId>>()
        let temporaryReferences = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
        let check (path: string) =
            if File.GetAttributes(path).HasFlag FileAttributes.ReparsePoint then
                invalidOp $"Cannot clean up linked run data: {path}"
        let parameters = Path.Combine(folder, "runParameters")
        if Directory.Exists parameters then
            check parameters
            for path in Directory.GetFiles(parameters, "*.msgpack") do
                token.ThrowIfCancellationRequested()
                check path
                let item = RunParameterFiles.loadFile path
                if item.ProjectId <> projectId then invalidOp $"Parameter file belongs to another project: {path}"
                if Set.contains item.RunId removed then
                    deleted.Add path
                else
                    for key in RunOutputFiles.keys item do references.Add key.Id |> ignore
                    if item.Sgd.IsSome then temporaryReferences.Add(RunOutputFiles.temporarySnapshotPath folder item) |> ignore
                    for input in item.SetupInputs do
                        if input.ProjectId = projectId then references.Add input.Id |> ignore
        let data = Path.Combine(folder, "data")
        if Directory.Exists data then
            check data
            for phase in ["setup"; "results"] do
                let phaseFolder = Path.Combine(data, phase)
                if Directory.Exists phaseFolder then
                    check phaseFolder
                    for kindFolder in Directory.GetDirectories phaseFolder do
                        check kindFolder
                        for path in Directory.GetFiles(kindFolder, "*.msgpack") do
                            token.ThrowIfCancellationRequested()
                            check path
                            if Path.GetFileName kindFolder = "temporarySnapshots" then
                                if not (temporaryReferences.Contains path) then deleted.Add path
                            else
                                let header = (DataEnvelope.readHeader path token).Envelope
                                if header.ProjectId <> UMX.untag projectId then invalidOp $"Data file belongs to another project: {path}"
                                let dataType = SortopiaDataType.create header.Kind header.Label
                                let reference = { ProjectId = projectId; Id = UMX.tag header.Id; QueryId = UMX.tag header.QueryId
                                                  Phase = RunExecutionPhase.fromIsSetup header.IsSetup; DataType = dataType }
                                let dependencies = DataEnvelope.dependencies header
                                if not (references.Contains reference.Id || keepData reference dependencies) then
                                    deleted.Add path
        deleted.ToArray()
