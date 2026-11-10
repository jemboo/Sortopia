using System.Globalization;
using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FileDataStoreTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-data-store-");
    private readonly Guid projectId = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static FSharpList<T> List<T>(params T[] values) => ListModule.OfArray(values);
    private static parameterSet Parameters(params parameterBinding[] values) => parameterSet.fromBindings(values);
    private SortopiaFileDb Open() => new(temporary.FullName, projectId);
    private dataKey Key(parameterSet? parameters = null, string[]? keys = null, sortopiaDataType? type = null,
        bool setup = true, string version = "1", storedDataReference[]? inputs = null) =>
        DataKey.create(projectId, "Data", setup, "function", version, type ?? sortopiaDataType.NewSortableTests("standard"),
            List(keys ?? ["ChannelCount"]), parameters ?? Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4)), List(inputs ?? []));
    private static FSharpFunc<Unit, FSharpResult<functionData, string>> Factory(Func<functionData> make) =>
        FuncConvert.FromFunc<Unit, FSharpResult<functionData, string>>(_ => FSharpResult<functionData, string>.NewOk(make()));
    private static functionData Tests(int channels = 4) => CatalogTestHost.Invoke(true, SetupFunctionNames.standardTests,
        Parameters(ParameterBinding.create(ParamKeys.channelCountKey, channels), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"))).Head;
    private static functionData Models(int stages = 3) => CatalogTestHost.Invoke(true, SetupFunctionNames.uniformSorterModels,
        Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.stageLengthKey, stages),
            ParameterBinding.create(ParamKeys.simpleSorterModelTypeKey, "Mssi"), ParameterBinding.create(ParamKeys.rngTypeKey, "SplitMix64"),
            ParameterBinding.create(ParamKeys.excludeSelfCeKey, true), ParameterBinding.create(ParamKeys.sorterCountKey, 2), ParameterBinding.create(ParamKeys.replKey, 0))).Head;
    private static T Ok<T>(FSharpResult<T, string> result)
    { Assert.True(result.IsOk, result.IsError ? result.ErrorValue : ""); return result.ResultValue; }

    [Fact]
    public void Identity_uses_only_the_typed_projection_and_stable_scope()
    {
        var first = Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 16), ParameterBinding.create(ParamKeys.stageLengthKey, 1));
        var second = Parameters(ParameterBinding.create(ParamKeys.stageLengthKey, 4), ParameterBinding.fromText("ChannelCount", parameterValueType.Integer, "+0016"));
        Assert.Equal(Key(first).Id, Key(second).Id);
        Assert.NotEqual(Key(first).Id, Key(first, type: sortopiaDataType.NewSortableTests("different label")).Id);
        Assert.NotEqual(Key(first).Id, Key(first, version: "2").Id);
        Assert.NotEqual(Key(first).Id, Key(first, setup: false).Id);
        Assert.Equal(Key(first, ["ChannelCount", "StageLength"]).Id, Key(first, ["StageLength", "ChannelCount"]).Id);
        var integer = Parameters(ParameterBinding.fromText("Custom", parameterValueType.Integer, "1"));
        var text = Parameters(ParameterBinding.fromText("Custom", parameterValueType.Text, "1"));
        Assert.NotEqual(Key(integer, ["Custom"]).Id, Key(text, ["Custom"]).Id);
        var original = CultureInfo.CurrentCulture;
        try
        {
            var value = Parameters(ParameterBinding.fromText("Custom", parameterValueType.Floating, "1.25"));
            var id = Key(value, ["Custom"]).Id;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(id, Key(value, ["Custom"]).Id);
        }
        finally { CultureInfo.CurrentCulture = original; }
        Assert.Throws<ArgumentException>(() => Key(first, ["Missing"]));
    }

    [Fact]
    public void Reopening_loads_the_original_compressed_data_without_calling_the_generator()
    {
        var store = Open(); var key = Key(Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 16))); var created = 0;
        var first = Ok(((ISortopiaDb)store).GetOrCreate(key, Factory(() => { created++; return Tests(16); }), Token));
        Assert.False(first.Item2);
        var file = store.GetPath(key.Reference);
        Assert.True(new FileInfo(file).Length < 600_000); // The uncompressed Boolean DTO is over 1.3 MiB.
        var original = File.ReadAllBytes(file);
        var second = Ok(((ISortopiaDb)Open()).GetOrCreate(key, Factory(() => { created++; throw new Exception("Must not generate"); }), Token));
        Assert.True(second.Item2); Assert.Equal(1, created);
        Assert.Equal(original, File.ReadAllBytes(file));
        var tests = Assert.IsType<functionData.SortableTests>(second.Item1.Value);
        Assert.Equal(65536, SortableTests.getSortableCount(tests.Item1));
        Assert.Equal(SortableTests.getId(((functionData.SortableTests)first.Item1.Value).Item1), SortableTests.getId(tests.Item1));
        Assert.Single(Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories));
    }

    [Fact]
    public void Codecs_preserve_prefixes_models_and_evaluations()
    {
        var name = MergeLibId.availableForChannelCount(4).First(value => value.MergeDimension == 2);
        var prefixed = CatalogTestHost.Invoke(false, ResultsFunctionNames.mergeSortableTests, Parameters(ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"),
            ParameterBinding.create(ParamKeys.mergeLibIdKey, name))).Head;
        var models = Models(); var tests = Tests();
        var evaluation = CatalogTestHost.Invoke(false, ResultsFunctionNames.sorterEval, Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4),
            ParameterBinding.create(ParamKeys.sorterEvalTypeKey, "V2"), ParameterBinding.create(ParamKeys.collectSortableTestsKey, false)), models, tests).Head;
        foreach (var value in new[] { prefixed, models, evaluation })
        {
            var key = Key(type: SortopiaDataType.create(FunctionData.kind(value), "roundtrip"));
            Ok(((ISortopiaDb)Open()).GetOrCreate(key, Factory(() => value), Token));
            var restored = Ok(((ISortopiaDb)Open()).Load(key.Reference, Token)).Value;
            Assert.Equal(FunctionData.kind(value), FunctionData.kind(restored));
            if (value is functionData.SortableTests before)
            {
                var after = Assert.IsType<functionData.SortableTests>(restored);
                Assert.Equal(before.Item2.Select(Ce.toIndex), after.Item2.Select(Ce.toIndex));
                Assert.Equal(SortableTests.getId(before.Item1), SortableTests.getId(after.Item1));
            }
            if (restored is functionData.SorterModelSet modelSet) Assert.Equal(2, modelSet.Item.Count);
            if (restored is functionData.SorterSetEval evaluated) Assert.Equal(2, evaluated.Item.SorterEvals.Length);
        }
    }

    [Fact]
    public void Corruption_is_an_error_and_never_regenerates_or_overwrites_data()
    {
        var store = Open(); var key = Key();
        Assert.Null(Ok(((ISortopiaDb)store).TryLoad(key, Token)));
        Ok(((ISortopiaDb)store).GetOrCreate(key, Factory(() => Tests()), Token));
        var file = store.GetPath(key.Reference); var bytes = File.ReadAllBytes(file); bytes[^1] ^= 1; File.WriteAllBytes(file, bytes);
        var calls = 0;
        var result = ((ISortopiaDb)Open()).GetOrCreate(key, Factory(() => { calls++; return Tests(); }), Token);
        Assert.True(result.IsError); Assert.Contains("checksum", result.ErrorValue); Assert.Equal(0, calls);
        Assert.Equal(bytes, File.ReadAllBytes(file));
    }

    [Fact]
    public void Failure_cancellation_and_temporary_files_do_not_publish_incomplete_data()
    {
        var store = Open(); var key = Key(setup: false); var path = store.GetPath(key.Reference);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path + ".interrupted.tmp", "partial");
        Assert.Empty(Ok(((ISortopiaDb)store).FindResults(key, Token)));
        var failure = ((ISortopiaDb)store).GetOrCreate(key, Factory(() => throw new InvalidOperationException("failed generator")), Token);
        Assert.True(failure.IsError); Assert.False(File.Exists(path));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Assert.ThrowsAny<OperationCanceledException>(() => ((ISortopiaDb)store).GetOrCreate(key, Factory(() => { cancellation.Cancel(); return Tests(); }), cancellation.Token));
        Assert.False(File.Exists(path));
        Assert.Null(Ok(((ISortopiaDb)store).TryLoad(key, Token)));
    }

    [Fact]
    public async Task Concurrent_creators_publish_one_file_and_only_generate_once()
    {
        var key = Key(); var calls = 0;
        var factory = Factory(() => { Interlocked.Increment(ref calls); Thread.Sleep(60); return Tests(); });
        var values = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Ok(((ISortopiaDb)Open()).GetOrCreate(key, factory, Token)), Token)));
        Assert.Equal(1, calls); Assert.Single(values, value => !value.Item2);
        Assert.Single(values.Select(value => value.Item1.Reference.Id).Distinct());
        Assert.Single(Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories));
    }

    [Fact]
    public void Different_setup_inputs_never_overwrite_matching_result_query_keys()
    {
        var a = Key().Reference;
        var b = Key(Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 8))).Reference;
        var keyA = Key(setup: false, inputs: [a]); var keyB = Key(setup: false, inputs: [b]);
        Assert.Equal(keyA.QueryId, keyB.QueryId); Assert.NotEqual(keyA.Id, keyB.Id);
        var store = (ISortopiaDb)Open();
        Ok(store.GetOrCreate(keyA, Factory(() => Tests(4)), Token));
        Ok(store.GetOrCreate(keyB, Factory(() => Tests(8)), Token));
        Assert.Equal(2, Ok(store.FindResults(Key(setup: false), Token)).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Host_database_reuses_each_output_independently_on_partial_hits(bool setup)
    {
        var settings = RunExecutionTests.Settings(true);
        settings.setupFunctionName = SetupFunctionNames.evaluationStandard;
        settings.setupDataTypes = [];
        settings.setupParameterDefaults = [.. RunExecutionTests.ModelDefaults(), RunExecutionTests.Binding("SortableDataFormat", "Bools")];
        var branch = ((run.BranchRun)RunExecutionTests.MakeRun(true, settings)).Item;
        var store = new SortopiaFileDb(temporary.FullName, branch.ParentProjectId);
        var testCalls = 0; var modelCalls = 0;
        foreach (var stage in new[] { 3, 4, 4 })
        {
            var parameters = parameterSet.fromBindings(branch.SetupParameterDefaults).WithBinding(ParameterBinding.create(ParamKeys.stageLengthKey, stage));
            IRunDatabase context = new FileRunDatabase(store, branch.ParentProjectId, setup ? branch.SetupDatabaseName : branch.ResultsDatabaseName,
                setup, setup ? branch.SetupFunctionName : ResultsFunctionNames.standardEvaluationInputs, "1",
                branch.ProjectConfiguration.Setup.DataTypes, parameters, FuncConvert.FromFunc<storedDataReference, bool>(_ => false), Token);
            context.SaveIfMissing(sortopiaDataType.NewSortableTests(""), List<storedDataReference>(), FuncConvert.FromFunc<Unit, functionData>(_ => { testCalls++; return Tests(); }));
            context.SaveIfMissing(sortopiaDataType.NewSorterModelSet(""), List<storedDataReference>(), FuncConvert.FromFunc<Unit, functionData>(_ => { modelCalls++; return Models(stage); }));
            Assert.Equal(2, context.StoredOutputs.Length);
        }
        Assert.Equal(1, testCalls); Assert.Equal(2, modelCalls);
        Assert.Equal(3, Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Host_database_rejects_undeclared_types_labels_wrong_payloads_and_duplicate_saves()
    {
        var type = sortopiaDataType.NewSortableTests("");
        var parameters = Parameters(ParameterBinding.create(ParamKeys.channelCountKey, 4), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools"));
        var outputs = MapModule.OfArray(new[] { Tuple.Create(type, List("ChannelCount", "SortableDataFormat")) });
        IRunDatabase database = new FileRunDatabase(Open(), projectId, "Setup", true, SetupFunctionNames.standardTests, "1", outputs,
            parameters, FuncConvert.FromFunc<storedDataReference, bool>(_ => false), Token);
        var make = FuncConvert.FromFunc<Unit, functionData>(_ => Tests());
        Assert.Throws<ArgumentException>(() => database.SaveIfMissing(sortopiaDataType.NewSortableTests("other"), List<storedDataReference>(), make));
        Assert.Throws<ArgumentException>(() => database.SaveIfMissing(sortopiaDataType.NewSorterModelSet(""), List<storedDataReference>(), make));
        Assert.Contains("payload", Assert.Throws<InvalidOperationException>(() => database.SaveIfMissing(type, List<storedDataReference>(),
            FuncConvert.FromFunc<Unit, functionData>(_ => Models()))).Message);
        Assert.Empty(database.StoredOutputs);
        database.SaveIfMissing(type, List<storedDataReference>(), make);
        Assert.Throws<InvalidOperationException>(() => database.SaveIfMissing(type, List<storedDataReference>(), make));
        Assert.Single(database.StoredOutputs);
        Assert.Single(Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
