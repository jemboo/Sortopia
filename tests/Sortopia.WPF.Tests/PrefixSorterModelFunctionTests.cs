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
    private static string Function(string model, string family = "Simple") => $"sorter-model-set.prefix.{model.ToLowerInvariant()}{ModelFamilyResultsTests.Suffix(family)}";
    private static parameterBinding[] Defaults(string model = "Mssi", int width = 8, int repl = 0) => [
        ParameterBinding.create(ParamKeys.prefixLibIdKey, prefixLibId.create(width, 1, prefixLibVariant.Prefix1)),
        Bind("RngType", "SplitMix64", parameterValueType.Text),
        Bind("SorterSuccessFraction", "50%", parameterValueType.SorterSuccessFraction), Bind("SorterCount", "3"), Bind("Repl", repl.ToString()), .. ModelDefaults(model, width)];
    private static parameterBinding[] ModelDefaults(string model, int width) => model switch {
        "Msce" => [Bind("ExcludeSelfCe", "true", parameterValueType.Boolean)],
        "Msrs" => [Bind("StageWeights", "1,1,1", parameterValueType.OpsWeights)],
        "Msuf4" => [Bind("OrthoGenRate", "1", parameterValueType.Floating), Bind("ParaGenRate", "1", parameterValueType.Floating), Bind("SelfSymGenRate", "1", parameterValueType.Floating)],
        "Msuf6" => [Bind("Seed6Weights", "1,1,1,1,1,1,1", parameterValueType.Seed6Weights),
            Bind("UnfoldingWeights", string.Join(";", Enumerable.Repeat("1,1,1", Math.Max(0, (int)Math.Ceiling(Math.Log2(width / 6.0))))), parameterValueType.UnfoldingWeights)],
        _ => []
    };
    private static parameterSet Parameters(string model = "Mssi", int width = 8, int repl = 0) => parameterSet.fromBindings(ListModule.OfArray(Defaults(model, width, repl)));
    private static sorterModelSet Generate(parameterSet parameters, string model = "Mssi", string family = "Simple") => Assert.IsType<sortopiaStorageData.SorterModelSet>(
        Assert.Single(CatalogTestHost.Invoke(false, Function(model, family), parameters))).Item;

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
        parameterSet Inputs(int repl = 0) => ModelFamilyResultsTests.WithFamilyParameters(Parameters(type, width, repl), family);
        var set = Generate(Inputs(), type, family);
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
            var stages = SimpleSorterStageLengths.stageLength50(width, 1, SimpleSorterModelType.fromString(type));
            Assert.Equal(type == "Msce" ? width * stages / 2 : stages, SimpleSorterModel.getLength(simple));
            Assert.Equal(width, SorterModel.getChannelCount(model));
            Assert.Equal(expectedPrefix, SorterModel.getPrefix(model));
            var sorter = SorterModel.makeSorter(model, null!);
            Assert.Equal(expectedPrefix, sorter.Ces.Take(expectedPrefix.Length));
            Assert.True(sorter.Ces.Length >= expectedPrefix.Length);
            if (family == "Simple") Assert.True(sorter.Ces.Length > expectedPrefix.Length);
            if (model is sorterModel.SimpleR r) Assert.All(r.Item.Activators, value => Assert.InRange(value, -1, 1));
        });
        var repeated = Generate(Inputs(), type, family);
        Assert.Equal(set.SorterModels.Select(SorterModel.getId), repeated.SorterModels.Select(SorterModel.getId));
        foreach (var pair in set.SorterModels.Zip(repeated.SorterModels))
            Assert.Equal(SorterModel.makeSorter(pair.First, null!).Ces, SorterModel.makeSorter(pair.Second, null!).Ces);
        Assert.Empty(set.SorterModels.Select(SorterModel.getId).Intersect(Generate(Inputs(1), type, family).SorterModels.Select(SorterModel.getId)));
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
        var config = new resultsConfig("Results", Function(type), ResultsFunctionCatalog.require(Function(type)).Version,
            [new resultsDataTypeConfig(sortopiaDataType.NewSorterModelSet(""), ["PrefixLibId"])], []);
        Assert.Contains(reason, Assert.Throws<ArgumentException>(() => FileResultsHost.plan(Guid.NewGuid(), config, values, [])).Message);
    }

    [Theory]
    [InlineData("SorterSuccessFraction", "75%", "Choose")]
    [InlineData("StageLength", "2", "not used")]
    [InlineData("SorterCount", "0", "positive")]
    [InlineData("Repl", "-1", "0 or greater")]
    [InlineData("Repl", "2147483647", "indices")]
    [InlineData("RngType", "missing", "rngType")]
    [InlineData("SorterModelType", "Unknown", "not used")]
    [InlineData("SorterModelType", "missing", "not used")]
    [InlineData("ActivationProbability", "-0.1", "between 0 and 1")]
    [InlineData("ActivationProbability", "1.1", "between 0 and 1")]
    [InlineData("PrefixLibId", "PrefixLibId(channelCount=16, stageLength=2, Variant=PrefixA)", "not available")]
    public void Invalid_values_fail_before_generation(string key, string text, string reason)
    {
        var family = key == "ActivationProbability" ? "SimpleS" : "Simple";
        var values = ModelFamilyResultsTests.WithFamilyParameters(Parameters(), family);
        Assert.Contains(reason, Assert.Throws<ArgumentException>(() => Generate(values.WithBinding(Bind(key, text, ParamKeys.valueType(key))), family: family)).Message);
    }

    [Fact]
    public void Domain_validation_checks_partial_defaults_and_all_span_values_beyond_the_preview_limit()
    {
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.prefixMsuf4SimpleSorterModels);
        Assert.Empty(definition.SourceDataTypes);
        Assert.Equal(new[] { "OrthoGenRate", "ParaGenRate", "PrefixLibId", "Repl", "RngType", "SelfSymGenRate", "SorterCount", "SorterSuccessFraction" }, definition.RequiredParameters.Select(key => key.Name).Order());
        Assert.True(Assert.Single(definition.SortopiaDataTypes).IsSorterModelSet);
        var defaults = ListModule.OfSeq(Defaults("Msuf4").Where(value => value.KeyName != "PrefixLibId"));
        FunctionValidation.validateDefaults(definition, defaults);
        var spans = ListModule.Singleton(ParameterSpan.fromTexts("PrefixLibId", parameterValueType.PrefixLibId,
            ListModule.OfSeq(Enumerable.Repeat(Prefix(8), 50).Append(Prefix(18)))));
        Assert.Contains("incompatible", Assert.Throws<ArgumentException>(() => FunctionValidation.validateInputs(definition, ["PrefixLibId"], defaults, spans)).Message);
        var weights = ListModule.Singleton(ParameterSpan.fromTexts("UnfoldingWeights", parameterValueType.UnfoldingWeights, ["1,1,1", "1,1,1;1,1,1"]));
        Assert.Contains("needs 1 unfolding", ResultsFunctionCatalog.require(ResultsFunctionNames.prefixMsuf6SimpleSorterModels).InputError
            .Invoke([Defaults("Msuf6", 12).Single(binding => binding.KeyName == "PrefixLibId")]).Invoke(weights));
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
        var inputs = family == "SimpleR" ? Parameters().WithBinding(ParameterBinding.create(ParamKeys.aMaxKey, 0)).WithBinding(ParameterBinding.create(ParamKeys.aInitialKey, 0))
            : Parameters().WithBinding(ParameterBinding.create(ParamKeys.activationProbabilityKey, probability));
        foreach (var model in Generate(inputs, family: family).SorterModels) {
            var prefix = SorterModel.getPrefix(model);
            var full = SorterModel.makeSorter(model, null!).Ces;
            if (probability == 0) Assert.Equal(prefix, full);
            else Assert.True(full.Length > prefix.Length);
            if (model is sorterModel.SimpleR r) { Assert.Equal(0, r.Item.AMax); Assert.All(r.Item.Activators, value => Assert.Equal(0, value)); }
            if (model is sorterModel.SimpleS s) Assert.All(s.Item.Activators, value => Assert.Equal(probability == 1, value));
        }
    }

    [Fact]
    public void Family_is_fixed_by_the_results_function_and_is_not_an_editable_parameter()
    {
        var module = Module(); var editor = module.Editor!;
        Assert.DoesNotContain(editor.ResultsParameterDefaults, row => row.Key is "SorterModelType" or "AMax" or "AInitial" or "ActivationProbability");
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        run.Editor!.RefreshInherited();
        Assert.Empty(run.Editor.ResultsParameters);
        Assert.DoesNotContain(project.Editor!.ResultsQueryOutputs.SelectMany(output => output.Keys), key => key.Key == "SorterModelType");
    }
    private static void Configure(WorkspaceTreeNode module, string family = "Simple")
    {
        module.Editor!.ResultsFunctionName = Function("Msrs", family);
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        module.Editor.ResultsParameterDefaults.Single(value => value.Key == "SorterCount").Value.Text = "3";
    }

    [Fact]
    public void Editors_validate_inherited_defaults_and_spans_without_moving_defaults()
    {
        var module = Module(); var editor = module.Editor!;
        Assert.Equal(6, editor.ResultsParameterDefaults.Count); Assert.Empty(editor.ResultsParameterDefaultsError);
        var prefix = editor.ResultsParameterDefaults.Single(value => value.Key == "PrefixLibId");
        prefix.Value.Text = Prefix(9); Assert.Contains("incompatible", editor.ResultsParameterDefaultsError);
        prefix.Value.Text = Prefix(16); Assert.Empty(editor.ResultsParameterDefaultsError);
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project"); module.Children.Add(project);
        Assert.Equal(6, project.Editor!.InheritedOutputDefaults.Count());
        prefix.Value.Text = Prefix(9); Assert.Contains("incompatible", project.Editor.ResultsParameterDefaultsError);
        prefix.RemoveCommand.Execute(null);
        project.Editor!.ResultsQueryOutputs.Single().Keys.Single(row => row.Key == "PrefixLibId").Selected = true;
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run"); project.Children.Add(run);
        var inputs = run.Editor!;
        Assert.Equal(5, inputs.ResultsRequiredInputs.DefaultCount);
        inputs.RefreshInherited();
        var span = Assert.Single(inputs.ResultsParameters); Assert.Equal("PrefixLibId", span.Key); Assert.Empty(span.Values);
        span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(8);
        span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(9);
        Assert.Contains("incompatible", inputs.ResultsParametersError);
        Assert.Empty(inputs.ResultsParameterSetsPreview.Rows);
        span.Values.Last().Text = Prefix(16);
        Assert.Empty(inputs.ResultsParametersError); Assert.Equal(2, inputs.ResultsParameterSetsPreview.Rows.Length);
        Assert.Equal(5, inputs.ResultsRequiredInputs.DefaultCount); Assert.Equal(1, inputs.ResultsRequiredInputs.SpanCount);
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
            page.AddModuleCommand.Execute(null); var module = page.Selected!; Configure(module, family);
            if (family == "SimpleR") module.Editor!.ResultsParameterDefaults.Single(value => value.Key == "AInitial").Value.Text = "-1";
            var prefix = module.Editor!.ResultsParameterDefaults.Single(value => value.Key == "PrefixLibId");
            prefix.Value.Text = Prefix(9);
            await page.SaveWorkspaceAsync(); Assert.Contains("incompatible", page.Error);
            module = page.Roots[0].Children[0]; page.Selected = module;
            prefix = module.Editor!.ResultsParameterDefaults.Single(value => value.Key == "PrefixLibId");
            prefix.RemoveCommand.Execute(null);
            page.AddProjectCommand.Execute(null); var project = page.Selected!;
            project.Editor!.ResultsQueryOutputs.Single().Keys.Single(row => row.Key == "PrefixLibId").Selected = true;
            page.AddRunCommand.Execute(null); var run = page.Selected!;
            run.Editor!.RefreshInherited();
            var span = Assert.Single(run.Editor.ResultsParameters);
            foreach (var width in new[] { 8, 9 }) { span.AddValueCommand.Execute(null); span.Values.Last().Text = Prefix(width); }
            await page.SaveWorkspaceAsync(); Assert.Contains("incompatible", page.Error);
            run = page.Roots[0].Children[0].Children[0].Children[0];
            span = Assert.Single(run.Editor!.ResultsParameters);
            span.Values.Last().Text = Prefix(16);
            run.Parent!.Editor!.ResultsQueryOutputs.Single().Keys.Single().Selected = true;
            await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
            page.Selected = run;
            await page.GenerateRunParametersAsync(); Assert.Empty(page.Error);
            Assert.Empty(page.SelectedExecution!.Report!.Setup); Assert.Empty(page.SelectedExecution.Report.Results);
            var source = Path.GetDirectoryName(page.RunParametersFolder)!;
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
                var models = Assert.IsType<sortopiaStorageData.SorterModelSet>(stored.ResultValue.Value).Item;
                Assert.Equal(3, models.Count);
                var width = call.Parameters.TryFind(ParamKeys.prefixLibIdKey).Value.ChannelCount;
                Assert.All(models.SorterModels, model => {
                    AssertFamily(family, model); Assert.Equal(width, SorterModel.getChannelCount(model));
                    if (model is sorterModel.SimpleR r) Assert.All(r.Item.Activators, value => Assert.Equal(-1, value));
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
