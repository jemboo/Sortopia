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
    private readonly Dictionary<storedDataReference, sortopiaStorageData> data = [];
    public run Run => value ?? throw new InvalidOperationException("This host is for an isolated catalog invocation.");
    public CancellationToken CancellationToken => token;
    public sortopiaStorageData Load(storedDataReference reference) => data[reference];

    private sealed class Database(CatalogTestHost owner, bool setup, string name, parameterSet parameters,
        FSharpMap<sortopiaDataType, FSharpList<string>> contract, IEnumerable<storedOutput> inputs) : IRunDatabase
    {
        private readonly List<storedOutput> saved = [];
        private FSharpList<runDataOutput> Plan => ListModule.OfSeq(contract.Select(entry =>
        {
            var dependencies = setup ? SetupFunctionCatalog.require(name).OutputDependencies : ResultsFunctionCatalog.require(name).OutputDependencies;
            return RunDataOutput.create(entry.Key, entry.Value, parameters, ListModule.OfSeq(inputs
                .Where(input => dependencies[entry.Key].Contains(input.Reference.DataType)).Select(input => input.Reference)));
        }));
        public RunOutputs Outputs => new(Plan, FuncConvert.FromFunc<Unit, FSharpList<storedOutput>>(_ => ListModule.OfSeq(saved)));
        public storedDataReference Find(Guid projectId, sortopiaDataType dataType, parameterSet parameters)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            if (!setup) throw new InvalidOperationException("Source lookup is available only during setup.");
            return inputs.Single(output => output.Reference.ProjectId == projectId && Equals(output.Reference.DataType, dataType)).Reference;
        }
        public storedDataReference Find(sortopiaDataType dataType)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            var references = inputs.Select(input => input.Reference).Concat(Plan.Select(Find))
                .Where(reference => Equals(reference.DataType, dataType)).Distinct().ToArray();
            return references.Length == 1 ? references[0] : throw new ArgumentException("Expected one matching input or output.");
        }
        public storedDataReference Find(runDataOutput output)
        {
            if (!Plan.Contains(output)) throw new ArgumentException("Unplanned output.");
            var version = setup ? SetupFunctionCatalog.require(name).Version : ResultsFunctionCatalog.require(name).Version;
            return DataKey.create(owner.projectId, setup ? "Setup" : "Results", setup, name, version, output.DataType,
                ListModule.OfSeq(output.QueryParameters.ParamMap.Keys), output.QueryParameters, output.Dependencies).Reference;
        }
        public sortopiaStorageData Load(storedDataReference reference)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            if (!inputs.Any(input => Equals(input.Reference, reference)) && !Plan.Any(output => Equals(Find(output), reference)))
                throw new ArgumentException("Undeclared input or output.");
            return owner.Load(reference);
        }
        public void Save(runDataOutput output, FSharpFunc<Unit, sortopiaStorageData> factory)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            var reference = Find(output);
            var reused = owner.data.ContainsKey(reference);
            if (!reused)
            {
                var payload = factory.Invoke(null!);
                if (SortopiaStorageData.kind(payload) != SortopiaDataType.kindName(output.DataType)) throw new InvalidOperationException("Payload type mismatch.");
                owner.data.Add(reference, payload);
            }
            if (!saved.Any(value => Equals(value.Reference, reference))) saved.Add(new(reference, reused));
        }
        public void Save(runDataOutput output, sortopiaStorageData value)
        {
            owner.CancellationToken.ThrowIfCancellationRequested();
            var reference = Find(output);
            if (SortopiaStorageData.kind(value) != SortopiaDataType.kindName(output.DataType)) throw new InvalidOperationException("Payload type mismatch.");
            if (owner.data.TryGetValue(reference, out var existing) && !Equals(existing, value)) throw new InvalidOperationException("Output differs from replay.");
            Save(output, FuncConvert.FromFunc<Unit, sortopiaStorageData>(_ => value));
        }
    }

    private runHost Context(bool setup, string name, parameterSet parameters, IEnumerable<storedOutput> inputs)
    {
        if (setup) FunctionValidation.validate(SetupFunctionCatalog.functions[name], parameters);
        else FunctionValidation.validate(ResultsFunctionCatalog.functions[name], parameters);
        var contract = setup ? SetupFunctionCatalog.functions[name].DataTypes : ResultsFunctionCatalog.functions[name].DataTypes;
        var target = new Database(this, setup, name, parameters, contract, inputs);
        return new(setup ? runExecutionPhase.Setup : runExecutionPhase.Results, parameters,
            target, target.Outputs, token, Environment.ProcessorCount, null, null);
    }
    public runHost ForSetup(parameterSet parameters) => Context(true, Run.SetupFunctionName.Value, parameters, []);
    public runHost ForResults(parameterSet parameters, FSharpList<storedOutput> outputs) => Context(false, Run.ResultsFunctionName, parameters, outputs);

    internal static FSharpList<sortopiaStorageData> Invoke(bool setup, string name, parameterSet parameters, params sortopiaStorageData[] inputs)
    {
        var owner = new CatalogTestHost();
        var references = inputs.Select(input => {
            var reference = new storedDataReference(setup ? owner.sourceId : owner.projectId, Guid.NewGuid(), Guid.NewGuid(), setup ? runExecutionPhase.Results : runExecutionPhase.Setup, SortopiaDataType.create(SortopiaStorageData.kind(input), ""));
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
        return ListModule.OfSeq(host.Outputs.Stored.Select(output => owner.Load(output.Reference)));
    }
}
