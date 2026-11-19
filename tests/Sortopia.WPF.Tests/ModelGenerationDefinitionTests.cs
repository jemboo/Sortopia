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
        .Where(entry => !entry.Value.ParametersFromSources && entry.Value.SortopiaDataTypes.Any(type => type.IsSorterModelSet))
        .Select(entry => new object[] { entry.Key });

    [Theory]
    [InlineData("msce", "2", "compare-exchange (MSCE)")]
    [InlineData("mssi", "2", "self-inverse (MSSI)")]
    [InlineData("msrs", "2", "reflection-symmetric (MSRS)")]
    [InlineData("msuf4", "6", "4-channel seed unfolding (MSUF4)")]
    [InlineData("msuf6", "2", "6-channel seed unfolding (MSUF6)")]
    public void Algorithm_registrations_preserve_published_versions_and_descriptions(string algorithm, string version, string description)
    {
        void Check<T>(functionDefinition<T> definition)
        {
            Assert.Equal(version, definition.Version);
            Assert.Contains(description, definition.Description);
        }

        foreach (var constructor in new[] { "simple", "simple-r", "simple-s" })
        foreach (var group in new[] { "sorter-model-set.uniform", "sorter-model-set.prefix", "sorter-eval-inputs.standard" })
        {
            // Names and versions participate in saved identities across every catalog variant.
            var name = $"{group}.{algorithm}.{constructor}";
            Check(ResultsFunctionCatalog.require(name));
            if (group != "sorter-model-set.prefix") Check(SetupFunctionCatalog.require(name));
        }
    }

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
        Assert.Equal(setup.RequiredParameters, results.RequiredParameters);
        Assert.Equal(setup.DataTypes, results.DataTypes);
        Assert.Equal(setup.OutputDependencies, results.OutputDependencies);
        var parameters = Parameters(name);
        Assert.Equal(setup.ParameterSupport.Preview.Invoke(parameters), results.ParameterSupport.Preview.Invoke(parameters));
        var bindings = ListModule.OfSeq(parameters.ParamMap.Values);
        Assert.Equal(setup.ParameterSupport.Guidance.Invoke(bindings).Invoke(ListModule.Empty<parameterSpan>()),
            results.ParameterSupport.Guidance.Invoke(bindings).Invoke(ListModule.Empty<parameterSpan>()));
        var setupOutputs = CatalogTestHost.Invoke(true, name, parameters).ToArray();
        var resultsOutputs = CatalogTestHost.Invoke(false, name, parameters).ToArray();
        Assert.Equal(setupOutputs.Select(SortopiaStorageData.kind), resultsOutputs.Select(SortopiaStorageData.kind));
        // Container IDs are new for each invocation; generated model content is reproducible.
        var setupModels = Assert.IsType<sortopiaStorageData.SorterModelSet>(setupOutputs[0]).Item;
        var resultModels = Assert.IsType<sortopiaStorageData.SorterModelSet>(resultsOutputs[0]).Item;
        Assert.Equal(setupModels.RawCeLength, resultModels.RawCeLength);
        Assert.Equal(setupModels.SorterModels, resultModels.SorterModels);
        if (setupOutputs.Length == 2)
        {
            var setupTests = Assert.IsType<sortopiaStorageData.SortableTests>(setupOutputs[1]);
            var resultTests = Assert.IsType<sortopiaStorageData.SortableTests>(resultsOutputs[1]);
            var first = Assert.IsType<sortableTests.Bools>(setupTests.Item).Item;
            var second = Assert.IsType<sortableTests.Bools>(resultTests.Item).Item;
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
            target, target.Outputs, CancellationToken.None, 1, null, null);
        if (setup) SetupFunctionCatalog.require(name).Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>());
        else ResultsFunctionCatalog.require(name).Execute.Invoke(host);
        Assert.Equal(new[] { models, tests }, target.Outputs.Stored.Select(output => output.Reference.DataType));
        Assert.Equal(new[] { modelsCached, testsCached }, target.Outputs.Stored.Select(output => output.Reused));
        Assert.Equal(new[] { models, tests }.Where(type => !target.Cached.Contains(type)), target.Generated);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Cached_outputs_still_require_valid_inputs_before_any_save(bool setup, bool invalidConstructor)
    {
        var name = SetupFunctionNames.evaluationStandardMsuf4SimpleS;
        var parameters = Parameters(name).WithBinding(invalidConstructor
            ? ParameterBinding.create(ParamKeys.activationProbabilityKey, 1.1)
            : ParameterBinding.create(ParamKeys.rngTypeKey, "not-a-generator"));
        var target = new RecordingDatabase([sortopiaDataType.NewSorterModelSet(""), sortopiaDataType.NewSortableTests("")]);
        var host = new runHost(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameters,
            target, target.Outputs, CancellationToken.None, 1, null, null);
        var error = Assert.Throws<ArgumentException>(() =>
        {
            if (setup) SetupFunctionCatalog.require(name).Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>());
            else ResultsFunctionCatalog.require(name).Execute.Invoke(host);
        });
        Assert.Contains(invalidConstructor ? "between 0 and 1" : "Unknown rngType", error.Message);
        Assert.Empty(target.Outputs.Stored);
        Assert.Empty(target.Generated);
    }

    private sealed class RecordingDatabase(sortopiaDataType[] cached) : IRunDatabase
    {
        private readonly List<storedOutput> saved = [];
        private readonly Dictionary<runDataOutput, storedDataReference> planned = new[] { sortopiaDataType.NewSorterModelSet(""), sortopiaDataType.NewSortableTests("") }
            .Select(type => new runDataOutput(type, parameterSet.fromBindings([]), []))
            .ToDictionary(output => output, output => new storedDataReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Results, output.DataType));
        public RunOutputs Outputs => new(ListModule.OfSeq(planned.Keys), FuncConvert.FromFunc<Unit, FSharpList<storedOutput>>(_ => ListModule.OfSeq(saved)));
        public HashSet<sortopiaDataType> Cached { get; } = cached.ToHashSet();
        public List<sortopiaDataType> Generated { get; } = [];
        public storedDataReference Find(runDataOutput output) => planned[output];
        public storedDataReference Find(sortopiaDataType dataType) => planned.Single(entry => Equals(entry.Key.DataType, dataType)).Value;
        public storedDataReference Find(Guid projectId, sortopiaDataType dataType, parameterSet parameters) => throw new InvalidOperationException("Generators must not read inputs.");
        public sortopiaStorageData Load(storedDataReference reference) => throw new InvalidOperationException("Generators must not read inputs.");
        public void Save(runDataOutput output, sortopiaStorageData value) => Save(output, FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => value));
        public void Save(runDataOutput output, FSharpFunc<Unit, sortopiaStorageData> factory)
        {
            var reference = Find(output);
            if (saved.Any(value => Equals(value.Reference, reference))) return;
            Assert.Empty(output.Dependencies);
            var type = output.DataType;
            if (!Cached.Contains(type))
            {
                Assert.Equal(SortopiaDataType.kindName(type), SortopiaStorageData.kind(factory.Invoke(null!)));
                Generated.Add(type);
            }
            saved.Add(new(reference, Cached.Contains(type)));
        }
    }
}
