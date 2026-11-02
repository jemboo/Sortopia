namespace Sortopia.Models.Sorting

open System
open FSharp.UMX
open Sortopia.Core
open Sortopia.Permutations
open Sortopia.Sorting
open Sortopia.Sorting.Networks
open Sortopia.Models.Sorting.Simple
open Sortopia.Models.Sorting.Simple.Ce

/// Generator adapter that retains each generated stage and handles odd-width involutions.
module RandomNetwork =
    let private powerOfTwo n = n > 0 && (n &&& (n - 1)) = 0

    let supportsWidth kind width =
        match kind with
        | "msce" | "mssi" -> width >= 2
        | "msrs" -> width >= 4 && width % 2 = 0
        | "msuf4" -> width >= 4 && powerOfTwo width
        | "msuf6" -> width >= 12 && width % 3 = 0 && powerOfTwo (width / 3)
        | _ -> false

    /// Size is CE count for msce, stage count otherwise. A supplied ID permits exact replay.
    let generateModel (kind: string) (width: int) (size: int) (id: Guid) =
        if not (supportsWidth kind width) then invalidArg "width" "Channel count is not supported by the selected generator."
        if size < 1 then invalidArg "size" "Count must be positive."
        let channels = UMX.tag<channelCount> width
        let modelId = UMX.tag<sorterModelId> id
        if kind = "msce" then
            let gen = msceRandGen.create rngFactory.SmxFactory channels (UMX.tag true) (UMX.tag size)
            simpleSorterModel.Msce (gen.MakeSorterModelFromId modelId)
        else
            let modelType =
                match kind with
                | "mssi" -> simpleSorterModelType.Mssi
                | "msrs" -> simpleSorterModelType.Msrs
                | "msuf4" -> simpleSorterModelType.Msuf4
                | _ -> simpleSorterModelType.Msuf6
            let generator = SimpleSorterModelGen.makeUniform rngFactory.SmxFactory channels (UMX.tag size) modelType (UMX.tag true)
            SimpleSorterModelGen.makeSorterModelFromId modelId generator

    let toStages (model: simpleSorterModel) =
        let channels = SimpleSorterModel.getChannelCount model
        let fromPermutation (permutation: permSi) =
            // Fixed points in an odd-width involution are idle wires, not comparisons.
            permutation.Array
            |> Array.mapi (fun low high -> if low < high then Some (ce.create low high) else None)
            |> Array.choose (fun value -> value)
            |> fun ces -> stage.create ces channels
        let stages =
            match model with
            | simpleSorterModel.Msce model ->
                let result = ResizeArray<stage>()
                let current = ResizeArray<ce>()
                let occupied = Collections.Generic.HashSet<int>()
                let flush () =
                    if current.Count > 0 then result.Add(stage.create (current.ToArray()) channels)
                    current.Clear()
                    occupied.Clear()
                for code in model.CeCodes do
                    let comparator = Ce.fromIndex code
                    let low, high = int comparator.LowChannel, int comparator.HighChannel
                    if occupied.Contains low || occupied.Contains high then flush ()
                    current.Add comparator
                    occupied.Add low |> ignore
                    occupied.Add high |> ignore
                flush ()
                result.ToArray()
            | simpleSorterModel.Mssi model -> model.Perm_Sis |> Array.map fromPermutation
            | simpleSorterModel.Msrs model -> model.Perm_Rss |> Array.map (fun p -> fromPermutation p.PermSi)
            | simpleSorterModel.Msuf4 model -> model.TwoOrbitUnfolder4s |> Array.map (fun p -> fromPermutation p.MakePerm_Si)
            | simpleSorterModel.Msuf6 model -> model.TwoOrbitUnfolder6s |> Array.map (fun p -> fromPermutation p.MakePerm_Si)
        stageSequence.create stages channels

    let generate kind width size id = generateModel kind width size id |> toStages

    /// Use each model family's native mutation, preserving the number of CEs or stages.
    let createMutator (model: simpleSorterModel) (rate: float) =
        if not (rate > 0.0 && rate <= 1.0) then invalidArg "rate" "Mutation rate must be in (0, 1]."
        let rng = rngFactory.SmxFactory
        let width = SimpleSorterModel.getChannelCount model
        let excludeSelf = UMX.tag<excludeSelfCe> true
        let modification = UMX.tag<modificationRate> rate
        match model with
        | simpleSorterModel.Msce _ ->
            SimpleSorterModelMutator.getMsceModelMutator rng excludeSelf modification (UMX.tag 1.) (UMX.tag 0.) (UMX.tag 0.) mutatorVariant.V1
        | simpleSorterModel.Mssi _ ->
            SimpleSorterModelMutator.getMssiModelMutator rng excludeSelf modification (UMX.tag 1.) (UMX.tag 1.) mutatorVariant.V1
        | simpleSorterModel.Msrs _ ->
            SimpleSorterModelMutator.getMsrsModelMutator rng excludeSelf modification (UMX.tag 1.) (UMX.tag 1.) (UMX.tag 1.) mutatorVariant.V1
        | simpleSorterModel.Msuf4 _ ->
            SimpleSorterModelMutator.getMsuf4ModelMutator width rng excludeSelf (UMX.tag rate) modification (UMX.tag 1.) (UMX.tag 1.) (UMX.tag 1.) mutatorVariant.V2
        | simpleSorterModel.Msuf6 _ ->
            SimpleSorterModelMutator.getMsuf6ModelMutator width rng excludeSelf (UMX.tag rate) modification (UMX.tag 1.) (UMX.tag 1.) (UMX.tag 1.) mutatorVariant.V1
