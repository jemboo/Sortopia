using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ModelGenerationDefinitionTests
{
    public static IEnumerable<object[]> PairedFunctions => SetupFunctionCatalog.functions
        .Where(entry => entry.Value.ModelGeneration is not null).Select(entry => new object[] { entry.Key });

    private static parameterSet Parameters(string name)
    {
        var editor = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Models", family: hierarchyKind.Branch).Editor!;
        editor.SetupFunctionName = name;
        editor.AddRequiredSetupDefaultsCommand.Execute(null);
        editor.SetupParameterDefaults.Single(row => row.Key == "SorterCount").Value.Text = "2";
        editor.SetupParameterDefaults.Single(row => row.Key == "SorterSuccessFraction").Value.Text = "50%";
        if (editor.SetupParameterDefaults.FirstOrDefault(row => row.Key == "AInitial") is { } initial) initial.Value.Text = "1";
        return parameterSet.fromBindings(editor.ToProjectSettings().setupParameterDefaults.Select(ParameterBindingDto.toDomain));
    }

    [Theory]
    [MemberData(nameof(PairedFunctions))]
    public void Matching_setup_and_results_entries_preserve_contracts_and_generate_identical_content(string name)
    {
        var setup = SetupFunctionCatalog.require(name);
        var results = ResultsFunctionCatalog.require(name);
        Assert.Equal(setup.Version, results.Version);
        Assert.Equal(setup.ModelGeneration, results.ModelGeneration);
        Assert.Equal(setup.RequiredParameters, results.RequiredParameters);
        Assert.Equal(setup.DataTypes, results.DataTypes);
        Assert.Equal(setup.OutputDependencies, results.OutputDependencies);
        var parameters = Parameters(name);
        var setupOutputs = CatalogTestHost.Invoke(true, name, parameters).ToArray();
        var resultsOutputs = CatalogTestHost.Invoke(false, name, parameters).ToArray();
        Assert.Equal(setupOutputs.Select(FunctionData.kind), resultsOutputs.Select(FunctionData.kind));
        // Container IDs are new for each invocation; generated model content is reproducible.
        var setupModels = Assert.IsType<functionData.SorterModelSet>(setupOutputs[0]).Item;
        var resultModels = Assert.IsType<functionData.SorterModelSet>(resultsOutputs[0]).Item;
        Assert.Equal(setupModels.RawCeLength, resultModels.RawCeLength);
        Assert.Equal(setupModels.SorterModels, resultModels.SorterModels);
        if (setupOutputs.Length == 2)
        {
            var setupTests = Assert.IsType<functionData.SortableTests>(setupOutputs[1]);
            var resultTests = Assert.IsType<functionData.SortableTests>(resultsOutputs[1]);
            Assert.Equal(setupTests.Item2, resultTests.Item2);
            var first = Assert.IsType<sortableTests.Bools>(setupTests.Item1).Item;
            var second = Assert.IsType<sortableTests.Bools>(resultTests.Item1).Item;
            Assert.Equal(first.ChannelCount, second.ChannelCount);
            Assert.Equal(first.SortableBinaryArrays, second.SortableBinaryArrays);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Combined_generators_write_to_the_correct_database_and_reuse_each_output_independently(bool setup, bool modelsCached, bool testsCached)
    {
        var name = SetupFunctionNames.evaluationStandardMsuf4SimpleS;
        var parameters = Parameters(name);
        var models = sortopiaDataType.NewSorterModelSet("");
        var tests = sortopiaDataType.NewSortableTests("");
        var target = new RecordingDatabase([.. modelsCached ? new[] { models } : [], .. testsCached ? new[] { tests } : []]);
        var host = new runHost(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameters,
            setup ? target : null!, setup ? null! : target, null!, CancellationToken.None, 1, null, null);
        if (setup) SetupFunctionCatalog.require(name).Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>());
        else ResultsFunctionCatalog.require(name).Execute.Invoke(host);
        Assert.Equal(new[] { models, tests }, target.StoredOutputs.Select(output => output.Reference.DataType));
        Assert.Equal(new[] { modelsCached, testsCached }, target.StoredOutputs.Select(output => output.Reused));
        Assert.Equal(new[] { models, tests }.Where(type => !target.Cached.Contains(type)), target.Generated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cached_outputs_still_require_valid_inputs_before_any_save(bool setup)
    {
        var name = SetupFunctionNames.evaluationStandardMsuf4SimpleS;
        var parameters = Parameters(name).WithBinding(ParameterBinding.create(ParamKeys.rngTypeKey, "not-a-generator"));
        var target = new RecordingDatabase([sortopiaDataType.NewSorterModelSet(""), sortopiaDataType.NewSortableTests("")]);
        var host = new runHost(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameters,
            setup ? target : null!, setup ? null! : target, null!, CancellationToken.None, 1, null, null);
        var error = Assert.Throws<ArgumentException>(() =>
        {
            if (setup) SetupFunctionCatalog.require(name).Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>());
            else ResultsFunctionCatalog.require(name).Execute.Invoke(host);
        });
        Assert.Contains("Unknown rngType", error.Message);
        Assert.Empty(target.StoredOutputs);
        Assert.Empty(target.Generated);
    }

    private sealed class RecordingDatabase(sortopiaDataType[] cached) : IRunDatabase
    {
        private readonly List<storedOutput> saved = [];
        public HashSet<sortopiaDataType> Cached { get; } = cached.ToHashSet();
        public List<sortopiaDataType> Generated { get; } = [];
        public FSharpList<storedOutput> StoredOutputs => ListModule.OfSeq(saved);
        public storedDataReference Reference(sortopiaDataType type) => throw new InvalidOperationException("Generators must not read inputs.");
        public functionData Load(sortopiaDataType type) => throw new InvalidOperationException("Generators must not read inputs.");
        public void SaveIfMissing(sortopiaDataType type, FSharpList<storedDataReference> dependencies, FSharpFunc<Unit, functionData> factory)
        {
            Assert.Empty(dependencies);
            if (!Cached.Contains(type))
            {
                Assert.Equal(SortopiaDataType.kindName(type), FunctionData.kind(factory.Invoke(null!)));
                Generated.Add(type);
            }
            saved.Add(new(new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), false, type), Cached.Contains(type)));
        }
    }
}
