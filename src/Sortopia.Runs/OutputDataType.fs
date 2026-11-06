namespace Sortopia.Runs

/// Output categories used by run configurations and result queries.
[<RequireQualifiedAccess>]
type outputDataType =
    | SortableTestsWithPrefix = 2
    | SorterModelSet = 3
    | SorterSetEval = 4
