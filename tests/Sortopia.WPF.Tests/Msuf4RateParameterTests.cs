using System.Globalization;
using Microsoft.FSharp.Collections;
using Sortopia.Core;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Simple.Uf4;
using Sortopia.Permutations;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class Msuf4RateParameterTests
{
    public static IEnumerable<object[]> Cases =>
        from phase in new[] { "setup", "results", "prefix" }
        from family in new[] { "Simple", "SimpleR", "SimpleS" }
        from width in new[] { 4, 8, 16 }
        where phase != "prefix" || width >= 8
        select new object[] { phase, family, width };

    private static (WorkspaceConfigurationViewModel Editor, parameterSet Parameters) Configure(string phase, string family, int width,
        double ortho, double para, double self)
    {
        bool setup = phase == "setup";
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: setup ? hierarchyKind.Branch : hierarchyKind.Root).Editor!;
        var name = $"sorter-model-set.{(phase == "prefix" ? "prefix" : "uniform")}.msuf4{ModelFamilyResultsTests.Suffix(family)}";
        if (setup) editor.SetupFunctionName = name; else editor.ResultsFunctionName = name;
        (setup ? editor.AddRequiredSetupDefaultsCommand : editor.AddRequiredResultsDefaultsCommand).Execute(null);
        var rows = setup ? editor.SetupParameterDefaults : editor.ResultsParameterDefaults;
        foreach (var key in new[] { "OrthoGenRate", "ParaGenRate", "SelfSymGenRate" })
        {
            var value = rows.Single(row => row.Key == key).Value;
            Assert.Equal("1", value.Text); Assert.Equal(parameterValueType.Floating, value.ValueType);
            Assert.True(value.IsNumeric); Assert.Contains("normalized", value.Hint);
        }
        Assert.DoesNotContain(rows, row => row.Key is "Seed4Weights" or "UnfoldingWeights");
        rows.Single(row => row.Key == "SorterCount").Value.Text = "1";
        rows.Single(row => row.Key == "SorterSuccessFraction").Value.Text = "50%";
        foreach (var (key, value) in new[] { ("OrthoGenRate", ortho), ("ParaGenRate", para), ("SelfSymGenRate", self) })
            rows.Single(row => row.Key == key).Value.Text = value.ToString("R", CultureInfo.InvariantCulture);
        rows.Single(row => row.Key == (phase == "prefix" ? "PrefixLibId" : "ChannelCount")).Value.Text = phase == "prefix"
            ? PrefixLibId.toString(prefixLibId.create(width, 1, prefixLibVariant.Prefix1)) : width.ToString();
        Assert.Empty(setup ? editor.SetupParameterDefaultsError : editor.ResultsParameterDefaultsError);
        var settings = editor.ToProjectSettings();
        var parameters = parameterSet.fromBindings((setup ? settings.setupParameterDefaults : settings.resultsParameterDefaults).Select(ParameterBindingDto.toDomain));
        return (editor, parameters);
    }

    private static msuf4 Generate(string phase, WorkspaceConfigurationViewModel editor, parameterSet parameters)
    {
        var output = Assert.IsType<sortopiaStorageData.SorterModelSet>(Assert.Single(CatalogTestHost.Invoke(phase == "setup",
            phase == "setup" ? editor.SetupFunctionName : editor.ResultsFunctionName, parameters))).Item;
        var simple = Assert.Single(output.SorterModels) switch {
            sorterModel.Simple value => value.Item, sorterModel.SimpleR value => value.Item.Model,
            sorterModel.SimpleS value => value.Item.Model, _ => throw new InvalidOperationException() };
        return Assert.IsType<simpleSorterModel.Msuf4>(simple).Item;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void All_families_and_phases_normalize_once_and_derive_every_level_from_width(string phase, string family, int width)
    {
        var (editor, parameters) = Configure(phase, family, width, 2, 1, 1);
        var generated = Generate(phase, editor, parameters);
        var rates = opsGenRates.create(0.5, 0.25, 0.25);
        int levels = width == 4 ? 0 : width == 8 ? 1 : 2;
        var prefix = phase == "prefix" ? SorterDataParse.getCeArrayFromPrefixLib(parameters.TryFind(ParamKeys.prefixLibIdKey).Value).Value : [];
        var generator = Msuf4RandGen.create(rngFactory.SmxFactory, width, prefix,
            SimpleSorterStageLengths.stageLength50(width, phase == "prefix" ? 1 : 0, simpleSorterModelType.Msuf4),
            rates, ListModule.OfSeq(Enumerable.Repeat(rates, levels)));
        var baseGenerator = simpleSorterModelGen.NewSmmMsuf4RandGen(generator);
        var expectedId = family switch {
            "SimpleR" => simpleSorterModelRGen.create(baseGenerator, parameters.TryFind(ParamKeys.aMaxKey).Value,
                parameters.TryFind(ParamKeys.aInitialKey).Value).MakeSorterModelId(0),
            "SimpleS" => simpleSorterModelSGen.create(baseGenerator, parameters.TryFind(ParamKeys.activationProbabilityKey).Value).MakeSorterModelId(0),
            _ => generator.MakeSorterModelId(0)
        };
        var expected = generator.MakeSorterModelFromId(expectedId);
        Assert.Equal(expected.Id, generated.Id);
        Assert.All(generated.TwoOrbitUnfolder4s, stage => Assert.Equal(levels, stage.TwoOrbitUnfolderSteps.Length));
        // Scaling all relative rates preserves the generated distribution and model identity.
        var scaled = parameters.WithBinding(ParameterBinding.create(ParamKeys.orthoGenRateKey, 20.0))
            .WithBinding(ParameterBinding.create(ParamKeys.paraGenRateKey, 10.0)).WithBinding(ParameterBinding.create(ParamKeys.selfSymGenRateKey, 10.0));
        Assert.Equal(generated.Id, Generate(phase, editor, scaled).Id);
        foreach (var obsolete in new[] { ParameterBinding.create(ParamKeys.seed4WeightsKey, new opsWeights(1, 1, 1)),
            ParameterBinding.create(ParamKeys.unfoldingWeightsKey, new unfoldingWeights([])),
            ParameterBinding.create(ParamKeys.orthoRateKey, 1.0), ParameterBinding.create(ParamKeys.paraRateKey, 1.0),
            ParameterBinding.create(ParamKeys.selfSymRateKey, 1.0) })
            Assert.Contains("not used", Assert.Throws<ArgumentException>(() => Generate(phase, editor, parameters.WithBinding(obsolete))).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Width_spans_preview_and_generate_with_automatically_sized_unfolding_levels(bool setup)
    {
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch);
        module.Editor!.SetupFunctionName = SetupFunctionNames.uniformMsuf4SimpleSorterModels;
        module.Editor.ResultsFunctionName = ResultsFunctionNames.uniformMsuf4SimpleSorterModels;
        module.Editor.AddRequiredSetupDefaultsCommand.Execute(null);
        module.Editor.AddRequiredResultsDefaultsCommand.Execute(null);
        foreach (var defaults in new[] { module.Editor.SetupParameterDefaults, module.Editor.ResultsParameterDefaults })
        {
            defaults.Single(row => row.Key == "SorterCount").Value.Text = "1";
            defaults.Single(row => row.Key == "SorterSuccessFraction").Value.Text = "50%";
        }
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Project", family: hierarchyKind.Branch);
        module.Children.Add(project);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Run", family: hierarchyKind.Branch);
        project.Children.Add(run);
        WorkspaceParameterSetsPreviewTests.AddSpan(run, "ChannelCount", setup, "4", "8", "16", "32", "64");
        Assert.Empty(setup ? run.Editor!.SetupParametersError : run.Editor!.ResultsParametersError);
        var preview = setup ? run.Editor.SetupParameterSetsPreview : run.Editor.ResultsParameterSetsPreview;
        Assert.Equal(5, preview.Rows.Length);
        foreach (var (row, levels) in preview.Rows.Select((row, index) => (row, index)))
        {
            var generated = Generate(setup ? "setup" : "results", run.Editor, row.ParameterSet);
            Assert.Equal(4 << levels, generated.ChannelCount);
            Assert.All(generated.TwoOrbitUnfolder4s, stage => Assert.Equal(levels, stage.TwoOrbitUnfolderSteps.Length));
        }
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    public void A_single_nonzero_rate_selects_the_same_mode_for_seed_and_every_unfolding_step(double ortho, double para, double self)
    {
        var (editor, parameters) = Configure("results", "Simple", 16, ortho, para, self);
        var generated = Generate("results", editor, parameters);
        foreach (var stage in generated.TwoOrbitUnfolder4s)
            foreach (var pair in stage.TwoOrbitUnfolderSteps.SelectMany(step => step.TwoOrbitPairTypes).Prepend(stage.SeedTwoOrbitPairType))
            {
                Assert.Equal(ortho > 0, pair.IsOrtho); Assert.Equal(para > 0, pair.IsPara); Assert.Equal(self > 0, pair.IsSelfRefl);
            }
    }

    [Fact]
    public void Validation_checks_partial_defaults_and_every_rate_span_combination_without_expanding_them()
    {
        var definition = ResultsFunctionCatalog.require(ResultsFunctionNames.uniformMsuf4SimpleSorterModels);
        Assert.Equal("7", definition.Version);
        foreach (var key in new[] { ParamKeys.orthoGenRateKey, ParamKeys.paraGenRateKey, ParamKeys.selfSymGenRateKey })
            Assert.Contains(key.Name, definition.InputError.Invoke([ParameterBinding.create(key, -1.0)]).Invoke([]));
        var zeros = new[] { ParameterBinding.create(ParamKeys.orthoGenRateKey, 0.0), ParameterBinding.create(ParamKeys.paraGenRateKey, 0.0), ParameterBinding.create(ParamKeys.selfSymGenRateKey, 0.0) };
        Assert.Empty(definition.InputError.Invoke(ListModule.OfSeq(zeros.Take(2))).Invoke([]));
        Assert.Contains("positive", definition.InputError.Invoke(ListModule.OfArray(zeros)).Invoke([]));
        var spans = ListModule.OfSeq(new[] { ParamKeys.orthoGenRateKey, ParamKeys.paraGenRateKey, ParamKeys.selfSymGenRateKey }
            .Select(key => ParameterSpan.create(key, ListModule.OfSeq(Enumerable.Repeat(1.0, 50).Append(0.0)))));
        Assert.Contains("every parameter combination", definition.InputError.Invoke([]).Invoke(spans));
        var (editor, parameters) = Configure("results", "Simple", 4, double.MaxValue, double.MaxValue, double.MaxValue);
        Assert.NotEmpty(Generate("results", editor, parameters).TwoOrbitUnfolder4s);
        foreach (var text in new[] { "NaN", "Infinity", "-1" })
        {
            editor.ResultsParameterDefaults.Single(row => row.Key == "OrthoGenRate").Value.Text = text;
            Assert.NotEmpty(editor.ResultsParameterDefaultsError);
        }
        Assert.Equal("SelfSymGenRate", ParamKeys.selfSymGenRateKey.Name);
    }

    [Theory]
    [InlineData("V1")]
    [InlineData("V2")]
    public void Generation_and_mutation_choose_modes_independently(string variant)
    {
        var (editor, parameters) = Configure("prefix", "Simple", 16, 1, 0, 0);
        var parent = Generate("prefix", editor, parameters);
        var mutation = SimpleSorterModelMutator.getMsuf4ModelMutator(16, rngFactory.SmxFactory, true,
            1, 1, 0, 1, 0, MutatorVariant.fromString(variant));
        var child = Assert.IsType<simpleSorterModel.Msuf4>(SimpleSorterModelMutator.makeMutantSorterModelFromIndexAndMod(
            mutation, simpleSorterModel.NewMsuf4(parent), 0, 0)).Item;
        Assert.Equal(parent.Prefix, child.Prefix);
        Assert.Equal(parent.TwoOrbitUnfolder4s.Length, child.TwoOrbitUnfolder4s.Length);
        foreach (var stage in parent.TwoOrbitUnfolder4s)
            Assert.All(stage.TwoOrbitUnfolderSteps.SelectMany(step => step.TwoOrbitPairTypes).Prepend(stage.SeedTwoOrbitPairType),
                mode => Assert.True(mode.IsOrtho));
        foreach (var stage in child.TwoOrbitUnfolder4s)
            Assert.All(stage.TwoOrbitUnfolderSteps.SelectMany(step => step.TwoOrbitPairTypes).Prepend(stage.SeedTwoOrbitPairType),
                mode => Assert.True(mode.IsPara));
    }

    [Fact]
    public void Generation_and_sgd_publish_separate_rate_inputs_and_query_keys()
    {
        var generation = ResultsFunctionCatalog.require(ResultsFunctionNames.uniformMsuf4SimpleSorterModels);
        var mutation = SgdFunctions.standardMsuf4Simple;
        foreach (var (gen, mut) in new[] { (ParamKeys.orthoGenRateKey, ParamKeys.orthoRateKey),
            (ParamKeys.paraGenRateKey, ParamKeys.paraRateKey), (ParamKeys.selfSymGenRateKey, ParamKeys.selfSymRateKey) })
        {
            Assert.Equal(parameterValueType.Floating, ParamKeys.valueType(gen.Name));
            Assert.Equal(parameterValueType.Floating, ParamKeys.valueType(mut.Name));
            Assert.Contains(generation.RequiredParameters, key => key.Name == gen.Name);
            Assert.DoesNotContain(generation.RequiredParameters, key => key.Name == mut.Name);
            Assert.Contains(mutation.RequiredParameters, key => key.Name == mut.Name);
            Assert.DoesNotContain(mutation.RequiredParameters, key => key.Name == gen.Name);
            Assert.Contains(gen.Name, generation.DataTypes[sortopiaDataType.NewSorterModelSet("")]);
            Assert.DoesNotContain(mut.Name, generation.DataTypes[sortopiaDataType.NewSorterModelSet("")]);
            Assert.Contains(mut.Name, mutation.DataTypes[sortopiaDataType.NewSorterPoolSet("")]);
            Assert.DoesNotContain(gen.Name, mutation.DataTypes[sortopiaDataType.NewSorterPoolSet("")]);
        }
    }
}
