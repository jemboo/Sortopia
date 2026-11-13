using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;

namespace Sortopia.WPF.Tests;

/// <summary>In-memory database host for catalog and execution-loop tests.</summary>
internal sealed class CatalogTestHost(run? value = null, CancellationToken token = default) : IRunHostFactory
{
    private readonly Guid projectId = value?.ParentProjectId ?? Guid.NewGuid();
    private readonly Guid sourceId = Guid.NewGuid();
    private readonly Dictionary<storedDataReference, functionData> data = [];
    public run Run => value ?? throw new InvalidOperationException("This host is for an isolated catalog invocation.");
    public CancellationToken CancellationToken => token;
    public functionData Load(storedDataReference reference) => data[reference];

    private sealed class Database(CatalogTestHost owner, bool setup, string name, parameterSet parameters,
        FSharpMap<sortopiaDataType, FSharpList<string>>? contract, IEnumerable<storedOutput>? inputs = null) : IRunDatabase
    {
        private readonly List<storedOutput> saved = inputs?.ToList() ?? [];
        public FSharpList<storedOutput> StoredOutputs => ListModule.OfSeq(saved);
        public storedDataReference Reference(sortopiaDataType type) => saved.Single(output => Equals(output.Reference.DataType, type)).Reference;
        public functionData Load(sortopiaDataType type) => owner.Load(Reference(type));
        public void SaveIfMissing(sortopiaDataType type, FSharpList<storedDataReference> dependencies, FSharpFunc<Unit, functionData> factory)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            if (contract is null) throw new InvalidOperationException("Read-only database.");
            if (saved.Any(output => Equals(output.Reference.DataType, type))) throw new InvalidOperationException("Output already saved.");
            var keys = contract[type];
            var version = setup ? SetupFunctionCatalog.require(name).Version : ResultsFunctionCatalog.require(name).Version;
            var key = DataKey.create(owner.projectId, setup ? "Setup" : "Results", setup, name, version, type, keys, parameters, dependencies);
            var reused = owner.data.ContainsKey(key.Reference);
            if (!reused)
            {
                var payload = factory.Invoke(null!);
                if (FunctionData.kind(payload) != SortopiaDataType.kindName(type)) throw new InvalidOperationException("Payload type mismatch.");
                owner.data.Add(key.Reference, payload);
            }
            saved.Add(new(key.Reference, reused));
        }
    }

    private sealed class Sources(CatalogTestHost owner, IEnumerable<storedOutput> inputs) : ISourceResults
    {
        public storedDataReference Find(Guid project, sortopiaDataType type, parameterSet parameters) =>
            inputs.Single(output => output.Reference.ProjectId == project && Equals(output.Reference.DataType, type)).Reference;
        public functionData Load(storedDataReference reference) => owner.Load(reference);
    }

    private runHost Context(bool setup, string name, parameterSet parameters, IEnumerable<storedOutput> inputs)
    {
        var contract = setup ? SetupFunctionCatalog.functions[name].DataTypes : ResultsFunctionCatalog.functions[name].DataTypes;
        var target = new Database(this, setup, name, parameters, contract);
        var reader = new Database(this, true, "read", parameters, null, inputs);
        return new(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameters,
            setup ? target : reader, setup ? new Database(this, false, "read", parameters, null) : target,
            new Sources(this, inputs), token, Environment.ProcessorCount);
    }
    public runHost ForSetup(parameterSet parameters) => Context(true, Run.SetupFunctionName.Value, parameters, []);
    public runHost ForResults(parameterSet parameters, FSharpList<storedOutput> outputs) => Context(false, Run.ResultsFunctionName, parameters, outputs);

    internal static FSharpList<functionData> Invoke(bool setup, string name, parameterSet parameters, params functionData[] inputs)
    {
        var owner = new CatalogTestHost();
        var references = inputs.Select(input => {
            var reference = new storedDataReference(setup ? owner.sourceId : owner.projectId, Guid.NewGuid(), Guid.NewGuid(), !setup, SortopiaDataType.create(FunctionData.kind(input), ""));
            owner.data.Add(reference, input); return new storedOutput(reference, false);
        }).ToArray();
        var host = owner.Context(setup, name, parameters, references);
        if (setup)
        {
            var definition = SetupFunctionCatalog.functions[name];
            var projects = MapModule.OfSeq(definition.SourceDataTypes.Select(type => Tuple.Create(type, owner.sourceId)));
            definition.Execute.Invoke(host).Invoke(projects);
        }
        else ResultsFunctionCatalog.functions[name].Execute.Invoke(host);
        var database = setup ? host.SetupDb : host.ResultsDb;
        return ListModule.OfSeq(database.StoredOutputs.Select(output => owner.Load(output.Reference)));
    }
}
