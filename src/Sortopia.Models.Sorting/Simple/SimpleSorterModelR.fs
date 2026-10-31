namespace Sortopia.Models.Sorting.Simple

open FSharp.UMX
open Sortopia.Sorting
open Sortopia.Sorting.Networks

/// A simple model paired with one bounded activator per underlying array entry.
/// Only entries with positive activators contribute to the sorter.
[<NoComparison>]
type simpleSorterModelR =
    private {
        model: simpleSorterModel
        aMax: int
        activators: int[]
    }

    static member create (model: simpleSorterModel) (aMax: int) (activators: int[]) : simpleSorterModelR =
        if obj.ReferenceEquals(model, null) then nullArg "model"
        if aMax < 0 then invalidArg "aMax" "aMax must be nonnegative."
        if isNull activators then nullArg "activators"
        let values = Array.copy activators
        let expectedLength = SimpleSorterModel.getLength model
        if values.Length <> expectedLength then
            invalidArg "activators" (sprintf "Expected %d activators for the model's underlying array, got %d." expectedLength values.Length)
        if values |> Array.exists (fun value -> value < -aMax || value > aMax) then
            invalidArg "activators" (sprintf "Activators must be within [%d, %d]." (-aMax) aMax)
        { model = model; aMax = aMax; activators = values }

    member this.Model = this.model
    member this.AMax = this.aMax
    /// Return a copy so callers cannot invalidate the activator bounds.
    member this.Activators = Array.copy this.activators

    /// Filter model entries first, expand them in source order, then apply the CE limit.
    member this.MakeSorter (maxCeLength: int<ceLength> option) : sorter =
        let active entries =
            entries |> Array.indexed
            |> Array.choose (fun (index, entry) -> if this.activators.[index] > 0 then Some entry else None)
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

module SimpleSorterModelR =
    let getId (model: simpleSorterModelR) = SimpleSorterModel.getId model.Model

    let getChannelCount (model: simpleSorterModelR) = SimpleSorterModel.getChannelCount model.Model

    let getCeLength (model: simpleSorterModelR) : int<ceLength> =
        let activeCount = model.Activators |> Array.filter (fun value -> value > 0) |> Array.length
        match model.Model with
        | Msce _ -> UMX.tag activeCount
        | _ -> UMX.tag (activeCount * int (getChannelCount model) / 2)

    let getStageLength (model: simpleSorterModelR) : int<stageLength> =
        let activeCount = model.Activators |> Array.filter (fun value -> value > 0) |> Array.length
        match model.Model with
        | Msce _ -> UMX.tag (2 * activeCount / int (getChannelCount model))
        | _ -> UMX.tag activeCount

    let makeSorter (maxCeLength: int<ceLength> option) (model: simpleSorterModelR) : sorter =
        model.MakeSorter maxCeLength
