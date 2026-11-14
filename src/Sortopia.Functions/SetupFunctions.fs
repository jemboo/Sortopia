namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Models.Sorting
open FunctionParameters
open InputGenerators

// Adapted from GeneSort's SetupCatalog.runBoth and SortableTestsMakers.getModuleTests.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module SetupFunctions =
    let private testsType = sortopiaDataType.SortableTests ""
    let private modelsType = sortopiaDataType.SorterModelSet ""

    let standardTests =
        FunctionDefinition.setup "1" standardKeys [testsType, queryKeys standardKeys]
            (fun host _ -> host.SetupDb.SaveIfMissing(testsType, [], fun () -> InputGenerators.standardTests host.Parameters))
        |> FunctionDefinition.describe "Generate the full set of binary sortable tests for ChannelCount in the selected data format, with an empty prefix. Save the tests in the setup database."

    let private sourceInputError withPrefix defaults spans =
        try
            let widths = inputValues ParamKeys.channelCountKey defaults spans
            if widths |> List.exists (fun count -> count < 1) then
                invalidArg "ChannelCount" "ChannelCount must be positive."
            if withPrefix then
                for prefix in inputValues ParamKeys.prefixLibIdKey defaults spans do
                    if widths |> List.exists ((<>) (int prefix.ChannelCount)) then
                        invalidArg "ChannelCount" $"ChannelCount must match PrefixLibId's {int prefix.ChannelCount} channels for every parameter combination."
            ""
        with error -> error.Message

    /// Copy both evaluation inputs unchanged, using a separate source project per type.
    let private copyEvaluationInputs (host: runHost) (projects: sourceProjects) =
        let channels = required ParamKeys.channelCountKey host.Parameters
        let modelsReference = host.SourceResults.Find(projects[modelsType], modelsType, host.Parameters)
        let testsReference = host.SourceResults.Find(projects[testsType], testsType, host.Parameters)
        let modelsValue = host.SourceResults.Load modelsReference
        let testsValue = host.SourceResults.Load testsReference
        // Validate both sources before publishing either local copy, including on retries.
        match modelsValue with
        | functionData.SorterModelSet models ->
            if models.SorterModels |> Array.exists (fun model -> int (SorterModel.getChannelCount model) <> channels) then
                invalidArg "ChannelCount" $"Every model in the source SorterModelSet must have ChannelCount {channels}."
        | _ -> invalidOp "Expected a source SorterModelSet."
        match testsValue with
        | functionData.SortableTests (tests, _) ->
            if int (Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests) <> channels then
                invalidArg "ChannelCount" $"The source SortableTests must have ChannelCount {channels}."
        | _ -> invalidOp "Expected source SortableTests."
        host.SetupDb.SaveIfMissing(modelsType, [modelsReference], fun () -> modelsValue)
        host.SetupDb.SaveIfMissing(testsType, [testsReference], fun () -> testsValue)

    let evaluationSource =
        let keys = [ParamKey.describe ParamKeys.channelCountKey]
        FunctionDefinition.setupWithSourcesValidated "1" keys
            [modelsType, queryKeys keys; testsType, queryKeys keys]
            [modelsType, [modelsType]; testsType, [testsType]] (sourceInputError false) copyEvaluationInputs
        |> FunctionDefinition.describe "Copy sorter models and sortable tests from separately selected source projects into the setup database. Find both by ChannelCount, validate matching widths, and preserve the models, tests, and tests' prefix."

    let evaluationSourcePrefix =
        let keys = [ParamKey.describe ParamKeys.channelCountKey; ParamKey.describe ParamKeys.prefixLibIdKey]
        FunctionDefinition.setupWithSourcesValidated "1" keys
            [modelsType, [ParamKeys.channelCountKey.Name]; testsType, [ParamKeys.prefixLibIdKey.Name]]
            [modelsType, [modelsType]; testsType, [testsType]] (sourceInputError true) copyEvaluationInputs
        |> FunctionDefinition.describe "Copy sorter models by ChannelCount and sortable tests by PrefixLibId from separately selected source projects into the setup database. Require matching channel counts and preserve the existing models, tests, and tests' prefix."

    let private modelGenerator modelType keys make =
        FunctionDefinition.setupValidated "1" keys [modelsType, queryKeys keys] (ModelGenerators.inputError ChannelCountInput modelType)
            (fun host _ -> host.SetupDb.SaveIfMissing(modelsType, [], fun () -> make host.Parameters))
        |> FunctionDefinition.describe (ModelGenerators.description ChannelCountInput modelType + " Save the models in the setup database.")

    let uniformMsceSorterModels = modelGenerator simpleSorterModelType.Msce (ModelGenerators.msceKeys ChannelCountInput) (ModelGenerators.msce ChannelCountInput)
    let uniformMssiSorterModels = modelGenerator simpleSorterModelType.Mssi (ModelGenerators.mssiKeys ChannelCountInput) (ModelGenerators.mssi ChannelCountInput)
    let uniformMsrsSorterModels = modelGenerator simpleSorterModelType.Msrs (ModelGenerators.msrsKeys ChannelCountInput) (ModelGenerators.msrs ChannelCountInput)
    let uniformMsuf4SorterModels = modelGenerator simpleSorterModelType.Msuf4 (ModelGenerators.msuf4Keys ChannelCountInput) (ModelGenerators.msuf4 ChannelCountInput)
    let uniformMsuf6SorterModels = modelGenerator simpleSorterModelType.Msuf6 (ModelGenerators.msuf6Keys ChannelCountInput) (ModelGenerators.msuf6 ChannelCountInput)

    let private prepareEvaluation testsKeys fromSource modelType modelKeys makeModels =
        let testsDescription =
            if not fromSource then "Generate full binary sortable tests."
            elif testsKeys |> List.exists (fun key -> key.Name = ParamKeys.mergeLibIdKey.Name) then
                "Copy tests and their prefix from a source project, selecting by MergeLibId and SortableDataFormat."
            else "Copy tests and their prefix from a source project, selecting by PrefixLibId and SortableDataFormat."
        FunctionDefinition.setupWithSourcesValidated "1"
            (testsKeys @ modelKeys |> List.distinctBy (fun key -> key.Name))
            [testsType, queryKeys testsKeys; modelsType, queryKeys modelKeys]
            [testsType, (if fromSource then [testsType] else []); modelsType, []]
            (evaluationInputError testsKeys modelType)
            (fun host projects ->
                let sources =
                    if fromSource then
                        let reference = host.SourceResults.Find(projects[testsType], testsType, host.Parameters)
                        // Validate source/model compatibility even when both outputs already exist.
                        sourceTests host.Parameters [host.SourceResults.Load reference] |> ignore
                        [reference]
                    else []
                host.SetupDb.SaveIfMissing(modelsType, [], fun () -> makeModels host.Parameters)
                host.SetupDb.SaveIfMissing(testsType, sources, fun () ->
                    if fromSource then sourceTests host.Parameters [host.SourceResults.Load sources.Head]
                    else InputGenerators.standardTests host.Parameters))
        |> FunctionDefinition.describe ($"{testsDescription} Generate {ModelGenerators.algorithmDescription modelType} sorter models with no fixed prefix. Save both inputs in the setup database for results evaluation; their channel counts must match.")

    let evaluationStandardMsce = prepareEvaluation standardKeys false simpleSorterModelType.Msce (ModelGenerators.msceKeys ChannelCountInput) (ModelGenerators.msce ChannelCountInput)
    let evaluationStandardMssi = prepareEvaluation standardKeys false simpleSorterModelType.Mssi (ModelGenerators.mssiKeys ChannelCountInput) (ModelGenerators.mssi ChannelCountInput)
    let evaluationStandardMsrs = prepareEvaluation standardKeys false simpleSorterModelType.Msrs (ModelGenerators.msrsKeys ChannelCountInput) (ModelGenerators.msrs ChannelCountInput)
    let evaluationStandardMsuf4 = prepareEvaluation standardKeys false simpleSorterModelType.Msuf4 (ModelGenerators.msuf4Keys ChannelCountInput) (ModelGenerators.msuf4 ChannelCountInput)
    let evaluationStandardMsuf6 = prepareEvaluation standardKeys false simpleSorterModelType.Msuf6 (ModelGenerators.msuf6Keys ChannelCountInput) (ModelGenerators.msuf6 ChannelCountInput)
    // Source project IDs replace GeneSort's SortableTestsModule name. The library
    // key and format look up the source's exact result projection.
    let evaluationMergeMsce = prepareEvaluation mergeKeys true simpleSorterModelType.Msce (ModelGenerators.msceKeys ChannelCountInput) (ModelGenerators.msce ChannelCountInput)
    let evaluationMergeMssi = prepareEvaluation mergeKeys true simpleSorterModelType.Mssi (ModelGenerators.mssiKeys ChannelCountInput) (ModelGenerators.mssi ChannelCountInput)
    let evaluationMergeMsrs = prepareEvaluation mergeKeys true simpleSorterModelType.Msrs (ModelGenerators.msrsKeys ChannelCountInput) (ModelGenerators.msrs ChannelCountInput)
    let evaluationMergeMsuf4 = prepareEvaluation mergeKeys true simpleSorterModelType.Msuf4 (ModelGenerators.msuf4Keys ChannelCountInput) (ModelGenerators.msuf4 ChannelCountInput)
    let evaluationMergeMsuf6 = prepareEvaluation mergeKeys true simpleSorterModelType.Msuf6 (ModelGenerators.msuf6Keys ChannelCountInput) (ModelGenerators.msuf6 ChannelCountInput)
    let evaluationPrefixMsce = prepareEvaluation prefixKeys true simpleSorterModelType.Msce (ModelGenerators.msceKeys ChannelCountInput) (ModelGenerators.msce ChannelCountInput)
    let evaluationPrefixMssi = prepareEvaluation prefixKeys true simpleSorterModelType.Mssi (ModelGenerators.mssiKeys ChannelCountInput) (ModelGenerators.mssi ChannelCountInput)
    let evaluationPrefixMsrs = prepareEvaluation prefixKeys true simpleSorterModelType.Msrs (ModelGenerators.msrsKeys ChannelCountInput) (ModelGenerators.msrs ChannelCountInput)
    let evaluationPrefixMsuf4 = prepareEvaluation prefixKeys true simpleSorterModelType.Msuf4 (ModelGenerators.msuf4Keys ChannelCountInput) (ModelGenerators.msuf4 ChannelCountInput)
    let evaluationPrefixMsuf6 = prepareEvaluation prefixKeys true simpleSorterModelType.Msuf6 (ModelGenerators.msuf6Keys ChannelCountInput) (ModelGenerators.msuf6 ChannelCountInput)
