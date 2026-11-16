using System.Globalization;
using System.Text;
using Microsoft.FSharp.Collections;
using Microsoft.VisualBasic.FileIO;
using Sortopia.Evaluation;
using Sortopia.Reporting;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class TypedReportTests
{
    internal static Dictionary<string, string>[] ReadCsv(string path)
    {
        using var parser = new TextFieldParser(path, Encoding.UTF8) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        parser.SetDelimiters(","); var headers = parser.ReadFields()!;
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var values = parser.ReadFields()!; Assert.Equal(headers.Length, values.Length);
            rows.Add(headers.Zip(values).ToDictionary(pair => pair.First, pair => pair.Second));
        }
        return rows.ToArray();
    }

    [Fact]
    public void Typed_csv_preserves_values_quotes_and_missing_cells_with_a_stable_schema()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            reportColumn[] columns = [new("Number", reportValueType.Number), new("Count", reportValueType.Unsigned64),
                new("Text", reportValueType.Text), new("Boolean", reportValueType.Boolean), new("Missing", reportValueType.Integer)];
            var enumerations = 0;
            IEnumerable<FSharpMap<string, reportValue>> Rows()
            {
                enumerations++;
                yield return ReportRow.create(new[] {
                    Tuple.Create("Number", reportValue.NewNumber(-0.35)), Tuple.Create("Count", reportValue.NewUnsigned64(ulong.MaxValue)),
                    Tuple.Create("Text", reportValue.NewText("comma, tab\t and \"quote\"\r\nnext line")), Tuple.Create("Boolean", reportValue.NewBoolean(true)) });
                yield return ReportRow.create(new[] { Tuple.Create("Text", reportValue.NewText("=1+1")) });
            }
            using var output = new MemoryStream();
            CsvReport.write(output, new typedReport(columns, Rows()), TestContext.Current.CancellationToken);
            Assert.Equal(1, enumerations); Assert.Equal(new byte[] { 239, 187, 191 }, output.ToArray()[..3]);
            var text = Encoding.UTF8.GetString(output.ToArray()[3..]);
            Assert.StartsWith("\"Number\",\"Count\",\"Text\",\"Boolean\",\"Missing\"\r\n", text);
            Assert.Contains("-0.35,18446744073709551615,\"comma, tab\t and \"\"quote\"\"\r\nnext line\",TRUE,\r\n", text);
            Assert.EndsWith(",,\"'=1+1\",,\r\n", text);
            using var empty = new MemoryStream();
            CsvReport.write(empty, new typedReport(columns, []), TestContext.Current.CancellationToken);
            Assert.Single(Encoding.UTF8.GetString(empty.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void Typed_reports_reject_collisions_unknown_columns_wrong_types_nonfinite_numbers_and_cancel()
    {
        var row = ReportRow.create(new[] { Tuple.Create("Value", reportValue.NewNumber(1)) });
        Assert.Throws<ArgumentException>(() => ReportRow.combine(row, row));
        using var output = new MemoryStream();
        Assert.Throws<ArgumentException>(() => new CsvReportWriter(output, [new("Value", reportValueType.Number), new("Value", reportValueType.Number)]));
        using var writer = new CsvReportWriter(output, [new("Value", reportValueType.Number)]);
        Assert.Throws<ArgumentException>(() => writer.Write(ReportRow.create(new[] { Tuple.Create("Other", reportValue.NewNumber(1)) }), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => writer.Write(ReportRow.create(new[] { Tuple.Create("Value", reportValue.NewText("1")) }), TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => writer.Write(ReportRow.create(new[] { Tuple.Create("Value", reportValue.NewNumber(double.NaN)) }), TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => writer.Write(row, cancelled.Token));
    }

    internal static sorterEval Eval(int unsorted, int ces, int stages) => sorterEval.NewV1(sorterEvalV1.create(Guid.NewGuid(), "all", 4, unsorted, 123, ces - 1, stages, ces, true, 2, 1));
    internal static sorterSetEval Set(params sorterEval[] values) => sorterSetEval.create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), values);

    [Fact]
    public void Summaries_use_successful_networks_for_lengths_and_distinguish_empty_from_zero_success()
    {
        var source = Set(Eval(0, 8, 6), Eval(0, 4, 2), Eval(3, 1, 1));
        var summary = SorterEvalReports.summarize(source);
        Assert.Equal(3, summary.EvaluatedSorterCount); Assert.Equal(2, summary.SuccessfulSorterCount);
        Assert.Equal(2.0 / 3.0, summary.ObservedSuccessFraction.Value);
        Assert.Equal(4, summary.SuccessfulMinCeLength.Value); Assert.Equal(6, summary.SuccessfulMedianCeLength.Value);
        Assert.Equal(2, summary.SuccessfulMinStageLength.Value); Assert.Equal(4, summary.SuccessfulMedianStageLength.Value);
        var failed = SorterEvalReports.summarize(Set(Eval(1, 1, 1)));
        Assert.Equal(0, failed.ObservedSuccessFraction.Value); Assert.Null(failed.SuccessfulMedianCeLength);
        var empty = SorterEvalReports.summarize(Set());
        Assert.Equal(0, empty.EvaluatedSorterCount); Assert.Null(empty.ObservedSuccessFraction); Assert.Null(empty.ChannelCount);
        Assert.True(SorterEvalReports.summaryRow(empty)["ObservedSuccessFraction"].IsMissing);
        var details = SorterEvalReports.detailRow(source.SorterEvals[0]);
        Assert.True(details["SorterId"].IsIdentifier); Assert.True(details["IsSorted"].IsBoolean); Assert.True(details["CeLength"].IsInteger);
    }
}
