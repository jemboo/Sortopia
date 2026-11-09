namespace Sortopia.Runs

/// A data category and its free-form text, used by run configurations and queries.
[<RequireQualifiedAccess>]
type sortopiaDataType =
    | Workspace of string
    | Run of string
    | WorkspaceModule of string
    | RunParameters of string
    | SorterPoolSet of string
    | SorterSet of string
    | SorterModelSet of string
    | SortableTests of string
    | SorterSetEval of string
    | TextReport of string

module SortopiaDataType =
    let kinds = [ "Workspace"; "Run"; "WorkspaceModule"; "RunParameters"; "SorterPoolSet"; "SorterSet"; "SorterModelSet"; "SortableTests"; "SorterSetEval"; "TextReport" ]
    let kindName = function
        | sortopiaDataType.Workspace _ -> "Workspace"
        | sortopiaDataType.Run _ -> "Run"
        | sortopiaDataType.WorkspaceModule _ -> "WorkspaceModule"
        | sortopiaDataType.RunParameters _ -> "RunParameters"
        | sortopiaDataType.SorterPoolSet _ -> "SorterPoolSet"
        | sortopiaDataType.SorterSet _ -> "SorterSet"
        | sortopiaDataType.SorterModelSet _ -> "SorterModelSet"
        | sortopiaDataType.SortableTests _ -> "SortableTests"
        | sortopiaDataType.SorterSetEval _ -> "SorterSetEval"
        | sortopiaDataType.TextReport _ -> "TextReport"
    let text = function
        | sortopiaDataType.Workspace value | sortopiaDataType.Run value | sortopiaDataType.WorkspaceModule value
        | sortopiaDataType.RunParameters value | sortopiaDataType.SorterPoolSet value | sortopiaDataType.SorterSet value
        | sortopiaDataType.SorterModelSet value | sortopiaDataType.SortableTests value | sortopiaDataType.SorterSetEval value
        | sortopiaDataType.TextReport value -> value
    let validate value =
        if isNull (box value) then nullArg "dataType"
        if isNull (text value) then nullArg "text"
    let create kind value =
        if isNull value then nullArg "value"
        match kind with
        | "Workspace" -> sortopiaDataType.Workspace value
        | "Run" -> sortopiaDataType.Run value
        | "WorkspaceModule" -> sortopiaDataType.WorkspaceModule value
        | "RunParameters" -> sortopiaDataType.RunParameters value
        | "SorterPoolSet" -> sortopiaDataType.SorterPoolSet value
        | "SorterSet" -> sortopiaDataType.SorterSet value
        | "SorterModelSet" -> sortopiaDataType.SorterModelSet value
        | "SortableTests" -> sortopiaDataType.SortableTests value
        | "SorterSetEval" -> sortopiaDataType.SorterSetEval value
        | "TextReport" -> sortopiaDataType.TextReport value
        | _ -> invalidArg "kind" $"Unknown Sortopia data type: {kind}"
