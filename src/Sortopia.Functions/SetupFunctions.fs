namespace Sortopia.Functions

open Sortopia.Runs
open InputGenerators

// Adapted from GeneSort's SetupCatalog.runBoth and SortableTestsMakers.getModuleTests.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module SetupFunctions =
    let private testsType = sortopiaDataType.SortableTests ""
    let private modelsType = sortopiaDataType.SorterModelSet ""

    let standardTests =
        FunctionDefinition.setup "1" (setupParameterRequirements.Declared standardKeys)
            [{ DataType = testsType; ParameterKeys = queryKeys standardKeys; Dependencies = [] }] ParameterDefaults.support
            (fun host _ -> host.Database.Save(host.Outputs.Output(testsType, []), fun () -> InputGenerators.standardTests host.Parameters))
        |> FunctionDefinition.describe "Generate the full set of binary sortable tests for ChannelCount in the selected data format. Save the tests in the setup database."

    /// Copy both evaluation inputs unchanged, using a separate source project per type.
    let private copyEvaluationInputs (host: runHost) (projects: sourceProjects) =
        let modelsReference = host.Database.Find(projects[modelsType], modelsType, host.Parameters)
        let testsReference = host.Database.Find(projects[testsType], testsType, host.Parameters)
        let modelsValue = host.Database.Load modelsReference
        let testsValue = host.Database.Load testsReference
        // Validate the actual data before publishing either local copy, including on retries.
        let tests =
            match testsValue with
            | sortopiaStorageData.SortableTests tests -> tests
            | _ -> invalidOp "Expected source SortableTests."
        let channels = Sortopia.Sorting.Inputs.SortableTests.getChannelCount tests
        match host.Parameters.TryFind ParamKeys.channelCountKey with
        | Some requested when requested <> int channels -> invalidArg "ChannelCount" "The source SortableTests must match the queried ChannelCount."
        | _ -> ()
        match modelsValue with
        | sortopiaStorageData.SorterModelSet models ->
            EvaluationInputs.validateCopiedModels channels models
        | _ -> invalidOp "Expected a source SorterModelSet."
        host.Database.Save(host.Outputs.Output(modelsType, [modelsReference]), fun () -> modelsValue)
        host.Database.Save(host.Outputs.Output(testsType, [testsReference]), fun () -> testsValue)

    let evaluationSource =
        let support =
            { FunctionParameterSupport.empty with
                Defaults = ParameterDefaults.combine [ParameterDefaults.common; ModelGenerators.sourceDefaults]
                EditorRules = ParameterRules.combine [ParameterRules.common; ModelGenerators.sourceRules]
                Guidance = fun _ _ -> "Copy both evaluation inputs unchanged from the selected source projects. Their results query keys determine the required parameters. Supply every source key through a default or span; each local setup projection may select a subset of its source's keys. Model families, prefixes, suffix lengths, and activators are inherited from the source data." }
        FunctionDefinition.setup "2" setupParameterRequirements.FromSources
            [{ DataType = modelsType; ParameterKeys = []; Dependencies = [modelsType] }
             { DataType = testsType; ParameterKeys = []; Dependencies = [testsType] }] support copyEvaluationInputs
        |> FunctionDefinition.describe "Copy sorter models and sortable tests from separately selected source projects. Supply every source results query key through a default or run span. Each local setup projection may retain a subset of its source's keys. Preserve models, their prefixes and activators, and the tests; validate matching widths before saving either input. No models or tests are generated."
