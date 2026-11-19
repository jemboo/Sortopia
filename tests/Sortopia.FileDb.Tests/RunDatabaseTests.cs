using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class RunDatabaseTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-run-database-");
    private readonly Guid projectId = Guid.NewGuid();
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly sortopiaDataType Type = sortopiaDataType.NewSortableTests("sample");
    private static runDataOutput Output(int generation) => RunDataOutput.create(Type, ListModule.Singleton("GenerationCurrent"),
        parameterSet.fromBindings([ParameterBinding.fromText("GenerationCurrent", parameterValueType.Integer, generation.ToString())]), []);
    private static sortopiaStorageData Payload(Guid id) => sortopiaStorageData.NewSortableTests(
        sortableTests.NewBools(sortableBinaryTests.create(id, 4, [])));
    private FSharpMap<runDataOutput, plannedOutput> Plan(params runDataOutput[] outputs) =>
        OutputPlan.bind(projectId, "Results", false, "test", "1", ListModule.OfSeq(outputs.Select(output =>
            Tuple.Create(output, FuncConvert.FromAction<sortopiaStorageData>(_ => { })))));
    private SortopiaFileDb Store() => new(temporary.FullName, projectId);

    [Fact]
    public void Same_type_outputs_have_independent_queries_references_and_cache_entries()
    {
        var first = Output(0); var second = Output(2);
        var plan = Plan(first, second);
        var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        var storage = new FileRunDatabase(Store(), plan, Token);
        IRunDatabase database = storage;
        Assert.Throws<ArgumentException>(() => storage.Outputs.Output(Type));
        Assert.Throws<ArgumentException>(() => database.Find(Type));
        database.Save(first, FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => Payload(firstId)));
        database.Save(second, FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => Payload(secondId)));
        Assert.Equal(2, storage.Outputs.Stored.Length);
        Assert.Throws<ArgumentException>(() => database.Find(Type));
        Assert.NotEqual(database.Find(first).Id, database.Find(second).Id);
        Assert.NotEqual(database.Find(first).QueryId, database.Find(second).QueryId);
        foreach (var (output, id) in new[] { (first, firstId), (second, secondId) })
        {
            var reference = database.Find(output);
            Assert.Equal(id, SortableTests.getId(Assert.IsType<sortopiaStorageData.SortableTests>(database.Load(reference)).Item));
            Assert.Equal(Path.Combine(temporary.FullName, "data", "results", "SortableTests", reference.Id + ".msgpack"), Store().GetPath(reference));
        }
        var reopenedStorage = new FileRunDatabase(Store(), plan, Token);
        IRunDatabase reopened = reopenedStorage;
        var neverGenerate = FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Cached output must not be generated."));
        reopened.Save(first, neverGenerate);
        reopened.Save(second, neverGenerate);
        Assert.All(reopenedStorage.Outputs.Stored, output => Assert.True(output.Reused));
        Assert.Equal(outputState.Complete, OutputPlan.inspect(Store(), plan, Token).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saves_reuse_outputs_and_verify_replays_without_overwriting_or_double_counting(bool supplyValue)
    {
        var output = Output(0); var plan = Plan(output); var value = Payload(Guid.NewGuid());
        var storage = new FileRunDatabase(Store(), plan, Token);
        IRunDatabase database = storage;
        var reference = database.Find(Type);
        Assert.Equal(reference, database.Find(output));
        Assert.Throws<InvalidOperationException>(() => database.Load(reference));
        Assert.Empty(storage.Outputs.Stored);
        Assert.Empty(Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories));
        var generated = 0;
        var factory = FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => { generated++; return value; });
        var neverGenerate = FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Cached output must not be generated."));
        if (supplyValue) database.Save(output, value);
        else database.Save(output, factory);
        Assert.Equal(supplyValue ? 0 : 1, generated);
        Assert.Equal(reference, database.Find(Type));
        Assert.Equal(value, database.Load(reference));
        var path = Store().GetPath(reference);
        var bytes = File.ReadAllBytes(path);
        database.Save(output, neverGenerate);
        database.Save(output, value);
        Assert.Contains("differs from replay", Assert.Throws<InvalidOperationException>(() => database.Save(output, Payload(Guid.NewGuid()))).Message);
        Assert.False(Assert.Single(storage.Outputs.Stored).Reused);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var replayStorage = new FileRunDatabase(Store(), plan, Token);
        IRunDatabase replay = replayStorage;
        Assert.Contains("differs from replay", Assert.Throws<InvalidOperationException>(() => replay.Save(output, Payload(Guid.NewGuid()))).Message);
        Assert.Empty(replayStorage.Outputs.Stored);
        replay.Save(output, neverGenerate);
        replay.Save(output, value);
        replay.Save(output, neverGenerate);
        Assert.True(Assert.Single(replayStorage.Outputs.Stored).Reused);
        Assert.Contains("differs from replay", Assert.Throws<InvalidOperationException>(() => replay.Save(output, Payload(Guid.NewGuid()))).Message);
        Assert.Single(replayStorage.Outputs.Stored);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void Completion_requires_saving_every_planned_output_even_when_all_files_are_cached()
    {
        var first = Output(0); var second = Output(2); var plan = Plan(first, second);
        IRunDatabase initial = new FileRunDatabase(Store(), plan, Token);
        initial.Save(first, Payload(Guid.NewGuid()));
        initial.Save(second, Payload(Guid.NewGuid()));
        var neverGenerate = FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Cached outputs must not be generated."));

        foreach (var saveCount in new[] { 0, 1, 2 })
        {
            var storage = new FileRunDatabase(Store(), plan, Token);
            var host = new runHost(runExecutionPhase.Results, parameterSet.fromBindings([]), storage, storage.Outputs, Token, 1, null, null);
            var before = host.Outputs.Stored;
            var definition = FunctionDefinition.results("1", ListModule.Empty<paramKeyDescriptor>(),
                ListModule.Singleton(Tuple.Create(Type, ListModule.Empty<string>())), FuncConvert.FromFunc<runHost, Unit>(current =>
                {
                    foreach (var output in current.Outputs.Planned) current.Database.Load(current.Database.Find(output));
                    foreach (var output in current.Outputs.Planned.Take(saveCount)) current.Database.Save(output, neverGenerate);
                    return null!;
                }));
            if (saveCount < 2)
                Assert.Contains("exactly its planned outputs", Assert.Throws<InvalidOperationException>(() => definition.Execute.Invoke(host)).Message);
            else definition.Execute.Invoke(host);
            Assert.Empty(before);
            Assert.Equal(saveCount, host.Outputs.Stored.Length);
            Assert.All(host.Outputs.Stored, output => Assert.True(output.Reused));
            Assert.Equal(2, host.Outputs.Planned.Length);
        }
    }

    [Fact]
    public void Unplanned_queries_and_dependencies_cannot_write_files()
    {
        var output = Output(0);
        var storage = new FileRunDatabase(Store(), Plan(output), Token);
        IRunDatabase database = storage;
        var dependency = new storedDataReference(projectId, Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Setup, Type);
        var changedInputs = new runDataOutput(Type, output.QueryParameters, ListModule.Singleton(dependency));
        foreach (var invalid in new[] { Output(1), changedInputs })
        {
            Assert.Throws<ArgumentException>(() => database.Find(invalid));
            Assert.Throws<ArgumentException>(() => database.Save(invalid, Payload(Guid.NewGuid())));
            Assert.Throws<ArgumentException>(() => database.Save(invalid,
                FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new Exception("Unplanned output must not be generated."))));
        }
        Assert.Empty(storage.Outputs.Stored);
        Assert.Throws<ArgumentException>(() => database.Find(sortopiaDataType.NewSortableTests("unknown")));
        Assert.Empty(Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories));
    }

    public void Dispose() => temporary.Delete(true);
}
