namespace Sortopia.Sorting

/// Target fraction of generated networks that sort every input.
[<RequireQualifiedAccess>]
type sorterSuccessFraction =
    | pct50
    | pct90
    | pct99

module SorterSuccessFraction =
    let all = [sorterSuccessFraction.pct50; sorterSuccessFraction.pct90; sorterSuccessFraction.pct99]
    let percent = function
        | sorterSuccessFraction.pct50 -> 50
        | sorterSuccessFraction.pct90 -> 90
        | sorterSuccessFraction.pct99 -> 99
    let toString value = $"{percent value}%%"
    let fromString = function
        | "50%" | "pct50" -> sorterSuccessFraction.pct50
        | "90%" | "pct90" -> sorterSuccessFraction.pct90
        | "99%" | "pct99" -> sorterSuccessFraction.pct99
        | _ -> invalidArg "value" "Choose 50%, 90%, or 99% whole-network success."
