namespace Sortopia.Functions

open System.Globalization
open Sortopia.Runs
open Sortopia.Sorting
open Sortopia.Sorting.Catalog

/// Default suggestions shared by function providers, including source-query inputs.
module internal ParameterDefaults =
    let channelCount (bindings: parameterBinding list) =
        bindings |> List.tryPick (fun binding ->
            match string binding.KeyName, binding.Value with
            | "ChannelCount", parameterValue.IntegerValue width when width > 0 -> Some width
            | _ -> None)
        |> Option.orElseWith (fun () -> bindings |> List.tryPick (fun binding ->
            match binding.Value with
            | parameterValue.SorterLibIdValue id -> Some (int id.ChannelCount)
            | parameterValue.PrefixLibIdValue id -> Some (int id.ChannelCount)
            | parameterValue.MergeLibIdValue id -> Some (int id.ChannelCount)
            | _ -> None))

    let suppliedWidth (context: parameterDefaultContext) =
        channelCount context.Current |> Option.orElseWith (fun () -> channelCount context.Related)

    /// Later providers own duplicate keys. Never propose values outside the missing descriptors.
    let select (context: parameterDefaultContext) bindings =
        let proposed = bindings |> List.map (fun (binding: parameterBinding) -> binding.KeyName, binding) |> Map.ofList
        context.Missing |> List.choose (fun key -> Map.tryFind key.Name proposed)

    let combine providers (context: parameterDefaultContext) =
        providers |> List.collect (fun provider -> provider context) |> select context

    let commonAtWidth (width: int) (context: parameterDefaultContext) =
        let preferWidth count = if int count = width then 0 elif int count = 8 then 1 else 2
        context.Missing |> List.choose (fun key ->
            let text =
                match string key.Name with
                | "ChannelCount" -> Some (width.ToString(CultureInfo.InvariantCulture))
                | "StageLength" -> Some "1"
                | "SorterSuccessFraction" -> Some "99%"
                | "SorterCount" -> Some "100"
                | "Repl" | "InitialPoolSeed" -> Some "0"
                | "InitialPoolSelection" -> Some "Random"
                | "SortableDataFormat" -> Some "Bools"
                | "RngType" -> Some "SplitMix64"
                | "SorterEvalType" -> Some "V1"
                | "CollectSortableTests" -> Some "false"
                | "MergeSuffixType" -> Some "NoSuffix"
                | _ ->
                    match key.ValueType with
                    | parameterValueType.SorterLibId ->
                        SorterLib.ids () |> Seq.sortBy (fun id -> preferWidth id.ChannelCount, SorterLibId.toString id)
                        |> Seq.head |> SorterLibId.toString |> Some
                    | parameterValueType.PrefixLibId ->
                        PrefixLib.ids () |> Seq.sortBy (fun id -> preferWidth id.ChannelCount, id.StageLength, PrefixLibId.toString id)
                        |> Seq.head |> PrefixLibId.toString |> Some
                    | parameterValueType.MergeLibId ->
                        let available = MergeLibId.availableForChannelCount (width * 1<channelCount>)
                        (if available.Length > 0 then available else MergeLibId.availableForChannelCount 8<channelCount>)
                        |> Seq.sortBy (fun id -> (if id.MergeDimension = 2<mergeDimension> then 0 else 1), id.MergeDimension, MergeLibId.toString id)
                        |> Seq.head |> MergeLibId.toString |> Some
                    // Typed weight defaults also cover source keys that no current generator declares.
                    | parameterValueType.OpsWeights -> Some "1,1,1"
                    | parameterValueType.Seed6Weights -> Some "1,1,1,1,1,1,1"
                    | parameterValueType.UnfoldingWeights -> Some ""
                    | _ -> None
            text |> Option.map (ParameterBinding.fromText key.Name key.ValueType))

    let common context = commonAtWidth (suppliedWidth context |> Option.defaultValue 8) context
    let support = { FunctionParameterSupport.empty with Defaults = common; EditorRules = ParameterRules.common }
