namespace Sortopia.Functions

open Sortopia.Runs
open Sortopia.Models.Sorting
open InputGenerators

// Adapted from GeneSort's SetupCatalog.runBoth and SortableTestsMakers.getModuleTests.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module SetupFunctions =
    let private testsType = sortopiaDataType.SortableTests ""
    let private modelsType = sortopiaDataType.SorterModelSet ""

    let standardTests =
        FunctionDefinition.setup "1" standardKeys [testsType, queryKeys standardKeys]
            (fun host _ -> host.SetupDb.SaveIfMissing(testsType, [], fun () -> InputGenerators.standardTests host.Parameters))

    let private modelGenerator modelType keys make =
        FunctionDefinition.setupValidated "1" keys [modelsType, queryKeys keys] (ModelGenerators.inputError ChannelCountInput modelType)
            (fun host _ -> host.SetupDb.SaveIfMissing(modelsType, [], fun () -> make host.Parameters))

    let uniformMsceSorterModels = modelGenerator simpleSorterModelType.Msce (ModelGenerators.msceKeys ChannelCountInput) (ModelGenerators.msce ChannelCountInput)
    let uniformMssiSorterModels = modelGenerator simpleSorterModelType.Mssi (ModelGenerators.mssiKeys ChannelCountInput) (ModelGenerators.mssi ChannelCountInput)
    let uniformMsrsSorterModels = modelGenerator simpleSorterModelType.Msrs (ModelGenerators.msrsKeys ChannelCountInput) (ModelGenerators.msrs ChannelCountInput)
    let uniformMsuf4SorterModels = modelGenerator simpleSorterModelType.Msuf4 (ModelGenerators.msuf4Keys ChannelCountInput) (ModelGenerators.msuf4 ChannelCountInput)
    let uniformMsuf6SorterModels = modelGenerator simpleSorterModelType.Msuf6 (ModelGenerators.msuf6Keys ChannelCountInput) (ModelGenerators.msuf6 ChannelCountInput)

    let private prepareEvaluation testsKeys fromSource modelType modelKeys makeModels =
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
