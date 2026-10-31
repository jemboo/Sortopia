namespace Sortopia.Reporting
open System


/// Represents the tier assignment for each categorized block of items
type tmbGroup =
    | Top
    | Middle
    | Bottom

module RankedGroup =

    let toString (group: tmbGroup) : string =
        match group with
        | Top -> "Top"
        | Middle -> "Middle"
        | Bottom -> "Bottom"

    let toDataTableRecord (group: tmbGroup) : dataTableRecord =
        dataTableRecord.createEmpty()
        |> dataTableRecord.addData "EvalLabel" (toString group)
