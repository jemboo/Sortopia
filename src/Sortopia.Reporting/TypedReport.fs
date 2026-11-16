namespace Sortopia.Reporting

open System
open System.Globalization
open System.IO
open System.Text
open System.Threading

[<RequireQualifiedAccess>]
type reportValueType = Text | Integer | Unsigned64 | Number | Boolean | Identifier

[<RequireQualifiedAccess>]
type reportValue =
    | Missing
    | Text of string
    | Integer of int64
    | Unsigned64 of uint64
    | Number of float
    | Boolean of bool
    | Identifier of Guid

type reportColumn = { Name: string; ValueType: reportValueType }
type reportRow = Map<string, reportValue>
type typedReport = { Columns: reportColumn array; Rows: reportRow seq }

module ReportRow =
    /// Duplicate names are errors, including when joining provenance and metrics.
    let create (values: (string * reportValue) seq) : reportRow =
        values |> Seq.fold (fun row (name, value) ->
            if String.IsNullOrWhiteSpace name then invalidArg "values" "Report columns need a name."
            if Map.containsKey name row then invalidArg "values" $"Duplicate report column '{name}'."
            Map.add name value row) Map.empty

    let combine (first: reportRow) (second: reportRow) =
        Seq.append (Map.toSeq first) (Map.toSeq second) |> create

module ReportValue =
    let valueType = function
        | reportValue.Missing -> None
        | reportValue.Text _ -> Some reportValueType.Text
        | reportValue.Integer _ -> Some reportValueType.Integer
        | reportValue.Unsigned64 _ -> Some reportValueType.Unsigned64
        | reportValue.Number _ -> Some reportValueType.Number
        | reportValue.Boolean _ -> Some reportValueType.Boolean
        | reportValue.Identifier _ -> Some reportValueType.Identifier

    let format = function
        | reportValue.Missing -> ""
        | reportValue.Text text -> if isNull text then invalidArg "value" "Use Missing for an absent text value." else text
        | reportValue.Integer value -> value.ToString(CultureInfo.InvariantCulture)
        | reportValue.Unsigned64 value -> value.ToString(CultureInfo.InvariantCulture)
        | reportValue.Number value ->
            if not (Double.IsFinite value) then invalidArg "value" "Report numbers must be finite."
            value.ToString("R", CultureInfo.InvariantCulture)
        | reportValue.Boolean value -> if value then "TRUE" else "FALSE"
        | reportValue.Identifier value -> value.ToString("D")

/// RFC-style comma quoting, invariant numbers, CRLF records, and UTF-8 BOM for Excel.
/// The caller owns the stream. Rows are validated and written without buffering a table.
type CsvReportWriter(stream: Stream, columns: reportColumn array) =
    let columns = Array.copy columns
    let names = columns |> Array.map _.Name |> Set.ofArray
    do
        if columns.Length = 0 || names.Count <> columns.Length || columns |> Array.exists (fun column -> String.IsNullOrWhiteSpace column.Name) then
            invalidArg "columns" "Supply a nonempty ordered schema with unique column names."
    let writer = new StreamWriter(stream, UTF8Encoding(true), 65536, true)
    let quote (text: string) = "\"" + text.Replace("\"", "\"\"") + "\""
    let textCell (text: string) =
        // Keep text as text when Excel opens a CSV directly; quoting alone does not
        // prevent a leading formula operator from being interpreted as a formula.
        let trimmed = text.TrimStart()
        let safe = if trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) then "'" + text else text
        quote safe
    do
        writer.NewLine <- "\r\n"
        columns |> Array.map (_.Name >> textCell) |> String.concat "," |> writer.WriteLine

    member _.Write(row: reportRow, token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        for KeyValue(name, _) in row do
            if not (Set.contains name names) then invalidArg "row" $"Unknown report column '{name}'."
        let cells = columns |> Array.map (fun column ->
            let value = Map.tryFind column.Name row |> Option.defaultValue reportValue.Missing
            if ReportValue.valueType value |> Option.exists ((<>) column.ValueType) then
                invalidArg "row" $"Incorrect value type for report column '{column.Name}'."
            let text = ReportValue.format value
            match value with
            | reportValue.Text _ -> textCell text
            | reportValue.Identifier _ -> quote text
            | _ -> text)
        writer.WriteLine(String.Join(",", cells))

    interface IDisposable with member _.Dispose() = writer.Dispose()

module CsvReport =
    let write (stream: Stream) (report: typedReport) (token: CancellationToken) =
        token.ThrowIfCancellationRequested()
        use writer = new CsvReportWriter(stream, report.Columns)
        for row in report.Rows do writer.Write(row, token)
