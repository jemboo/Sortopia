using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceFileExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-file-execution-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Integer) =>
        new() { keyName = key, value = value, valueType = (int)type };
    private static project Project(string name, hierarchyKind kind, projectSettingsDto settings, parameterSpanDto[]? setup = null)
    {
        var parent = project.create(Guid.NewGuid(), name, name, SettingsDto.toDomain(kind, settings), []);
        var value = RunDto.toDomain(parent, new runDto
        {
            schemaVersion = 11, id = Guid.NewGuid(), parentProjectId = parent.Id, runName = "Trial",
            runKind = kind == hierarchyKind.Root ? "root" : "branch", resultsParameterSpans = [], setupParameterSpans = setup ?? []
        });
        return project.withRuns(parent, [value]);
    }
    private workspace Create(bool failSecondSetup = false)
    {
        var sourceSettings = RunExecutionTests.Settings(false);
        sourceSettings.resultsQueryParameterKeys = ["ChannelCount"];
        sourceSettings.resultsParameterDefaults = [..sourceSettings.resultsParameterDefaults, Binding("ChannelCount", "4")];
        var source = Project("Source", hierarchyKind.Root, sourceSettings);
        var consumerSettings = RunExecutionTests.Settings(true);
        consumerSettings.setupDataTypes = [new() { dataType = sourceSettings.resultsSortopiaDataTypes[0], sourceProjectId = source.Id, queryParameterKeys = ["SortableDataFormat"] }];
        var consumer = Project("Consumer", hierarchyKind.Branch, consumerSettings,
            failSecondSetup ? [new() { keyName = "SortableDataFormat", values = ["Bools", "invalid"] }] : null);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [
            workspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Outer", "Outer", [
                workspaceModule.create(hierarchyKind.Root, Guid.NewGuid(), "Inner", "Inner", [], [source])], []),
            workspaceModule.create(hierarchyKind.Branch, Guid.NewGuid(), "Consumers", "Consumers", [], [consumer])]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        return WorkspaceFileStore.LoadDefinition(temporary.FullName);
    }
    private static project Named(workspace world, string name) => world.ProjectIndex.Values.Single(value => value.ProjectName == name);
    private runExecutionReport Execute(workspace world, string name) => FileRunExecution.execute(world, temporary.FullName, Named(world, name).Runs[0], null!, Token);
    private static void Completed(runExecutionReport report) => Assert.True(report.Status == runExecutionStatus.Completed, report.Error);

    [Fact]
    public void Nested_source_lookup_uses_producer_projection_and_reuses_saved_files_after_reopening()
    {
        var world = Create();
        var source = Named(world, "Source"); var consumer = Named(world, "Consumer");
        Assert.Equal(source, world.FindProject(source.Id).Value);
        Assert.EndsWith(Path.Combine("modules", "Outer", "modules", "Inner", "projects", "Source"), WorkspaceData.projectFolder(world, temporary.FullName, source.Id));
        var missing = Execute(world, "Consumer");
        Assert.Contains("Execute the source run first", missing.Error); Assert.Empty(missing.Setup);
        var published = Execute(world, "Source"); Completed(published);
        var branch = ((run.BranchRun)consumer.Runs[0]).Item;
        var parameters = BranchRun.makeSetupParameterSets(branch)[0];
        var entry = branch.SetupDataTypes.Head;
        var loaded = WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token);
        Assert.Equal(published.Results[0].StoredOutputs.Head.Reference, loaded.Reference);
        // The source projects by ChannelCount, while this setup projects by SortableDataFormat.
        var other = parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 8), ParameterBinding.create(ParamKeys.sortableDataFormatKey, "Bools")]);
        Assert.Contains("missing", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, entry, other, Token)).Message);
        var wrongType = new setupDataTypeConfig(sortopiaDataType.NewSortableTests("different"), source.Id, entry.QueryParameterKeys);
        Assert.Contains("does not provide", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, wrongType, parameters, Token)).Message);
        var absent = new setupDataTypeConfig(entry.DataType, Guid.NewGuid(), entry.QueryParameterKeys);
        Assert.Contains("not found", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, absent, parameters, Token)).Message);
        var first = Execute(world, "Consumer"); Completed(first);
        var dataFiles = Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories).Where(path => path.Contains(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar)).ToDictionary(path => path, File.ReadAllBytes);
        var reopened = WorkspaceFileStore.LoadDefinition(temporary.FullName);
        var second = Execute(reopened, "Consumer"); Completed(second);
        Assert.All(second.Setup.Concat(second.Results).SelectMany(call => call.StoredOutputs), output => Assert.True(output.Reused));
        Assert.Equal(first.Setup[0].StoredOutputs.Head.Reference, second.Setup[0].StoredOutputs.Head.Reference);
        foreach (var file in dataFiles) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        Assert.Equal(3, dataFiles.Count); // One source result, one setup and one consumer result.
        var receipts = Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories).Where(path => path.Contains(Path.DirectorySeparatorChar + "executions" + Path.DirectorySeparatorChar));
        Assert.Equal(4, receipts.Count());
    }

    [Fact]
    public void A_later_setup_failure_keeps_prior_data_reusable_and_records_the_failure()
    {
        var world = Create(failSecondSetup: true); Completed(Execute(world, "Source"));
        var first = Execute(world, "Consumer");
        Assert.Equal(runExecutionStatus.Failed, first.Status); Assert.Contains("Setup set 2", first.Error);
        Assert.Single(first.Setup); Assert.Empty(first.Results); Assert.False(first.Setup[0].StoredOutputs.Head.Reused);
        var second = Execute(WorkspaceFileStore.LoadDefinition(temporary.FullName), "Consumer");
        Assert.Equal(runExecutionStatus.Failed, second.Status); Assert.Single(second.Setup); Assert.Empty(second.Results);
        Assert.True(second.Setup[0].StoredOutputs.Head.Reused);
        var folder = WorkspaceData.projectFolder(world, temporary.FullName, Named(world, "Consumer").Id);
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories));
        var receipts = Directory.GetFiles(Path.Combine(folder, "executions"), "*.msgpack").Select(path => Sortopia.Core.Serialization.MessagePackSetup.deserialize<executionReceiptDto>(File.ReadAllBytes(path))).ToArray();
        Assert.Equal(2, receipts.Length); Assert.All(receipts, receipt => Assert.Equal("Failed", receipt.Status));
    }

    [Fact]
    public void Ambiguous_or_corrupt_upstream_data_is_reported_instead_of_chosen_arbitrarily()
    {
        var world = Create(); var source = Named(world, "Source");
        var branch = ((run.BranchRun)Named(world, "Consumer").Runs[0]).Item;
        var entry = branch.SetupDataTypes.Head; var parameters = BranchRun.makeSetupParameterSets(branch)[0];
        var folder = WorkspaceData.projectFolder(world, temporary.FullName, source.Id);
        var store = new SortopiaFileDb(folder, source.Id);
        var output = ResultsFunctions.makeSortableTests(RunParameterSets.create(source.Runs[0]).Results[0], ListModule.Empty<functionData>()).ResultValue.Head;
        var factory = FuncConvert.FromFunc<Unit, FSharpResult<functionData, string>>(_ => FSharpResult<functionData, string>.NewOk(output));
        dataKey Key(bool withDependency) => DataKey.create(source.Id, source.ResultsDatabaseName, false, source.ResultsFunctionName, "1", entry.DataType,
            source.ResultsQueryParameterKeys, parameters, withDependency ? ListModule.OfArray(new[] { new storedDataReference(source.Id, Guid.NewGuid(), Guid.NewGuid(), true, entry.DataType) }) : ListModule.Empty<storedDataReference>());
        var firstKey = Key(false); var secondKey = Key(true);
        Assert.True(((ISortopiaDb)store).GetOrCreate(firstKey, factory, Token).IsOk);
        Assert.True(((ISortopiaDb)store).GetOrCreate(secondKey, factory, Token).IsOk);
        Assert.Contains("ambiguous", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token)).Message);
        var bytes = File.ReadAllBytes(store.GetPath(firstKey.Reference)); bytes[^1] ^= 1; File.WriteAllBytes(store.GetPath(firstKey.Reference), bytes);
        Assert.Contains("checksum", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token)).Message);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
