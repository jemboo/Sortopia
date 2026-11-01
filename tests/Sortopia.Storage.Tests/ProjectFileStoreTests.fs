module Sortopia.Storage.Tests.ProjectFileStoreTests

open System
open System.IO
open FSharp.UMX
open Xunit
open Sortopia.Evaluation
open Sortopia.Models.Sorting
open Sortopia.Sorting
open Sortopia.Sorting.Inputs
open Sortopia.Sorting.Networks
open Sortopia.Storage

let private withProject test =
    let project = Directory.CreateTempSubdirectory("sortopia-storage-")
    try test project.FullName
    finally project.Delete(true)

let private newParameters () = { RunId = Guid.NewGuid(); ParameterId = Guid.NewGuid() }

let private dataset () =
    let width = 2<channelCount>
    let inputs = [| sortableIntArray.create([| 1; 0 |], width, 2<symbolSetSize>) |]
    sortableIntTests.create (UMX.tag (Guid.NewGuid())) width inputs |> sortableTests.Ints

[<Fact>]
let ``reopened project lists and reads parameters and all Result types without a manifest`` () =
    withProject (fun root ->
        let store = ProjectFileStore(root)
        let producer = newParameters ()
        let values = Map.ofList [ "channels", "2"; "description", "Δ test" ]
        store.SaveParameters(producer, values)
        let tests = dataset ()
        let models = sorterModelSet.create (UMX.tag (Guid.NewGuid())) [||] 42<ceLength>
        let evaluation = sorterSetEval.create (UMX.tag (Guid.NewGuid())) (UMX.tag (UMX.untag models.Id)) (SortableTests.getId tests) [||]
        let data =
            [| ResultData.SortableTestsWithPrefix(dataset (), [||])
               ResultData.SortableTestsWithPrefix(tests, [| ce.create 0 1 |])
               ResultData.SorterModelSet models
               ResultData.SorterSetEval evaluation |]
        let refs = data |> Array.map (fun value -> store.SaveResult(producer, value))
        let reopened = ProjectFileStore(root)
        Assert.Equal(producer, Assert.Single(reopened.ListParameters(producer.RunId)))
        Assert.Equal<Map<string, string>>(values, reopened.LoadParameters(producer))
        Assert.Equal<ResultRef>(Array.sort refs, reopened.ListResults(producer.RunId))
        for reference, expected in Array.zip refs data do
            Assert.Equal(producer, reference.Parameters)
            Assert.Equal(expected, reopened.LoadResult(reference)))

[<Fact>]
let ``Results belong to a specific run and parameter file and duplicate writes preserve the original`` () =
    withProject (fun root ->
        let store = ProjectFileStore(root)
        let first = newParameters ()
        let second = { first with ParameterId = Guid.NewGuid() }
        let otherRun = { first with RunId = Guid.NewGuid() }
        let data = ResultData.SortableTestsWithPrefix(dataset (), [||])
        Assert.Throws<FileNotFoundException>(fun () -> store.SaveResult(first, data) |> ignore) |> ignore
        Assert.Empty(store.ListResults(first.RunId))
        for producer in [first; second; otherRun] do store.SaveParameters(producer, Map.empty)
        let refs = [| for producer in [first; second; otherRun] -> store.SaveResult(producer, data) |]
        Assert.Throws<IOException>(fun () -> store.SaveParameters(first, Map.ofList ["changed", "true"])) |> ignore
        Assert.Throws<IOException>(fun () -> store.SaveResult(first, data) |> ignore) |> ignore
        Assert.Empty(store.LoadParameters(first))
        Assert.Equal<ResultRef>(Array.sort refs[..1], store.ListResults(first.RunId))
        Assert.Equal(refs[2], Assert.Single(store.ListResults(otherRun.RunId)))
        Assert.Equal(data, store.LoadResult(refs[0])))

[<Fact>]
let ``invalid Result payloads never become discoverable`` () =
    withProject (fun root ->
        let store = ProjectFileStore(root)
        let producer = newParameters ()
        store.SaveParameters(producer, Map.empty)
        let tests = dataset ()
        let invalidPrefix = ResultData.SortableTestsWithPrefix(tests, [| ce.create 0 2 |])
        Assert.Throws<ArgumentException>(fun () -> store.SaveResult(producer, invalidPrefix) |> ignore) |> ignore
        let invalidEval = sorterSetEval.create (UMX.tag (Guid.NewGuid())) (UMX.tag Guid.Empty) (SortableTests.getId tests) [||]
        Assert.ThrowsAny<Exception>(fun () -> store.SaveResult(producer, ResultData.SorterSetEval invalidEval) |> ignore) |> ignore
        Assert.Empty(store.ListResults(producer.RunId)))

[<Fact>]
let ``reader detects a payload replaced with a different dataset`` () =
    withProject (fun root ->
        let store = ProjectFileStore(root)
        let producer = newParameters ()
        store.SaveParameters(producer, Map.empty)
        let save () = store.SaveResult(producer, ResultData.SortableTestsWithPrefix(dataset (), [||]))
        let first, second = save (), save ()
        let file reference =
            Assert.Single(Directory.GetFiles(root, reference.DataId.ToString("N") + ".mpk", SearchOption.AllDirectories))
        File.Copy(file second, file first, overwrite = true)
        Assert.Throws<InvalidDataException>(fun () -> store.LoadResult(first) |> ignore) |> ignore)
