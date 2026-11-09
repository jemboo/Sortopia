namespace Sortopia.Functions

open FSharp.UMX
open Sortopia.Runs

module ResultsFunctionNames =
    let sortableTests : string<resultsFunctionName> = UMX.tag "sortable-tests.publish"
    let sorterEval : string<resultsFunctionName> = UMX.tag "sorter-eval.evaluate"

module SetupFunctionNames =
    let standardTests : string<setupFunctionName> = UMX.tag "sortable-tests-with-prefix.standard"
    let uniformSorterModels : string<setupFunctionName> = UMX.tag "sorter-model-set.uniform"
