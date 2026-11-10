namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

module ResultsFunctionNames =
    let mergeSortableTests : string<resultsFunctionName> = UMX.tag "sortable-tests.merge"
    let prefixSortableTests : string<resultsFunctionName> = UMX.tag "sortable-tests.prefix"
    let sorterEval : string<resultsFunctionName> = UMX.tag "sorter-eval.evaluate"
    let standardSortableTests : string<resultsFunctionName> = UMX.tag "sortable-tests.standard"
    let uniformSorterModels : string<resultsFunctionName> = UMX.tag "sorter-model-set.uniform"
    let standardEvaluationInputs : string<resultsFunctionName> = UMX.tag "sorter-eval-inputs.standard"
    let mutantEval : string<resultsFunctionName> = UMX.tag "sorter-mutants.evaluate"

module SetupFunctionNames =
    let standardTests : string<setupFunctionName> = UMX.tag "sortable-tests-with-prefix.standard"
    let uniformSorterModels : string<setupFunctionName> = UMX.tag "sorter-model-set.uniform"
    let evaluationStandard : string<setupFunctionName> = UMX.tag "sorter-eval.standard"
    let evaluationMerge : string<setupFunctionName> = UMX.tag "sorter-eval.merge"
    let evaluationPrefix : string<setupFunctionName> = UMX.tag "sorter-eval.prefix"
