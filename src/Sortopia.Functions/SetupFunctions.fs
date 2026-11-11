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

    let uniformSorterModels =
        FunctionDefinition.setup "1" modelKeys [modelsType, queryKeys modelKeys]
            (fun host _ -> host.SetupDb.SaveIfMissing(modelsType, [], fun () -> uniformModels host.Parameters))

    let private prepareEvaluation version testsKeys fromSource =
        FunctionDefinition.setupWithSources version
            (testsKeys @ modelKeys |> List.distinctBy (fun key -> key.Name))
            [testsType, queryKeys testsKeys; modelsType, queryKeys modelKeys]
            (if fromSource then [testsType] else [])
            (fun host projects ->
                let sources =
                    if fromSource then
                        let reference = host.SourceResults.Find(projects[testsType], testsType, host.Parameters)
                        // Validate source/model compatibility even when both outputs already exist.
                        sourceTests host.Parameters [host.SourceResults.Load reference] |> ignore
                        [reference]
                    else []
                host.SetupDb.SaveIfMissing(modelsType, [], fun () -> uniformModels host.Parameters)
                host.SetupDb.SaveIfMissing(testsType, sources, fun () ->
                    if fromSource then sourceTests host.Parameters [host.SourceResults.Load sources.Head]
                    else InputGenerators.standardTests host.Parameters))

    let evaluationStandard = prepareEvaluation "1" standardKeys false
    // Source project IDs replace GeneSort's SortableTestsModule name. The library
    // key and format look up the source's exact result projection.
    let evaluationMerge = prepareEvaluation "1" mergeKeys true
    let evaluationPrefix = prepareEvaluation "1" prefixKeys true
