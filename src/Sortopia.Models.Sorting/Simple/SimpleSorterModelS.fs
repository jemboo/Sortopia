namespace Sortopia.Models.Sorting.Simple

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks

/// A simple model paired with one boolean activator per underlying array entry.
/// Only entries with true activators contribute to the sorter.
[<NoComparison>]
type simpleSorterModelS =
    private {
        model: simpleSorterModel
        activators: bool[]
    }

    static member create (model: simpleSorterModel) (activators: bool[]) : simpleSorterModelS =
        if obj.ReferenceEquals(model, null) then nullArg "model"
        if isNull activators then nullArg "activators"
        let values = Array.copy activators
        let expectedLength = SimpleSorterModel.getLength model
        if values.Length <> expectedLength then
            invalidArg "activators" (sprintf "Expected %d activators for the model's underlying array, got %d." expectedLength values.Length)
        { model = model; activators = values }

    member this.Model = this.model
    /// Return a copy so callers cannot invalidate the activator values.
    member this.Activators = Array.copy this.activators

    /// Filter model entries first, expand them in source order, then apply the CE limit.
    member this.MakeSorter (maxCeLength: int<ceLength> option) : sorter =
        let active entries =
            entries |> Array.indexed
            |> Array.choose (fun (index, entry) -> if this.activators.[index] then Some entry else None)
        let allCes =
            match this.model with
            | Msce model -> model.CeCodes |> active |> Array.map Ce.fromIndex
            | Mssi model -> model.Perm_Sis |> active |> Array.collect Ce.fromPermSi
            | Msrs model -> model.Perm_Rss |> active |> Array.collect (fun entry -> Ce.fromPermSi entry.PermSi)
            | Msuf4 model -> model.TwoOrbitUnfolder4s |> active |> Array.collect (fun entry -> Ce.fromPermSi entry.MakePerm_Si)
            | Msuf6 model -> model.TwoOrbitUnfolder6s |> active |> Array.collect (fun entry -> Ce.fromPermSi entry.MakePerm_Si)
        let ces =
            match maxCeLength with
            | Some length -> allCes |> Array.truncate (int length)
            | None -> allCes
        sorter.create (SimpleSorterModel.getId this.model |> UMX.untag |> UMX.tag<sorterId>)
            (SimpleSorterModel.getChannelCount this.model) ces

module SimpleSorterModelS =
    let getId (model: simpleSorterModelS) = SimpleSorterModel.getId model.Model

    let getChannelCount (model: simpleSorterModelS) = SimpleSorterModel.getChannelCount model.Model

    let getCeLength (model: simpleSorterModelS) : int<ceLength> =
        let activeCount = model.Activators |> Array.filter id |> Array.length
        match model.Model with
        | Msce _ -> UMX.tag activeCount
        | _ -> UMX.tag (activeCount * int (getChannelCount model) / 2)

    let getStageLength (model: simpleSorterModelS) : int<stageLength> =
        let activeCount = model.Activators |> Array.filter id |> Array.length
        match model.Model with
        | Msce _ -> UMX.tag (2 * activeCount / int (getChannelCount model))
        | _ -> UMX.tag activeCount

    let makeSorter (maxCeLength: int<ceLength> option) (model: simpleSorterModelS) : sorter =
        model.MakeSorter maxCeLength
