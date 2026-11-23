namespace Sortopia.Runs

open FSharp.UMX

/// Derived from the function and per-output projections; never persisted separately.
type phaseParameterContract = {
    RequiredKeys: Set<string<paramKeyName>>
    OutputQueryKeys: string<paramKeyName> list list
    SpanKeys: Set<string<paramKeyName>>
    DefaultKeys: Set<string<paramKeyName>>
}

type parameterIssueKind =
    | UnsupportedParameter = 0
    | UnexpectedSpan = 1
    | DefaultSpanConflict = 2
    | MissingDefault = 3
    | MissingSpan = 4
    | DuplicateDefault = 5
    | DuplicateSpan = 6
    | EmptySpan = 7

type parameterIssue = { Kind: parameterIssueKind; Key: string<paramKeyName>; Message: string }

module PhaseParameterContract =
    let create (requiredKeys: seq<string<paramKeyName>>) (outputQueryKeys: seq<seq<string<paramKeyName>>>) =
        let required = Set.ofSeq requiredKeys
        let outputs = outputQueryKeys |> Seq.map Seq.toList |> Seq.toList
        let spans = outputs |> List.collect id |> Set.ofList
        { RequiredKeys = required; OutputQueryKeys = outputs; SpanKeys = spans; DefaultKeys = Set.difference required spans }

    let private issue kind key message = { Kind = kind; Key = key; Message = message }

    let spanKeysIssues (queryKeys: seq<string<paramKeyName>>) (spanKeys: seq<string<paramKeyName>>) =
        let query, spans = Set.ofSeq queryKeys, Set.ofSeq spanKeys
        [ for key in Set.difference query spans do
              yield issue parameterIssueKind.MissingSpan key $"Query keys require explicit parameter spans: {key}. Add a span for each key, using one value if it is fixed. Defaults do not satisfy query keys."
          for key in Set.difference spans query do
              yield issue parameterIssueKind.UnexpectedSpan key $"‘{key}’ is not a project query key. Undeclared span keys: {key}. Select this query key on the project, or supply a fixed default and remove the span." ]

    /// Checks structural input rules without parsing values, so incomplete drafts can be inspected.
    let keyIssues contract (defaultKeys: seq<string<paramKeyName>>) (spanKeys: seq<string<paramKeyName>>) =
        let defaults, spans = Seq.toList defaultKeys, Seq.toList spanKeys
        let defaultSet, spanSet = Set.ofList defaults, Set.ofList spans
        [ for key in Set.difference (Set.union defaultSet spanSet) contract.RequiredKeys do
              yield issue parameterIssueKind.UnsupportedParameter key $"Parameters not required by the selected function: {key}."
          for key in Set.intersect defaultSet (Set.union spanSet contract.SpanKeys) do
              yield issue parameterIssueKind.DefaultSpanConflict key $"‘{key}’ already has a default. Parameters cannot have both a default and a span: {key}. Query keys require spans and cannot have defaults. Remove or exclude the default on the project."
          yield! spanKeysIssues contract.SpanKeys spans
          for key in Set.difference contract.DefaultKeys defaultSet do
              yield issue parameterIssueKind.MissingDefault key $"Missing values for required parameters: {key}. Non-query parameters need fixed defaults on the project or an ancestor module."
          for key, count in List.countBy id defaults do
              if count > 1 then yield issue parameterIssueKind.DuplicateDefault key $"Default keys must be unique: {key}."
          for key, count in List.countBy id spans do
              if count > 1 then yield issue parameterIssueKind.DuplicateSpan key $"Parameter keys must be unique: {key}. Keep one span for this parameter." ]

    let spanValuesIssues key count =
        if count > 0 then []
        else [ issue parameterIssueKind.EmptySpan key "Add at least one value. Blank values are allowed for text parameters." ]

    let messages (issues: seq<parameterIssue>) = issues |> Seq.map _.Message |> String.concat " "
