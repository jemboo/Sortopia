using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceFileExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-file-execution-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static parameterBindingDto Binding(string key, string value, parameterValueType type = parameterValueType.Integer) =>
        new() { keyName = key, value = value, valueType = (int)type };
    private static project Project(string name, hierarchyKind kind, projectSettingsDto settings, parameterSpanDto[]? setup = null) =>
        RunExecutionTests.MakeProject(name, kind == hierarchyKind.Branch, settings, setup: setup);
    private workspace Create(bool failSecondSetup = false)
    {
        var sourceSettings = RunExecutionTests.Settings(false);
        sourceSettings.resultsQueryParameterKeys = [];
        var source = Project("Source", hierarchyKind.Root, sourceSettings);
        var consumerSettings = RunExecutionTests.Settings(true);
        consumerSettings.setupFunctionName = SetupFunctionNames.evaluationMergeMssi;
        consumerSettings.setupParameterDefaults = [.. RunExecutionTests.UniformModelDefaults(), .. sourceSettings.resultsParameterDefaults];
        consumerSettings.setupDataTypes = [new() { dataType = sourceSettings.resultsSortopiaDataTypes[0], sourceProjectId = source.Id, queryParameterKeys = ["SortableDataFormat"] }];
        consumerSettings.resultsFunctionName = ResultsFunctionNames.sorterEval;
        consumerSettings.resultsParameterDefaults = [Binding("ChannelCount", "4"), Binding("SorterEvalType", "V1", parameterValueType.Text), Binding("CollectSortableTests", "false", parameterValueType.Boolean)];
        var consumer = Project("Consumer", hierarchyKind.Branch, consumerSettings,
            failSecondSetup ? [new() { keyName = "SortableDataFormat", values = ["Bools", "Ints"] }] : null);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Outer", "Outer", [
                WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Inner", "Inner", [], [source])], []),
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Consumers", "Consumers", [], [consumer])]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        return WorkspaceFileStore.LoadDefinition(temporary.FullName);
    }
    private static project Named(workspace world, string name) => world.ProjectIndex.Values.Single(value => value.ProjectName == name);
    private runExecutionReport Execute(workspace world, string name) => FileRunExecution.execute(world, temporary.FullName, Named(world, name).Runs[0], null!, Token);
    private static void Completed(runExecutionReport report) => Assert.True(report.Status == runExecutionStatus.Completed, report.Error);

    // A test-only host adapter for an independent setup definition, without adding it
    // to the application catalogs. Source lookup and all storage use the real file store.
    private sealed class WorkspaceSources(workspace world, string root) : ISourceResults
    {
        private readonly Dictionary<storedDataReference, functionData> loaded = [];
        public List<(Guid ProjectId, sortopiaDataType DataType)> Reads { get; } = [];
        public storedDataReference Find(Guid projectId, sortopiaDataType dataType, parameterSet parameters)
        {
            var entry = new setupDataTypeConfig(dataType, projectId, ListModule.Empty<string>());
            var data = WorkspaceData.loadSource(world, root, entry, parameters, Token);
            loaded[data.Reference] = data.Value;
            Reads.Add((projectId, dataType));
            return data.Reference;
        }
        public functionData Load(storedDataReference reference) => loaded[reference];
    }

    [Fact]
    public void Setup_reads_each_data_type_from_its_own_project_and_reuses_the_saved_outputs()
    {
        var testsType = sortopiaDataType.NewSortableTests("");
        var modelsType = sortopiaDataType.NewSorterModelSet("");
        var testsSettings = RunExecutionTests.Settings(false);
        testsSettings.resultsFunctionName = ResultsFunctionNames.standardSortableTests;
        testsSettings.resultsDatabaseName = "Test archive";
        testsSettings.resultsParameterDefaults = [Binding("ChannelCount", "4"), Binding("SortableDataFormat", "Bools", parameterValueType.Text)];
        var modelsSettings = RunExecutionTests.Settings(false);
        modelsSettings.resultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        modelsSettings.resultsDatabaseName = "Model archive";
        modelsSettings.resultsSortopiaDataTypes = [new() { kind = "SorterModelSet", value = "" }];
        modelsSettings.resultsParameterDefaults = RunExecutionTests.UniformModelDefaults();
        var testsProject = Project("Tests source", hierarchyKind.Root, testsSettings);
        var modelsProject = Project("Models source", hierarchyKind.Root, modelsSettings);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Sources", "Sources", [], [testsProject, modelsProject])]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        world = WorkspaceFileStore.LoadDefinition(temporary.FullName);
        var publishedTests = Execute(world, "Tests source"); Completed(publishedTests);
        var publishedModels = Execute(world, "Models source"); Completed(publishedModels);

        var testsDefinition = FunctionCatalogs.resultsFunctionCatalog[testsProject.ResultsFunctionName];
        var modelsDefinition = FunctionCatalogs.resultsFunctionCatalog[modelsProject.ResultsFunctionName];
        var required = ListModule.OfSeq(testsDefinition.RequiredParameters.Concat(modelsDefinition.RequiredParameters).DistinctBy(key => key.Name));
        var parameters = parameterSet.fromBindings(RunParameterSets.create(testsProject.Runs[0]).Results.Single().ParamMap.Values.Concat(RunParameterSets.create(modelsProject.Runs[0]).Results.Single().ParamMap.Values).DistinctBy(binding => binding.KeyName));
        var sourceTypes = ListModule.OfArray(new[] { testsType, modelsType });
        var projects = MapModule.OfArray(new[] { Tuple.Create(testsType, testsProject.Id), Tuple.Create(modelsType, modelsProject.Id) });
        var copies = 0;
        var definition = FunctionDefinition.setupWithSources("1", required,
            ListModule.OfArray(new[] { Tuple.Create(testsType, testsDefinition.DataTypes[testsType]), Tuple.Create(modelsType, modelsDefinition.DataTypes[modelsType]) }),
            ListModule.OfSeq(sourceTypes.Select(type => Tuple.Create(type, ListModule.Singleton(type)))), FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(host =>
                FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(sources =>
                {
                    foreach (var dataType in sourceTypes)
                    {
                        var reference = host.SourceResults.Find(sources[dataType], dataType, host.Parameters);
                        host.SetupDb.SaveIfMissing(dataType, ListModule.Singleton(reference), FuncConvert.FromFunc<Unit, functionData>(_ =>
                        { copies++; return host.SourceResults.Load(reference); }));
                    }
                    return null!;
                })));
        var consumerId = Guid.NewGuid();
        var outputFolder = Path.Combine(temporary.FullName, "Consumer");
        var plan = OutputPlan.create(consumerId, "Copied inputs", true, "copy-sources", "1", definition.DataTypes,
            definition.OutputDependencies, parameters, ListModule.OfSeq(publishedTests.Results.Concat(publishedModels.Results).SelectMany(call => call.StoredOutputs).Select(output => output.Reference)));
        runHost Host(WorkspaceSources sources) => new(runExecutionPhase.Setup, parameters,
            new FileRunDatabase(new SortopiaFileDb(outputFolder, consumerId), plan, Token),
            null!, sources, Token, Environment.ProcessorCount);
        var reader = new WorkspaceSources(world, temporary.FullName);
        var setup = Host(reader);
        Assert.Contains("exactly the source projects", Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(projects.Remove(modelsType))).Message);
        Assert.Empty(reader.Reads);
        var swapped = MapModule.OfArray(new[] { Tuple.Create(testsType, modelsProject.Id), Tuple.Create(modelsType, testsProject.Id) });
        Assert.Contains("does not provide", Assert.Throws<InvalidOperationException>(() => definition.Execute.Invoke(setup).Invoke(swapped)).Message);
        Assert.Empty(setup.SetupDb.StoredOutputs);

        definition.Execute.Invoke(setup).Invoke(projects);
        Assert.Equal(new[] { (testsProject.Id, testsType), (modelsProject.Id, modelsType) }, reader.Reads);
        Assert.Equal(2, copies);
        Assert.Equal(2, setup.SetupDb.StoredOutputs.Length);
        Assert.All(setup.SetupDb.StoredOutputs, output => { Assert.Equal(consumerId, output.Reference.ProjectId); Assert.True(output.Reference.IsSetup); });
        var sourceTests = (functionData.SortableTests)reader.Load(publishedTests.Results[0].StoredOutputs.Head.Reference);
        var copiedTests = (functionData.SortableTests)setup.SetupDb.Load(testsType);
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(sourceTests.Item1), Sortopia.Sorting.Inputs.SortableTests.getId(copiedTests.Item1));
        var sourceModels = (functionData.SorterModelSet)reader.Load(publishedModels.Results[0].StoredOutputs.Head.Reference);
        var copiedModels = (functionData.SorterModelSet)setup.SetupDb.Load(modelsType);
        Assert.Equal(sourceModels.Item.SorterModels.Select(Sortopia.Models.Sorting.SorterModel.getId), copiedModels.Item.SorterModels.Select(Sortopia.Models.Sorting.SorterModel.getId));
        var repeated = Host(new WorkspaceSources(WorkspaceFileStore.LoadDefinition(temporary.FullName), temporary.FullName));
        definition.Execute.Invoke(repeated).Invoke(projects);
        Assert.Equal(2, copies);
        Assert.All(repeated.SetupDb.StoredOutputs, output => Assert.True(output.Reused));
        Assert.Equal(2, Directory.GetFiles(outputFolder, "*.msgpack", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public void Unit_functions_save_through_the_host_and_read_only_their_configured_sources_and_setup_batch()
    {
        var world = Create();
        var published = Execute(world, "Source"); Completed(published);
        var source = Named(world, "Source"); var consumer = Named(world, "Consumer");
        var value = consumer.Runs[0]; var branch = ((run.BranchRun)value).Item;
        var factory = new FileRunHost(world, temporary.FullName, value, Token);
        var sets = RunParameterSets.create(value);
        var setup = factory.ForSetup(sets.Setup.Value[0]);
        var testsType = sortopiaDataType.NewSortableTests("");
        var resultType = sortopiaDataType.NewSorterSetEval("");
        var empty = ListModule.Empty<storedDataReference>();
        var never = FuncConvert.FromFunc<Unit, functionData>(_ => throw new InvalidOperationException("Factory must not run."));

        Assert.Contains("not allowed during setup", Assert.Throws<InvalidOperationException>(() => setup.ResultsDb.SaveIfMissing(testsType, empty, never)).Message);
        Assert.Throws<ArgumentException>(() => setup.SourceResults.Find(Guid.NewGuid(), testsType, setup.Parameters));
        Assert.Throws<ArgumentException>(() => setup.SourceResults.Find(source.Id, sortopiaDataType.NewSortableTests("other"), setup.Parameters));
        var sourceReference = published.Results[0].StoredOutputs.Head.Reference;
        // Planning resolves the exact authorized sources before execution.
        Assert.IsType<functionData.SortableTests>(setup.SourceResults.Load(sourceReference));
        Assert.Throws<ArgumentException>(() => setup.SourceResults.Load(new storedDataReference(source.Id, Guid.NewGuid(), sourceReference.QueryId, false, testsType)));
        Assert.Equal(sourceReference, setup.SourceResults.Find(source.Id, testsType, setup.Parameters));
        var sourceTests = Assert.IsType<functionData.SortableTests>(setup.SourceResults.Load(sourceReference));

        var definition = FunctionCatalogs.setupFunctionCatalog[branch.SetupFunctionName];
        var projects = MapModule.OfSeq(branch.SourceProjectIds.Where(pair => definition.SourceDataTypes.Contains(pair.Key))
            .Select(pair => Tuple.Create(pair.Key, pair.Value)));
        Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(MapModule.Empty<sortopiaDataType, Guid>()));
        var wrongProject = MapModule.OfArray(new[] { Tuple.Create(testsType, Guid.NewGuid()) });
        Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(wrongProject));
        Assert.Empty(setup.SetupDb.StoredOutputs);
        definition.Execute.Invoke(setup).Invoke(projects);
        Assert.Equal(2, setup.SetupDb.StoredOutputs.Length);
        Assert.All(setup.SetupDb.StoredOutputs, output => { Assert.True(output.Reference.IsSetup); Assert.Equal(consumer.Id, output.Reference.ProjectId); });

        Assert.Throws<ArgumentException>(() => factory.ForResults(sets.Results[0], ListModule.Empty<storedOutput>()));
        var results = factory.ForResults(sets.Results[0], setup.SetupDb.StoredOutputs);
        var savedTests = Assert.IsType<functionData.SortableTests>(results.SetupDb.Load(testsType));
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(sourceTests.Item1), Sortopia.Sorting.Inputs.SortableTests.getId(savedTests.Item1));
        Assert.Contains("read-only", Assert.Throws<InvalidOperationException>(() => results.SetupDb.SaveIfMissing(testsType, empty, never)).Message);
        Assert.Throws<ArgumentException>(() => results.SetupDb.Load(sortopiaDataType.NewSortableTests("other")));
        Assert.Throws<InvalidOperationException>(() => results.SourceResults.Find(source.Id, testsType, setup.Parameters));
        Assert.Throws<ArgumentException>(() => results.ResultsDb.SaveIfMissing(resultType, ListModule.Singleton(sourceReference), never));
        FunctionCatalogs.resultsFunctionCatalog[value.ResultsFunctionName].Execute.Invoke(results);
        var output = Assert.Single(results.ResultsDb.StoredOutputs);
        Assert.False(output.Reference.IsSetup); Assert.Equal(consumer.Id, output.Reference.ProjectId);
        var evaluation = Assert.IsType<functionData.SorterSetEval>(results.ResultsDb.Load(resultType));
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(savedTests.Item1), evaluation.Item.SorterTestId);
        Assert.Equal(4, Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories)
            .Count(path => path.Contains(Path.DirectorySeparatorChar + "data" + Path.DirectorySeparatorChar)));
    }

    [Fact]
    public void Hosts_require_a_saved_run_and_reject_access_to_the_wrong_phase()
    {
        var world = Create(); var source = Named(world, "Source"); var value = source.Runs[0];
        var factory = new FileRunHost(world, temporary.FullName, value, Token);
        var parameters = RunParameterSets.create(value).Results[0];
        Assert.Throws<InvalidOperationException>(() => factory.ForSetup(parameters));
        var results = factory.ForResults(parameters, ListModule.Empty<storedOutput>());
        Assert.Throws<ArgumentException>(() => results.SetupDb.Load(sortopiaDataType.NewSortableTests("")));
        Assert.Throws<ArgumentException>(() => SetupFunctions.standardTests.Execute.Invoke(results).Invoke(MapModule.Empty<sortopiaDataType, Guid>()));
        var unsaved = RunDto.toDomain(source, new runDto
        {
            schemaVersion = 14, id = Guid.NewGuid(), parentProjectId = source.Id, runName = "Unsaved", runKind = "root",
            resultsParameterSpans = RunDto.fromDomain(source.Runs[0]).resultsParameterSpans, setupParameterSpans = []
        });
        Assert.Contains("saved run", Assert.Throws<InvalidOperationException>(() => new FileRunHost(world, temporary.FullName, unsaved, Token)).Message);
    }

    [Fact]
    public void Nested_source_lookup_uses_producer_projection_and_reuses_saved_files_after_reopening()
    {
        var world = Create();
        var source = Named(world, "Source"); var consumer = Named(world, "Consumer");
        Assert.Equal(source, world.FindProject(source.Id).Value);
        Assert.EndsWith(Path.Combine("Outer", "Inner", "Source"), WorkspaceData.projectFolder(world, temporary.FullName, source.Id));
        var missing = Execute(world, "Consumer");
        Assert.Contains("Execute the source run first", missing.Error); Assert.Empty(missing.Setup);
        var published = Execute(world, "Source"); Completed(published);
        var branch = ((run.BranchRun)consumer.Runs[0]).Item;
        var parameters = BranchRun.makeSetupParameterSets(branch)[0];
        var entry = branch.SetupDataTypes.Single(output => output.DataType.IsSortableTests);
        var loaded = WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token);
        Assert.Equal(published.Results[0].StoredOutputs.Head.Reference, loaded.Reference);
        // Model-only parameters do not affect the source's library/format projection.
        var other = parameters.WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 8));
        Assert.Equal(loaded.Reference, WorkspaceData.loadSource(world, temporary.FullName, entry, other, Token).Reference);
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
        Assert.Equal(4, dataFiles.Count); // One source result, two setup outputs and one consumer evaluation.
        var receipts = Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories).Where(path => path.Contains(Path.DirectorySeparatorChar + "executions" + Path.DirectorySeparatorChar));
        Assert.Empty(receipts);
    }

    [Fact]
    public void A_later_setup_failure_keeps_prior_data_reusable_and_reports_the_failure()
    {
        var world = Create(failSecondSetup: true); Completed(Execute(world, "Source"));
        var first = Execute(world, "Consumer");
        Assert.Equal(runExecutionStatus.Failed, first.Status); Assert.Contains("Setup set 2", first.Error);
        Assert.Single(first.Setup); Assert.Empty(first.Results); Assert.False(first.Setup[0].StoredOutputs.Head.Reused);
        var second = Execute(WorkspaceFileStore.LoadDefinition(temporary.FullName), "Consumer");
        Assert.Equal(runExecutionStatus.Failed, second.Status); Assert.Single(second.Setup); Assert.Empty(second.Results);
        Assert.True(second.Setup[0].StoredOutputs.Head.Reused);
        var folder = WorkspaceData.projectFolder(world, temporary.FullName, Named(world, "Consumer").Id);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(folder, "data"), "*.msgpack", SearchOption.AllDirectories).Length);
        Assert.False(Directory.Exists(Path.Combine(folder, "executions")));
    }

    [Fact]
    public void Ambiguous_or_corrupt_upstream_data_is_reported_instead_of_chosen_arbitrarily()
    {
        var world = Create(); var source = Named(world, "Source");
        var branch = ((run.BranchRun)Named(world, "Consumer").Runs[0]).Item;
        var entry = branch.SetupDataTypes.Single(output => output.DataType.IsSortableTests); var parameters = BranchRun.makeSetupParameterSets(branch)[0];
        var folder = WorkspaceData.projectFolder(world, temporary.FullName, source.Id);
        var store = new SortopiaFileDb(folder, source.Id);
        var output = CatalogTestHost.Invoke(false, ResultsFunctionNames.mergeSortableTests, RunParameterSets.create(source.Runs[0]).Results[0]).Head;
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
