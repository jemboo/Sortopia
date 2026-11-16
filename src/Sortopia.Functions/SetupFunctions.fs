namespace Sortopia.Functions

open Sortopia.Runs
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

    /// Copy both evaluation inputs unchanged, using a separate source project per type.
    let private copyEvaluationInputs (host: runHost) (projects: sourceProjects) =
        let modelsReference = host.SourceResults.Find(projects[modelsType], modelsType, host.Parameters)
        let testsReference = host.SourceResults.Find(projects[testsType], testsType, host.Parameters)
        let modelsValue = host.SourceResults.Load modelsReference
        let testsValue = host.SourceResults.Load testsReference
        // Validate the actual data before publishing either local copy, including on retries.
        let tests, prefix =
            match testsValue with
            | functionData.SortableTests (tests, prefix) -> tests, prefix
            | _ -> invalidOp "Expected source SortableTests."
        let channels = Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests
        match host.Parameters.TryFind ParamKeys.channelCountKey with
        | Some requested when requested <> int channels -> invalidArg "ChannelCount" "The source SortableTests must match the queried ChannelCount."
        | _ -> ()
        match modelsValue with
        | functionData.SorterModelSet models ->
            EvaluationInputs.validateCopiedModels channels prefix models
        | _ -> invalidOp "Expected a source SorterModelSet."
        host.SetupDb.SaveIfMissing(modelsType, [modelsReference], fun () -> modelsValue)
        host.SetupDb.SaveIfMissing(testsType, [testsReference], fun () -> testsValue)

    let evaluationSource =
        FunctionDefinition.setupCopy "2" [modelsType; testsType] copyEvaluationInputs
        |> FunctionDefinition.describe "Copy sorter models and sortable tests from separately selected source projects. Supply every source results query key through a default or run span. Each local setup projection may retain a subset of its source's keys. Preserve models, activators, tests, and prefixes; validate matching widths and compatible prefixes before saving either input. No models or tests are generated."
