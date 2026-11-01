namespace Sortopia.Runs

open System
open System.Globalization
open FSharp.UMX

[<Measure>] type paramKeyName

/// The persisted name and string conversions for one parameter.
type paramKey<'value> =
    { Name: string<paramKeyName>
      TryParse: string -> 'value option
      Format: 'value -> string }

module ParamKey =

    let create (name: string) tryParse format =
        { Name = UMX.tag<paramKeyName> name; TryParse = tryParse; Format = format }

    let text name = create name Some id

    let private parsed (success, value) =
        if success then Some value else None

    let private invariant = CultureInfo.InvariantCulture

    let integer name =
        create name
            (fun s -> Int32.TryParse(s, NumberStyles.Integer, invariant) |> parsed)
            (fun value -> value.ToString(invariant))

    let unsigned64 name =
        create name
            (fun s -> UInt64.TryParse(s, NumberStyles.Integer, invariant) |> parsed)
            (fun value -> value.ToString(invariant))

    let floating name =
        create name
            (fun s -> Double.TryParse(s, NumberStyles.Float, invariant) |> parsed)
            (fun value -> value.ToString("R", invariant))

    let boolean name =
        create name (fun s -> Boolean.TryParse(s) |> parsed) string

    let guid name =
        create name (fun s -> Guid.TryParse(s) |> parsed) (fun value -> value.ToString("D"))


module ParamKeys =
    let setupCompletedKey = ParamKey.boolean "SetupCompleted"
    let sortableTestsModuleKey = ParamKey.text "SortableTestsModule"
    let sorterEvalModuleKey = ParamKey.text "SorterEvalModule"
    let adjustCeLengthFrequencyKey = ParamKey.integer "AdjustCeLengthFrequency"
    let activationProbabilityKey = ParamKey.floating "ActivationProbability"
    let controlledSeedAMaxKey = ParamKey.integer "ControlledSeedAMax"
    let aMaxKey = ParamKey.integer "AMax"
    let activatorMutationRateKey = ParamKey.floating "ActivatorMutationRate"
    let ceLengthKey = ParamKey.integer "CeLength"
    let codeModKey = ParamKey.text "CodeModKey"
    let collectSortableTestsKey = ParamKey.boolean "CollectSortableTests"
    let databaseNameKey = ParamKey.text "DatabaseName"
    let deletionRateKey = ParamKey.floating "DeletionRate"
    let distinctSorterHashesKey = ParamKey.boolean "DistinctSorterHashes"
    let elapsedTimeKey = ParamKey.text "ElapsedTime"
    let excludeSelfCeKey = ParamKey.boolean "ExcludeSelfCe"
    let generationCurrentKey = ParamKey.integer "GenerationCurrent"
    let generationFirstKey = ParamKey.integer "GenerationFirst"
    let generationIntervalCountKey = ParamKey.integer "GenerationIntervalCount"
    let generationIntervalLastKey = ParamKey.integer "GenerationIntervalLast"
    let generationsPerMergedSetKey = ParamKey.integer "GenerationsPerMergedSet"
    let generationsPerSubsetKey = ParamKey.integer "GenerationsPerSubset"
    let idKey = ParamKey.guid "Id"
    let insertionRateKey = ParamKey.floating "InsertionRate"
    let latticeDistanceKey = ParamKey.integer "LatticeDistance"
    let maxOrbitKey = ParamKey.integer "MaxOrbit"
    let mergeDimensionKey = ParamKey.integer "MergeDimension"
    let mergeLibIdKey = ParamKey.text "MergeLibId"
    let mergeSuffixTypeKey = ParamKey.text "MergeSuffixType"
    let messageKey = ParamKey.text "Message"
    let modificationRateKey = ParamKey.floating "ModificationRate"
    let mutationModKey = ParamKey.integer "MutationMod"
    let mutationRateKey = ParamKey.floating "MutationRate"
    let mutatorParamsKey = ParamKey.text "MutatorParams"
    let mutatorVariantKey = ParamKey.text "MutatorVariantKey"
    let orthoRateKey = ParamKey.floating "OrthoRate"
    let paraRateKey = ParamKey.floating "ParaRate"
    let partitionCountKey = ParamKey.integer "PartitionCount"
    let prefixLibIdKey = ParamKey.text "PrefixLibId"
    let prioritizeNewMutantsKey = ParamKey.boolean "PrioritizeNewMutants"
    let projectNameKey = ParamKey.text "ProjectName"
    let queryNameKey = ParamKey.text "QueryName"
    let replKey = ParamKey.integer "Repl"
    let replSpanKey = ParamKey.integer "ReplSpan"
    let rngTypeKey = ParamKey.text "RngType"
    let runFinishedKey = ParamKey.boolean "RunFinished"
    let runNameKey = ParamKey.text "RunName"
    let seedModificationRateKey = ParamKey.floating "SeedModificationRate"
    let seedSorterPoolSelectionTypeKey = ParamKey.text "SeedPoolSorterSelectionType"
    let seedSossKey = ParamKey.unsigned64 "SeedSoss"
    let selectedSorterCountPerPoolKey = ParamKey.integer "SelectedSorterCountPerPool"
    let selectedSorterPoolCountKey = ParamKey.integer "SelectedSorterPoolCount"
    let selfSymRateKey = ParamKey.floating "SelfSym"
    let setupParametersIdKey = ParamKey.guid "SetupParametersId"
    let setupSortableTestsWithPrefixIdKey = ParamKey.guid "SetupSortableTestsWithPrefixId"
    let setupSorterModelSetIdKey = ParamKey.guid "SetupSorterModelSetId"
    let setupSorterPoolBinsSetSeriesIdKey = ParamKey.guid "SetupSorterPoolBinsSetSeriesId"
    let setupSorterPoolSetHistoryIdKey = ParamKey.guid "SetupSorterPoolSetHistoryId"
    let setupSorterPoolSetIdKey = ParamKey.guid "SetupSorterPoolSetId"
    let setupSorterPoolSetSummarySetIdKey = ParamKey.guid "SetupSorterPoolSetSummarySetId"
    let setupSorterSetEvalIdKey = ParamKey.guid "SetupSorterSetEvalId"
    let setupSorterSetIdKey = ParamKey.guid "SetupSorterSetId"
    let simpleSorterModelTypeKey = ParamKey.text "SimpleSorterModelType"
    let sortableCountKey = ParamKey.integer "SortableCount"
    let sortableDataFormatKey = ParamKey.text "SortableDataFormat"
    let sortedFractionKey = ParamKey.floating "SortedFraction"
    let sorterChildCountKey = ParamKey.integer "SorterChildCount"
    let sorterCountCycleKey = ParamKey.integer "SorterCountCycle"
    let sorterCountCycleMultiplierKey = ParamKey.floating "SorterCountCycleMultiplier"
    let sorterCountKey = ParamKey.integer "SorterCount"
    let sorterCountPerPoolKey = ParamKey.integer "SorterCountPerPool"
    let sorterEvalMeasureInitialKey = ParamKey.text "SorterEvalMeasureInitial"
    let sorterEvalMeasureKey = ParamKey.text "SorterEvalMeasure"
    let sorterEvalTypeKey = ParamKey.text "SorterEvalType"
    let sorterLibIdKey = ParamKey.text "SorterLibId"
    let sorterLibVariantKey = ParamKey.text "SorterLibVariant"
    let sorterParentCountKey = ParamKey.integer "SorterParentCount"
    let sorterPoolCountKey = ParamKey.integer "SorterPoolCount"
    let sorterPoolExpansionRateKey = ParamKey.integer "SorterPoolExpansionRate"
    let sorterPoolMeasureKey = ParamKey.text "SorterPoolMeasure"
    let sorterPoolSelectionIntervalsKey = ParamKey.text "SorterPoolSelectionIntervals"
    let channelCountKey = ParamKey.integer "ChannelCount"
    let sourceDbNameKey = ParamKey.text "SourceDbName"
    let sourceExecutionIdKey = ParamKey.guid "SourceExecutionId"
    let sourceGenerationIntervalKey = ParamKey.integer "SourceGenerationInterval"
    let sourceMutationModKey = ParamKey.integer "SourceMutationMod"
    let sourceProjectNameKey = ParamKey.text "SourceProjectName"
    let sourceRunNameKey = ParamKey.text "SourceRunName"
    let stageLengthKey = ParamKey.integer "StageLength"
    let startingReplKey = ParamKey.integer "StartingRepl"
    let textReportNameKey = ParamKey.text "TextReportName"

    /// Resolve a persisted source-filter name to the corresponding source parameter name.
    let sourceParameterKey = function
        | name when name = sourceMutationModKey.Name -> mutationModKey.Name
        | name when name = sourceRunNameKey.Name -> runNameKey.Name
        | name -> name
