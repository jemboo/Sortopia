namespace Sortopia.Functions

open Sortopia.Runs
open InputGenerators

// Adapted from GeneSort's SetupCatalog.runBoth and SortableTestsMakers.getModuleTests.
// Copyright (c) 2025 Tom Patterson. See GENESORT-LICENSE.txt.
module SetupFunctions =
    let standardTests =
        FunctionDefinition.setup [sortopiaDataType.SortableTests "", standardKeys]
            (fun context parameters _ ->
                context.GetOrCreate("SortableTests", [], fun () -> attempt (fun () -> InputGenerators.standardTests parameters))
                |> Result.map List.singleton)

    let uniformSorterModels =
        FunctionDefinition.setup [sortopiaDataType.SorterModelSet "", modelKeys]
            (fun context parameters _ ->
                context.GetOrCreate("SorterModelSet", [], fun () -> attempt (fun () -> uniformModels parameters))
                |> Result.map List.singleton)

    let private prepareEvaluation testsKeys sourceKinds makeTests =
        FunctionDefinition.setupWithSources
            [sortopiaDataType.SortableTests "", testsKeys; sortopiaDataType.SorterModelSet "", modelKeys]
            (sourceKinds |> List.map (fun kind -> SortopiaDataType.create kind ""))
            (fun context parameters inputs ->
                let sourceCheck = if List.isEmpty sourceKinds then Ok () else attempt (fun () -> makeTests parameters inputs |> ignore)
                // Each output checks its own projection before invoking its factory.
                sourceCheck |> Result.bind (fun () -> context.GetOrCreate("SorterModelSet", [], fun () -> attempt (fun () -> uniformModels parameters)))
                |> Result.bind (fun models ->
                    context.GetOrCreate("SortableTests", sourceKinds, fun () -> attempt (fun () -> makeTests parameters inputs))
                    |> Result.map (fun tests -> [models; tests])))

    let evaluationStandard = prepareEvaluation standardKeys [] (fun parameters _ -> InputGenerators.standardTests parameters)
    // Source project IDs replace GeneSort's SortableTestsModule name. The library
    // key and format look up the source's exact result projection.
    let evaluationMerge = prepareEvaluation mergeKeys ["SortableTests"] sourceTests
    let evaluationPrefix = prepareEvaluation prefixKeys ["SortableTests"] sourceTests
