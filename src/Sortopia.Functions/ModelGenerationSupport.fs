namespace Sortopia.Functions

open System
open Sortopia.Core
open Sortopia.Runs
open Sortopia.Models.Sorting
open Sortopia.Models.Sorting.Simple
open Sortopia.Sorting
open Sortopia.Sorting.Catalog
open FunctionParameters

/// Reusable policy builders. Algorithm registrations supply all algorithm-specific behavior.
module internal ModelGenerationSupport =
    let suffixStages modelType source parameters =
        let width, prefixStages =
            match source with
            | modelPrefixMode.CatalogPrefix ->
                let prefix = required ParamKeys.prefixLibIdKey parameters
                prefix.ChannelCount, prefix.StageLength
            | modelPrefixMode.NoPrefix -> required ParamKeys.channelCountKey parameters * 1<channelCount>, 0<stageLength>
        (SimpleSorterStageLengths.forSuccessFraction (required ParamKeys.sorterSuccessFractionKey parameters) width prefixStages modelType).Stages

    // Check partial defaults and every participating span value without expanding unrelated spans.
    let inputError modelType validateAlgorithm source (defaults: parameterBinding list) (spans: parameterSpan list) =
        let values key = inputValues key defaults spans
        try
            let widthKey = match source with modelPrefixMode.CatalogPrefix -> "PrefixLibId" | modelPrefixMode.NoPrefix -> "ChannelCount"
            let dimensions =
                match source with
                | modelPrefixMode.CatalogPrefix ->
                    values ParamKeys.prefixLibIdKey |> List.map (fun prefix ->
                        if PrefixLib.tryGet prefix |> Option.isNone then
                            invalidArg "PrefixLibId" $"PrefixLibId '{PrefixLibId.toString prefix}' is not available in the prefix catalog."
                        int prefix.ChannelCount, int prefix.StageLength)
                | modelPrefixMode.NoPrefix -> values ParamKeys.channelCountKey |> List.map (fun width -> width, 0)
            let widths = dimensions |> List.map fst
            for width in widths do
                if not (SimpleSorterModelType.supportsChannelCount width modelType) then
                    invalidArg widthKey $"The {SimpleSorterModelType.toString modelType} function is incompatible with {widthKey} ({width} channels): it requires {SimpleSorterModelType.channelCountRequirement modelType}."
            values ParamKeys.rngTypeKey |> List.iter (RngType.fromString >> ignore)
            validateAlgorithm widthKey widths defaults spans
            let budgets = [
                for width, prefix in dimensions do
                    // Validate dimensions even when the target is missing from partial defaults.
                    SimpleSorterStageLengths.forSuccessFraction sorterSuccessFraction.pct50 (width * 1<channelCount>) (prefix * 1<stageLength>) modelType |> ignore
                    for fraction in values ParamKeys.sorterSuccessFractionKey do
                        let recommendation = SimpleSorterStageLengths.forSuccessFraction fraction (width * 1<channelCount>) (prefix * 1<stageLength>) modelType
                        yield width, int recommendation.Stages ]
            let counts = values ParamKeys.sorterCountKey
            let replications = values ParamKeys.replKey
            if counts |> List.exists (fun value -> value < 1 || value > Array.MaxLength) then invalidArg "SorterCount" "SorterCount must be positive and fit in a model array."
            if replications |> List.exists (fun value -> value < 0) then invalidArg "Repl" "Repl must be nonnegative."
            if not counts.IsEmpty && not replications.IsEmpty then
                let count = int64 (List.max counts)
                if int64 (List.max replications) * count + count - 1L > int64 Int32.MaxValue then
                    invalidArg "parameters" "Repl and SorterCount produce sorter indices beyond the supported range."
            if budgets |> List.exists (fun (width, stages) -> int64 stages * int64 width > int64 Int32.MaxValue) then
                invalidArg "SorterSuccessFraction" $"The suggested stage length and {widthKey}'s channel count exceed the supported suffix-size range."
            ""
        with error -> error.Message

    let private defaults modelType fallback algorithmDefaults (context: parameterDefaultContext) =
        let supplied = ParameterDefaults.suppliedWidth context
        let needsWidth = context.Missing |> List.exists (fun key -> string key.Name = "ChannelCount" || string key.Name = "PrefixLibId")
        let width =
            match supplied with
            | Some width when not needsWidth || SimpleSorterModelType.supportsChannelCount width modelType -> width
            | _ -> fallback
        ParameterDefaults.commonAtWidth width context @ algorithmDefaults width |> ParameterDefaults.select context

    let private stageSuggestion source algorithm defaults spans =
        try
            let values key = FunctionParameters.inputValues key defaults spans
            let fractions = values ParamKeys.sorterSuccessFractionKey
            let dimensions =
                match source with
                | modelPrefixMode.CatalogPrefix -> values ParamKeys.prefixLibIdKey |> List.map (fun id -> id.ChannelCount, id.StageLength)
                | modelPrefixMode.NoPrefix -> values ParamKeys.channelCountKey |> List.map (fun width -> width * 1<channelCount>, 0<stageLength>)
            if fractions.IsEmpty || dimensions.IsEmpty then "Select a success fraction and channel count or prefix to see the suggested stage length. "
            else
                let suggestions = [ for width, prefix in dimensions do
                                        for fraction in fractions do
                                            yield SimpleSorterStageLengths.forSuccessFraction fraction width prefix algorithm ]
                if suggestions |> List.exists (fun value -> not value.IsSupported) then "Stage suggestion unavailable for an unsupported channel count. "
                else
                    let minimum = suggestions |> List.map _.Stages |> List.min
                    let maximum = suggestions |> List.map _.Stages |> List.max
                    if suggestions.Length = 1 then
                        let width, prefix = dimensions.Head
                        $"Suggested random suffix: {int minimum:N0} stages ({int width} channels, {int prefix} prefix stages, {SorterSuccessFraction.toString fractions.Head} target; {suggestions.Head.Source}). "
                    else $"Suggested random suffix: {int minimum:N0}–{int maximum:N0} stages across the selected widths, prefixes, and success fractions. "
        with :? ArgumentException as error -> $"Stage suggestion unavailable: {error.Message} "

    let private guidance algorithm weights includeTests source constructorGuidance defaults spans =
        let dimensions =
            match source with
            | modelPrefixMode.CatalogPrefix -> "Channel count and prefix stage length come from PrefixLibId. SorterSuccessFraction determines the random suffix capacity; the fixed prefix is always added. "
            | modelPrefixMode.NoPrefix -> "ChannelCount sets the width; SorterSuccessFraction determines the stage capacity with zero prefix stages. The generated models have no fixed prefix. "
        let tests = if includeTests then "Generates full sortable tests using ChannelCount and SortableDataFormat. Tests and models have separate output projections. " else ""
        stageSuggestion source algorithm defaults spans + constructorGuidance + dimensions
        + "Choose 50%, 90%, or 99% of generated networks sorting every input. These budgets are provisional; custom weights and inactive entries can change the actual success rate. "
        + tests + "Repl selects a reproducible batch of SorterCount models. " + weights

    let create modelType fallback algorithmDefaults weights includeTests source constructorGuidance : functionParameterSupport =
        { Defaults = defaults modelType fallback algorithmDefaults
          Guidance = guidance modelType weights includeTests source constructorGuidance
          Preview = fun parameters ->
              [{ Key = "SuffixStages"; Label = "Suggested suffix stages"
                 Value = parameterValue.IntegerValue (int (suffixStages modelType source parameters))
                 Hint = "Suggested suffix stages are calculated for each combination using its channel count, prefix depth, model type, and success fraction. Fixed prefix stages are additional; success rates are provisional." }] }
