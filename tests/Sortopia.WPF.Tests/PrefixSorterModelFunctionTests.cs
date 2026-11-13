using System.IO;
using Microsoft.FSharp.Collections;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class PrefixSorterModelFunctionTests
{
    private static string Prefix(int width) => PrefixLibId.toString(prefixLibId.create(width, 1, prefixLibVariant.Prefix1));
    private static parameterBinding Bind(string key, string text, parameterValueType type = parameterValueType.Integer) => ParameterBinding.fromText(key, type, text);
    private static string Function(string model) => $"sorter-model-set.prefix.{model.ToLowerInvariant()}";
    private static parameterBinding[] Defaults(string model = "Mssi", int width = 8, int repl = 0) => [
        Bind("SorterModelType", "Simple", parameterValueType.Text), Bind("AMax", "1"),
        Bind("ActivationProbability", "0.5", parameterValueType.Floating),
        ParameterBinding.create(ParamKeys.prefixLibIdKey, prefixLibId.create(width, 1, prefixLibVariant.Prefix1)),
        Bind("RngType", "SplitMix64", parameterValueType.Text),
        Bind("StageLength", "2"), Bind("SorterCount", "3"), Bind("Repl", repl.ToString()), .. ModelDefaults(model, width)];
    private static parameterBinding[] ModelDefaults(string model, int width) => model switch {
        "Msce" => [Bind("ExcludeSelfCe", "true", parameterValueType.Boolean)],
        "Msrs" => [Bind("StageWeights", "1,1,1", parameterValueType.OpsWeights)],
        "Msuf4" or "Msuf6" => [model == "Msuf4" ? Bind("Seed4Weights", "1,1,1", parameterValueType.OpsWeights)
            : Bind("Seed6Weights", "1,1,1,1,1,1,1", parameterValueType.Seed6Weights),
            Bind("UnfoldingWeights", string.Join(";", Enumerable.Repeat("1,1,1", Math.Max(0, (int)Math.Ceiling(Math.Log2(width / (model == "Msuf4" ? 4.0 : 6.0)))))), parameterValueType.UnfoldingWeights)],
        _ => []
    };
    private static parameterSet Parameters(string model = "Mssi", int width = 8, int repl = 0) => parameterSet.fromBindings(ListModule.OfArray(Defaults(model, width, repl)));
    private static sorterModelSet Generate(parameterSet parameters, string model = "Mssi") => Assert.IsType<functionData.SorterModelSet>(
        Assert.Single(CatalogTestHost.Invoke(false, Function(model), parameters))).Item;

    private static void AssertFamily(string family, sorterModel model) => Assert.True(family switch {
        "Simple" => model.IsSimple, "SimpleR" => model.IsSimpleR, "SimpleS" => model.IsSimpleS, _ => false
    });

    public static IEnumerable<object[]> ModelChoices =>
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        from pair in new[] { ("Msce", 8), ("Msce", 9), ("Mssi", 9), ("Msrs", 8), ("Msrs", 18), ("Msuf4", 8), ("Msuf4", 16), ("Msuf6", 12), ("Msuf6", 24) }
        select new object[] { family, pair.Item1, pair.Item2 };

    [Theory]
    [MemberData(nameof(ModelChoices))]
    public void Generates_selected_models_with_the_catalog_prefix_and_random_suffix(string family, string type, int width)
    {
        parameterSet Inputs(int repl = 0) => Parameters(type, width, repl).WithBinding(ParameterBinding.create(ParamKeys.sorterModelTypeKey, family));
        var set = Generate(Inputs(), type);
        var expectedPrefix = SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)).Value;
        Assert.Equal(3, set.Count);
        Assert.True(set.RawCeLength > 0);
        Assert.All(set.SorterModels, model => {
            AssertFamily(family, model);
            var simple = model switch {
                sorterModel.Simple value => value.Item,
                sorterModel.SimpleR value => value.Item.Model,
                sorterModel.SimpleS value => value.Item.Model,
                _ => throw new InvalidOperationException()
            };
            Assert.True(type switch {
                "Msce" => simple.IsMsce, "Mssi" => simple.IsMssi, "Msrs" => simple.IsMsrs,
                "Msuf4" => simple.IsMsuf4, "Msuf6" => simple.IsMsuf6, _ => false
            });
            Assert.Equal(type == "Msce" ? width : 2, SimpleSorterModel.getLength(simple));
            Assert.Equal(width, SorterModel.getChannelCount(model));
            Assert.Equal(expectedPrefix, SorterModel.getPrefix(model));
            var sorter = SorterModel.makeSorterWithPrefix(model, null!);
            Assert.Equal(expectedPrefix, sorter.Ces.Take(expectedPrefix.Length));
            Assert.True(sorter.Ces.Length >= expectedPrefix.Length);
            if (family == "Simple") Assert.True(sorter.Ces.Length > expectedPrefix.Length);
            if (model is sorterModel.SimpleR r) Assert.All(r.Item.Activators, value => Assert.InRange(value, -1, 1));
        });
        var repeated = Generate(Inputs(), type);
        Assert.Equal(set.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
        foreach (var pair in set.SorterModels.Zip(repeated.SorterModels))
            Assert.Equal(SorterModel.makeSorterWithPrefix(pair.First, null!).Ces, SorterModel.makeSorterWithPrefix(pair.Second, null!).Ces);
        Assert.Empty(set.SorterModels.Select(SorterModel.getId).Intersect(Generate(Inputs(1), type).SorterModels.Select(SorterModel.getId)));
    }

    [Theory]
    [InlineData("Msuf4", 18, "power-of-two")]
    [InlineData("Msuf6", 16, "six times")]
    [InlineData("Msuf6", 18, "six times")]
    [InlineData("Msrs", 9, "even")]
    public void Incompatible_model_and_prefix_are_rejected_by_execution_and_output_planning(string type, int width, string reason)
    {
        var values = Parameters(type, width);
        var error = Assert.Throws<ArgumentException>(() => Generate(values, type));
        Assert.Contains(type, error.Message); Assert.Contains("PrefixLibId", error.Message); Assert.Contains(reason, error.Message);
        var config = new resultsConfig("Results", Function(type), FunctionCatalogs.requireResults(Function(type)).Version,
            [new resultsDataTypeConfig(sortopiaDataType.NewSorterModelSet(""), ["PrefixLibId"])], []);
        Assert.Contains(reason, Assert.Throws<ArgumentException>(() => FileResultsHost.plan(Guid.NewGuid(), config, values, [])).Message);
    }

    [Theory]
    [InlineData("StageLength", "0", "at least 1")]
    [InlineData("StageLength", "2147483647", "suffix-size")]
    [InlineData("SorterCount", "0", "positive")]
    [InlineData("Repl", "-1", "nonnegative")]
    [InlineData("Repl", "2147483647", "indices")]
    [InlineData("RngType", "missing", "rngType")]
    [InlineData("SorterModelType", "Unknown", "Unknown cannot generate")]
    [InlineData("SorterModelType", "missing", "Choose Simple")]
    [InlineData("AMax", "-1", "AMax must be nonnegative")]
    [InlineData("ActivationProbability", "-0.1", "between 0 and 1")]
    [InlineData("ActivationProbability", "1.1", "between 0 and 1")]
    [InlineData("PrefixLibId", "PrefixLibId(channelCount=16, stageLength=2, Variant=PrefixA)", "not available")]
    public void Invalid_values_fail_before_generation(string key, string text, string reason)
    {
        var defaults = Defaults(); var original = defaults.Single(value => value.KeyName == key);
        defaults[Array.IndexOf(defaults, original)] = Bind(key, text, original.ValueType);
        Assert.Contains(reason, Assert.Throws<ArgumentException>(() => Generate(parameterSet.fromBindings(ListModule.OfArray(defaults)))).Message);
    }

    [Fact]
    public void Domain_validation_checks_partial_defaults_and_all_span_values_beyond_the_preview_limit()
    {
        var definition = FunctionCatalogs.requireResults(ResultsFunctionNames.prefixMsuf4SorterModels);
        Assert.Empty(definition.SourceDataTypes);
        Assert.Equal(new[] { "ActivationProbability", "AMax", "PrefixLibId", "Repl", "RngType", "Seed4Weights", "SorterCount", "SorterModelType", "StageLength", "UnfoldingWeights" }, definition.RequiredParameters.Select(key => key.Name).Order());
        Assert.True(Assert.Single(definition.SortopiaDataTypes).IsSorterModelSet);
        var defaults = ListModule.OfSeq(Defaults("Msuf4").Where(value => value.KeyName != "PrefixLibId"));
        FunctionValidation.validateDefaults(definition, defaults);
        var spans = ListModule.Singleton(ParameterSpan.fromTexts("PrefixLibId", parameterValueType.PrefixLibId,
            ListModule.OfSeq(Enumerable.Repeat(Prefix(8), 50).Append(Prefix(18)))));
        Assert.Contains("incompatible", Assert.Throws<ArgumentException>(() => FunctionValidation.validateInputs(definition, ["PrefixLibId"], defaults, spans)).Message);
        var weights = ListModule.Singleton(ParameterSpan.fromTexts("UnfoldingWeights", parameterValueType.UnfoldingWeights, ["1,1,1", "1,1,1;1,1,1"]));
        Assert.Contains("needs 1 unfolding", definition.InputError.Invoke([Defaults().Single(binding => binding.KeyName == "PrefixLibId")]).Invoke(weights));
    }

    private static WorkspaceTreeNode Module()
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Prefix models");
        Configure(module); return module;
    }

    [Theory]
    [InlineData("SimpleR", 0.0)]
    [InlineData("SimpleS", 0.0)]
    [InlineData("SimpleS", 1.0)]
    public void Activator_controls_affect_the_suffix_and_preserve_the_prefix(string family, double probability)
    {
        var inputs = Parameters().WithBinding(ParameterBinding.create(ParamKeys.sorterModelTypeKey, family))
            .WithBinding(ParameterBinding.create(ParamKeys.aMaxKey, 0))
            .WithBinding(ParameterBinding.create(ParamKeys.activationProbabilityKey, probability));
        foreach (var model in Generate(inputs).SorterModels) {
            var prefix = SorterModel.getPrefix(model);
            var full = SorterModel.makeSorterWithPrefix(model, null!).Ces;
            if (probability == 0) Assert.Equal(prefix, full);
            else Assert.True(full.Length > prefix.Length);
            if (model is sorterModel.SimpleR r) { Assert.Equal(0, r.Item.AMax); Assert.All(r.Item.Activators, value => Assert.Equal(0, value)); }
            if (model is sorterModel.SimpleS s) Assert.All(s.Item.Activators, value => Assert.Equal(probability == 1, value));
        }
    }

    [Fact]
    public void Family_picker_works_in_defaults_and_spans_and_rejects_unknown_before_preview()
    {
        var module = Module(); var defaults = module.Editor!.ResultsParameterDefaults;
        var family = defaults.Single(row => row.Key == "SorterModelType");
        Assert.Equal(new[] { "Simple", "SimpleR", "SimpleS", "Unknown" }, family.Value.Choices);
        Assert.Equal("Simple", family.Value.Text);
        Assert.Equal("1", defaults.Single(row => row.Key == "AMax").Value.Text);
        Assert.Equal("0.5", defaults.Single(row => row.Key == "ActivationProbability").Value.Text);
        family.Value.Text = "Unknown"; Assert.Contains("Unknown cannot generate", module.Editor.ResultsParameterDefaultsError);
        family.RemoveCommand.Execute(null);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        var editor = run.Editor!; editor.AddMissingResultsParametersCommand.Execute(null);
        var span = Assert.Single(editor.ResultsParameters); Assert.Equal("SorterModelType", span.Key);
        foreach (var name in new[] { "Simple", "SimpleR", "SimpleS" }) { span.AddValueCommand.Execute(null); span.Values.Last().Text = name; }
        Assert.Equal(family.Value.Choices, span.Values[0].Choices);
        Assert.Equal(3, editor.ResultsParameterSetsPreview.Rows.Length);
        var query = editor.ResultsQueryOutputs.Single().Keys.Single(); Assert.Equal("SorterModelType", query.Key); query.Selected = true;
        Assert.Equal(new[] { "SorterModelType" }, editor.ToProjectSettings().resultsDataTypes.Single().queryParameterKeys);
        Assert.Equal(8, editor.ResultsRequiredInputs.DefaultCount);
        for (var index = 3; index < 51; ++index) { span.AddValueCommand.Execute(null); span.Values.Last().Text = "Simple"; }
        span.Values.Last().Text = "Unknown";
        Assert.Contains("Unknown cannot generate", editor.ResultsParametersError); Assert.Empty(editor.ResultsParameterSetsPreview.Rows);
        span.Values.Last().Text = "SimpleR";
        Assert.Empty(editor.ResultsParametersError); Assert.True(editor.ResultsParameterSetsPreview.HasRows);
    }

    private static void Configure(WorkspaceTreeNode module)
    {
        module.Editor!.ResultsFunctionName = ResultsFunctionNames.prefixMsrsSorterModels;
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        module.Editor.ResultsParameterDefaults.Single(value => value.Key == "SorterCount").Value.Text = "3";
    }

    [Fact]
    public void Editors_validate_inherited_defaults_and_spans_without_moving_defaults()
    {
        var module = Module(); var editor = module.Editor!;
        Assert.Equal(9, editor.ResultsParameterDefaults.Count); Assert.Empty(editor.ResultsParameterDefaultsError);
        var prefix = editor.ResultsParameterDefaults.Single(value => value.Key == "PrefixLibId");
        prefix.Value.Text = Prefix(9); Assert.Contains("incompatible", editor.ResultsParameterDefaultsError);
        prefix.Value.Text = Prefix(16); Assert.Empty(editor.ResultsParameterDefaultsError);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        Assert.Equal(9, project.Editor!.InheritedOutputDefaults.Count());
        prefix.Value.Text = Prefix(9); Assert.Contains("incompatible", project.Editor.ResultsParameterDefaultsError);
        prefix.RemoveCommand.Execute(null);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        var inputs = run.Editor!;
        Assert.Equal(8, inputs.ResultsRequiredInputs.DefaultCount);
        inputs.AddMissingResultsParametersCommand.Execute(null);
        var span = Assert.Single(inputs.ResultsParameters); Assert.Equal("PrefixLibId", span.Key); Assert.Empty(span.Values);
        span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(8);
        span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(9);
        Assert.Contains("incompatible", inputs.ResultsParametersError);
        Assert.Empty(inputs.ResultsParameterSetsPreview.Rows);
        span.Values.Last().Text = Prefix(16);
        Assert.Empty(inputs.ResultsParametersError); Assert.Equal(2, inputs.ResultsParameterSetsPreview.Rows.Length);
        Assert.Equal(8, inputs.ResultsRequiredInputs.DefaultCount); Assert.Equal(1, inputs.ResultsRequiredInputs.SpanCount);
        Assert.Equal("PrefixLibId", Assert.Single(Assert.Single(inputs.ResultsQueryOutputs).Keys).Key);
        Assert.Contains("random suffix", inputs.ResultsInputGuidance);
    }

    [Theory]
    [InlineData("Simple")]
    [InlineData("SimpleR")]
    [InlineData("SimpleS")]
    public async Task UI_saves_root_work_items_that_execute_and_reuse_models_from_a_copied_project(string family)
    {
        var temp = Directory.CreateTempSubdirectory("Sortopia-prefix-models-");
        WorkspacePageViewModel? page = null;
        try {
            var root = Directory.CreateDirectory(Path.Combine(temp.FullName, "Workspace")).FullName;
            var settings = new WorkspaceFolderSettings(Path.Combine(temp.FullName, "settings.txt")); settings.Save(root);
            page = new WorkspacePageViewModel(() => { }, _ => null, settings);
            page.AddModuleCommand.Execute(null); var module = page.Selected!; Configure(module);
            module.Editor!.ResultsParameterDefaults.Single(value => value.Key == "SorterModelType").Value.Text = family;
            var prefix = module.Editor!.ResultsParameterDefaults.Single(value => value.Key == "PrefixLibId");
            prefix.Value.Text = Prefix(9);
            await page.SaveWorkspaceAsync(); Assert.Contains("incompatible", page.Error);
            prefix.RemoveCommand.Execute(null);
            page.AddProjectCommand.Execute(null); var project = page.Selected!;
            page.AddRunCommand.Execute(null); var run = page.Selected!;
            run.Editor!.AddMissingResultsParametersCommand.Execute(null);
            var span = Assert.Single(run.Editor.ResultsParameters);
            foreach (var width in new[] { 8, 9 }) { span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(width); }
            await page.SaveWorkspaceAsync(); Assert.Contains("incompatible", page.Error);
            span.Values.Last().Text = Prefix(16);
            run.Editor.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.Empty(page.SelectedExecution!.Report!.Setup); Assert.Empty(page.SelectedExecution.Report.Results);
            var source = Path.GetDirectoryName(page.ResultsExecution.Folder)!;
            Assert.False(Directory.Exists(Path.Combine(source, "data", "results")));
            var copy = Directory.CreateDirectory(Path.Combine(temp.FullName, "Cloud project")).FullName;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) {
                var target = Path.Combine(copy, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            var folder = Path.Combine(copy, "runParameters");
            Assert.Equal(2, Directory.GetFiles(folder, "*.msgpack").Length);
            var report = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(report.Status == runExecutionStatus.Completed, report.Error); Assert.Equal(2, report.Results.Length);
            ISortopiaDb store = new SortopiaFileDb(copy, project.Id);
            foreach (var call in report.Results) {
                var output = Assert.Single(call.StoredOutputs); Assert.False(output.Reused);
                var stored = store.Load(output.Reference, TestContext.Current.CancellationToken);
                Assert.True(stored.IsOk, stored.IsError ? stored.ErrorValue : "");
                var models = Assert.IsType<functionData.SorterModelSet>(stored.ResultValue.Value).Item;
                Assert.Equal(3, models.Count);
                var width = call.Parameters.TryFind(ParamKeys.prefixLibIdKey).Value.ChannelCount;
                Assert.All(models.SorterModels, model => {
                    AssertFamily(family, model); Assert.Equal(width, SorterModel.getChannelCount(model));
                    Assert.Equal(SorterDataParse.getCeArrayFromPrefixLib(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)).Value, SorterModel.getPrefix(model));
                });
            }
            var repeated = RunParameterExecution.executeFolder(folder, null!, null!, TestContext.Current.CancellationToken);
            Assert.True(repeated.Status == runExecutionStatus.Completed, repeated.Error);
            Assert.All(repeated.Results.SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(copy, "data", "results", "SorterModelSet"), "*.msgpack").Length);
        }
        finally { page?.Cancel(); temp.Delete(true); }
    }
}
