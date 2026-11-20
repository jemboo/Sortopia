using System.IO;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;
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
        var source = Project("Source", hierarchyKind.Root, sourceSettings);
        var modelSettings = RunExecutionTests.Settings(false);
        modelSettings.resultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        modelSettings.resultsParameterDefaults = RunExecutionTests.UniformModelDefaults();
        var modelSource = Project("Model source", hierarchyKind.Root, modelSettings);
        var consumerSettings = RunExecutionTests.Settings(true);
        consumerSettings.setupFunctionName = SetupFunctionNames.evaluationSource;
        consumerSettings.setupParameterDefaults = [.. RunExecutionTests.UniformModelDefaults(), .. sourceSettings.resultsParameterDefaults];
        consumerSettings.setupDataTypes = [new() { dataType = sourceSettings.resultsDataTypes[0].dataType, sourceProjectId = source.Id, queryParameterKeys = ["SortableDataFormat"] },
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = modelSource.Id, queryParameterKeys = [] }];
        consumerSettings.resultsFunctionName = ResultsFunctionNames.sorterEval;
        consumerSettings.resultsParameterDefaults = [Binding("SorterEvalType", "V1", parameterValueType.Text), Binding("CollectSortableTests", "false", parameterValueType.Boolean)];
        var consumer = Project("Consumer", hierarchyKind.Branch, consumerSettings,
            failSecondSetup ? [new() { keyName = "SortableDataFormat", values = ["Bools", "Ints"] }] : null);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Outer", [
                WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Inner", [], [source, modelSource])], []),
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Branch, Guid.NewGuid(), "Consumers", [], [consumer])]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        world = WorkspaceFileStore.LoadDefinition(temporary.FullName);
        Completed(Execute(world, "Model source"));
        return world;
    }
    private static project Named(workspace world, string name) => world.ProjectIndex.Values.Single(value => value.ProjectName == name);
    private runExecutionReport Execute(workspace world, string name) => FileRunExecution.execute(world, temporary.FullName, Named(world, name).Runs[0], null!, Token);
    private static void Completed(runExecutionReport report) => Assert.True(report.Status == runExecutionStatus.Completed, report.Error);

    [Fact]
    public void Generated_run_file_retains_the_copied_model_capacity_and_reuses_both_inputs_on_retry()
    {
        var world = Create(); var source = Named(world, "Source"); var consumer = Named(world, "Consumer");
        var sourceParameters = RunParameterSets.create(source.Runs[0]).Results[0];
        var definition = ResultsFunctionCatalog.require(source.ResultsFunctionName);
        var testsType = sortopiaDataType.NewSortableTests("");
        var sourceKey = DataKey.create(source.Id, source.ResultsDatabaseName, false, source.ResultsFunctionName,
            definition.Version, testsType, source.ResultsQueryParameterKeys, sourceParameters, []);
        // A source stores the supplied tests independently of its construction recipe.
        var tests = sortopiaStorageData.NewSortableTests(sortableTests.NewBools(sortableBinaryTests.create(Guid.NewGuid(), 4,
            [sortableBoolArray.create([false, true, false, true], 4)])));
        ISortopiaDb sourceStore = new SortopiaFileDb(WorkspaceData.projectFolder(world, temporary.FullName, source.Id), source.Id);
        Assert.True(sourceStore.GetOrCreate(sourceKey, FuncConvert.FromFunc<Unit, FSharpResult<sortopiaStorageData, string>>(_ =>
            FSharpResult<sortopiaStorageData, string>.NewOk(tests)), Token).IsOk);
        var value = consumer.Runs[0]; var parameters = RunParameterSets.create(value).Setup.Value[0];
        Assert.Null(FunctionDefinition.tryPreviewValue("SuffixStages", SetupFunctionCatalog.require(value.SetupFunctionName.Value), parameters));
        var generated = RunParameterExecution.generate(world, temporary.FullName, value, null!, Token);
        Completed(generated.Execution);
        var folder = WorkspaceData.projectFolder(world, temporary.FullName, consumer.Id);
        var item = RunParameterFiles.loadWorkItem(folder, Assert.Single(generated.WorkItemIds));
        Assert.Equal(5, item.SetupSuffixStages.Value);
        Assert.Null(item.ResultsSuffixStages);
        var retry = RunParameterExecution.generate(world, temporary.FullName, value, null!, Token);
        Completed(retry.Execution);
        Assert.Equal(generated.WorkItemIds, retry.WorkItemIds);
        Assert.All(Assert.Single(retry.Execution.Setup).StoredOutputs, output => Assert.True(output.Reused));
    }

    // A test-only host adapter for an independent setup definition, without adding it
    // to the application catalogs. Source lookup and all storage use the real file store.
    private sealed class WorkspaceDatabase(workspace world, string root, FileRunDatabase storage) : IRunDatabase
    {
        private readonly IRunDatabase outputs = storage;
        private readonly Dictionary<storedDataReference, sortopiaStorageData> loaded = [];
        public List<(Guid ProjectId, sortopiaDataType DataType)> Reads { get; } = [];
        public RunOutputs Outputs => storage.Outputs;
        public storedDataReference Find(runDataOutput output) => outputs.Find(output);
        public storedDataReference Find(sortopiaDataType dataType)
        {
            var references = Outputs.Planned.SelectMany(output => output.Dependencies)
                .Concat(Outputs.Planned.Select(outputs.Find)).Where(reference => Equals(reference.DataType, dataType)).Distinct().ToArray();
            return references.Length == 1 ? references[0] : throw new ArgumentException("Expected one matching input or output.");
        }
        public void Save(runDataOutput output, FSharpFunc<Unit, sortopiaStorageData> factory) => outputs.Save(output, factory);
        public void Save(runDataOutput output, sortopiaStorageData value) => outputs.Save(output, value);
        public storedDataReference Find(Guid projectId, sortopiaDataType dataType, parameterSet parameters)
        {
            var entry = new setupDataTypeConfig(dataType, projectId, ListModule.Empty<string>());
            var data = WorkspaceData.loadSource(world, root, entry, parameters, Token);
            loaded[data.Reference] = data.Value;
            Reads.Add((projectId, dataType));
            return data.Reference;
        }
        public sortopiaStorageData Load(storedDataReference reference) => loaded.TryGetValue(reference, out var value) ? value : outputs.Load(reference);
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
        modelsSettings.resultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        modelsSettings.resultsDatabaseName = "Model archive";
        modelsSettings.resultsParameterDefaults = RunExecutionTests.UniformModelDefaults();
        var testsProject = Project("Tests source", hierarchyKind.Root, testsSettings);
        var modelsProject = Project("Models source", hierarchyKind.Root, modelsSettings);
        var world = workspace.create(Guid.NewGuid(), "Workspace", "Workspace", [
            WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "Sources", [], [testsProject, modelsProject])]);
        WorkspaceFileStore.CreateWorkspace(temporary.FullName, world, Token);
        world = WorkspaceFileStore.LoadDefinition(temporary.FullName);
        var publishedTests = Execute(world, "Tests source"); Completed(publishedTests);
        var publishedModels = Execute(world, "Models source"); Completed(publishedModels);

        var testsDefinition = ResultsFunctionCatalog.functions[testsProject.ResultsFunctionName];
        var modelsDefinition = ResultsFunctionCatalog.functions[modelsProject.ResultsFunctionName];
        var required = ListModule.OfSeq(testsDefinition.RequiredParameters.Concat(modelsDefinition.RequiredParameters).DistinctBy(key => key.Name));
        var parameters = parameterSet.fromBindings(RunParameterSets.create(testsProject.Runs[0]).Results.Single().ParamMap.Values.Concat(RunParameterSets.create(modelsProject.Runs[0]).Results.Single().ParamMap.Values).DistinctBy(binding => binding.KeyName));
        var sourceTypes = ListModule.OfArray(new[] { testsType, modelsType });
        var projects = MapModule.OfArray(new[] { Tuple.Create(testsType, testsProject.Id), Tuple.Create(modelsType, modelsProject.Id) });
        var copies = 0;
        var definition = FunctionDefinition.setup("1", setupParameterRequirements.NewDeclared(required),
            [new(testsType, testsDefinition.DataTypes[testsType], [testsType]), new(modelsType, modelsDefinition.DataTypes[modelsType], [modelsType])],
            FunctionParameterSupport.empty, FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(host =>
                FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(sources =>
                {
                    foreach (var dataType in sourceTypes)
                    {
                        var reference = host.Database.Find(sources[dataType], dataType, host.Parameters);
                        host.Database.Save(host.Outputs.Output(dataType, ListModule.Singleton(reference)), FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ =>
                        { copies++; return host.Database.Load(reference); }));
                    }
                    return null!;
                })));
        var consumerId = Guid.NewGuid();
        var outputFolder = Path.Combine(temporary.FullName, "Consumer");
        var plan = OutputPlan.create(consumerId, "Copied inputs", true, "copy-sources", "1", definition.Outputs,
            definition.DataTypes, parameters, ListModule.OfSeq(publishedTests.Results.Concat(publishedModels.Results).SelectMany(call => call.StoredOutputs).Select(output => output.Reference)));
        WorkspaceDatabase Database(workspace world) => new(world, temporary.FullName,
            new FileRunDatabase(new SortopiaFileDb(outputFolder, consumerId), plan, Token));
        runHost Host(WorkspaceDatabase database) => new(runExecutionPhase.Setup, parameters,
            database, database.Outputs, Token, Environment.ProcessorCount, null, null);
        var reader = Database(world);
        var setup = Host(reader);
        Assert.Contains("exactly the source projects", Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(projects.Remove(modelsType))).Message);
        Assert.Empty(reader.Reads);
        var swapped = MapModule.OfArray(new[] { Tuple.Create(testsType, modelsProject.Id), Tuple.Create(modelsType, testsProject.Id) });
        Assert.Contains("does not provide", Assert.Throws<InvalidOperationException>(() => definition.Execute.Invoke(setup).Invoke(swapped)).Message);
        Assert.Empty(setup.Outputs.Stored);

        definition.Execute.Invoke(setup).Invoke(projects);
        Assert.Equal(new[] { (testsProject.Id, testsType), (modelsProject.Id, modelsType) }, reader.Reads);
        Assert.Equal(2, copies);
        Assert.Equal(2, setup.Outputs.Stored.Length);
        Assert.All(setup.Outputs.Stored, output => { Assert.Equal(consumerId, output.Reference.ProjectId); Assert.Equal(runExecutionPhase.Setup, output.Reference.Phase); });
        var sourceTests = (sortopiaStorageData.SortableTests)reader.Load(publishedTests.Results[0].StoredOutputs.Head.Reference);
        var copiedTests = (sortopiaStorageData.SortableTests)setup.Database.Load(setup.Database.Find(setup.Outputs.Output(testsType)));
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(sourceTests.Item), Sortopia.Sorting.Inputs.SortableTests.getId(copiedTests.Item));
        var sourceModels = (sortopiaStorageData.SorterModelSet)reader.Load(publishedModels.Results[0].StoredOutputs.Head.Reference);
        var copiedModels = (sortopiaStorageData.SorterModelSet)setup.Database.Load(setup.Database.Find(setup.Outputs.Output(modelsType)));
        Assert.Equal(sourceModels.Item.SorterModels.Select(Sortopia.Models.Sorting.SorterModel.getId), copiedModels.Item.SorterModels.Select(Sortopia.Models.Sorting.SorterModel.getId));
        var repeated = Host(Database(WorkspaceFileStore.LoadDefinition(temporary.FullName)));
        definition.Execute.Invoke(repeated).Invoke(projects);
        Assert.Equal(2, copies);
        Assert.All(repeated.Outputs.Stored, output => Assert.True(output.Reused));
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
        var never = FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => throw new InvalidOperationException("Factory must not run."));

        Assert.Throws<ArgumentException>(() => setup.Database.Save(setup.Outputs.Output(resultType, empty), never));
        Assert.Throws<ArgumentException>(() => ResultsFunctionCatalog.functions[value.ResultsFunctionName].Execute.Invoke(setup));
        Assert.Throws<ArgumentException>(() => setup.Database.Find(Guid.NewGuid(), testsType, setup.Parameters));
        Assert.Throws<ArgumentException>(() => setup.Database.Find(source.Id, sortopiaDataType.NewSortableTests("other"), setup.Parameters));
        var sourceReference = published.Results[0].StoredOutputs.Head.Reference;
        // Planning resolves the exact authorized sources before execution.
        Assert.IsType<sortopiaStorageData.SortableTests>(setup.Database.Load(sourceReference));
        Assert.Throws<ArgumentException>(() => setup.Database.Load(new storedDataReference(source.Id, Guid.NewGuid(), sourceReference.QueryId, runExecutionPhase.Results, testsType)));
        Assert.Equal(sourceReference, setup.Database.Find(source.Id, testsType, setup.Parameters));
        var sourceTests = Assert.IsType<sortopiaStorageData.SortableTests>(setup.Database.Load(sourceReference));
        Assert.Empty(setup.Outputs.Stored);

        var definition = SetupFunctionCatalog.functions[branch.SetupFunctionName];
        var projects = MapModule.OfSeq(branch.SourceProjectIds.Where(pair => definition.SourceDataTypes.Contains(pair.Key))
            .Select(pair => Tuple.Create(pair.Key, pair.Value)));
        Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(MapModule.Empty<sortopiaDataType, Guid>()));
        var wrongProject = MapModule.OfArray(new[] { Tuple.Create(testsType, Guid.NewGuid()) });
        Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(setup).Invoke(wrongProject));
        Assert.Empty(setup.Outputs.Stored);
        definition.Execute.Invoke(setup).Invoke(projects);
        Assert.Equal(2, setup.Outputs.Stored.Length);
        Assert.All(setup.Outputs.Stored, output => { Assert.Equal(runExecutionPhase.Setup, output.Reference.Phase); Assert.Equal(consumer.Id, output.Reference.ProjectId); });
        Assert.Throws<ArgumentException>(() => setup.Database.Find(testsType));
        Assert.NotEqual(sourceReference, setup.Database.Find(setup.Outputs.Output(testsType)));

        Assert.Throws<ArgumentException>(() => factory.ForResults(sets.Results[0], ListModule.Empty<storedOutput>()));
        var results = factory.ForResults(sets.Results[0], setup.Outputs.Stored);
        Assert.Empty(results.Outputs.Stored);
        Assert.Equal(setup.Database.Find(setup.Outputs.Output(testsType)), results.Database.Find(testsType));
        var savedTests = Assert.IsType<sortopiaStorageData.SortableTests>(results.Database.Load(results.Database.Find(testsType)));
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(sourceTests.Item), Sortopia.Sorting.Inputs.SortableTests.getId(savedTests.Item));
        Assert.Throws<ArgumentException>(() => results.Database.Save(results.Outputs.Output(testsType, empty), never));
        Assert.Throws<ArgumentException>(() => results.Database.Save(setup.Outputs.Output(testsType), savedTests));
        Assert.Throws<ArgumentException>(() => results.Database.Find(sortopiaDataType.NewSortableTests("other")));
        Assert.Throws<InvalidOperationException>(() => results.Database.Find(source.Id, testsType, setup.Parameters));
        Assert.Throws<ArgumentException>(() => results.Database.Load(sourceReference));
        Assert.Throws<ArgumentException>(() => results.Database.Save(results.Outputs.Output(resultType, ListModule.Singleton(sourceReference)), never));
        ResultsFunctionCatalog.functions[value.ResultsFunctionName].Execute.Invoke(results);
        var output = Assert.Single(results.Outputs.Stored);
        Assert.Equal(runExecutionPhase.Results, output.Reference.Phase); Assert.Equal(consumer.Id, output.Reference.ProjectId);
        var evaluation = Assert.IsType<sortopiaStorageData.SorterSetEval>(results.Database.Load(results.Database.Find(resultType)));
        Assert.Throws<ArgumentException>(() => setup.Database.Load(output.Reference));
        Assert.Throws<ArgumentException>(() => setup.Database.Save(results.Outputs.Output(resultType), evaluation));
        Assert.Equal(Sortopia.Sorting.Inputs.SortableTests.getId(savedTests.Item), evaluation.Item.SorterTestId);
        Assert.Equal(5, Directory.GetFiles(temporary.FullName, "*.msgpack", SearchOption.AllDirectories)
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
        Assert.Throws<InvalidOperationException>(() => results.Database.Load(results.Database.Find(sortopiaDataType.NewSortableTests(""))));
        Assert.Throws<ArgumentException>(() => SetupFunctions.standardTests.Execute.Invoke(results).Invoke(MapModule.Empty<sortopiaDataType, Guid>()));
        var unsaved = RunDto.toDomain(source, new runDto
        {
            schemaVersion = WorkspaceDtoSchema.version, id = Guid.NewGuid(), parentProjectId = source.Id, runName = "Unsaved", runKind = "root",
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
        Assert.Equal(5, dataFiles.Count); // Two source results, two setup outputs and one consumer evaluation.
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
        var factory = FuncConvert.FromFunc<Unit, FSharpResult<sortopiaStorageData, string>>(_ => FSharpResult<sortopiaStorageData, string>.NewOk(output));
        dataKey Key(bool withDependency) => DataKey.create(source.Id, source.ResultsDatabaseName, false, source.ResultsFunctionName, "1", entry.DataType,
            source.ResultsQueryParameterKeys, parameters, withDependency ? ListModule.OfArray(new[] { new storedDataReference(source.Id, Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Setup, entry.DataType) }) : ListModule.Empty<storedDataReference>());
        var firstKey = Key(false); var secondKey = Key(true);
        Assert.True(((ISortopiaDb)store).GetOrCreate(firstKey, factory, Token).IsOk);
        Assert.True(((ISortopiaDb)store).GetOrCreate(secondKey, factory, Token).IsOk);
        Assert.Contains("ambiguous", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token)).Message);
        var bytes = File.ReadAllBytes(store.GetPath(firstKey.Reference)); bytes[^1] ^= 1; File.WriteAllBytes(store.GetPath(firstKey.Reference), bytes);
        Assert.Contains("checksum", Assert.Throws<InvalidOperationException>(() => WorkspaceData.loadSource(world, temporary.FullName, entry, parameters, Token)).Message);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
