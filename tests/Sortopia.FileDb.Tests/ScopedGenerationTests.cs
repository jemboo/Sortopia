using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Scope_ignores_source_assignments_for_outputs_generated_from_parameters()
    {
        var (world, target) = Create(true);
        var document = WorkspaceDocumentModule.fromDomain(world);
        var saved = document.Modules.Single(module => module.Projects[0].Definition.id == target.Id).Projects[0];
        saved.Definition.configuration.setupDataTypes.Single(output => output.dataType.kind == "SorterModelSet").sourceProjectId = Guid.NewGuid();
        var scope = WorkspaceRunDocument.resolveByRunId(document, target.Runs[0].Id);
        Assert.Equal(2, scope.ProjectLocations.Length);
        Completed(RunParameterExecution.generateInScope(scope, Root, null!, Token).Execution);
    }

    [Fact]
    public void Scoped_generation_uses_source_results_without_parsing_source_runs_or_setup()
    {
        var (world, models, tests, consumer, _, _) = EvaluationCopyWorkspace(8);
        var document = WorkspaceDocumentModule.fromDomain(world);
        foreach (var module in document.Modules.Where(m => m.Projects[0].Definition.id != consumer.Id))
        {
            module.Definition.localOverrides.resultsFunctionName = "unfinished-module-function";
            var source = module.Projects[0];
            source.Runs[0].resultsParameterSpans[0].values = ["unfinished-input"];
            // Reading materialized results does not need the producer's setup contract.
            source.Definition.configuration.setupFunctionName = "unavailable-old-setup";
            source.Definition.configuration.setupParameterDefaults = null!;
        }
        Assert.ThrowsAny<Exception>(() => WorkspaceDocumentModule.toDomain(document));
        var scope = WorkspaceRunDocument.resolveByRunId(document, consumer.Runs[0].Id);
        Assert.Equal(3, scope.ProjectLocations.Length);
        var generated = RunParameterExecution.generateInScope(scope, Root, null!, Token);
        Completed(generated.Execution); Assert.Single(generated.WorkItemIds);
        var copied = Assert.Single(generated.Execution.Setup).StoredOutputs;
        Assert.Equal(2, copied.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scoped_generation_rejects_required_source_contract_errors(bool missingType)
    {
        var (world, models, _, consumer, _, _) = EvaluationCopyWorkspace(8);
        var document = WorkspaceDocumentModule.fromDomain(world);
        var source = document.Modules.Single(module => module.Projects[0].Definition.id == models.Id).Projects[0];
        if (missingType) source.Definition.configuration.resultsDataTypes = [];
        else source.Definition.configuration.resultsDataTypes[0].queryParameterKeys = ["UnknownQueryKey"];
        var error = Assert.ThrowsAny<Exception>(() => WorkspaceRunDocument.resolveByRunId(document, consumer.Runs[0].Id));
        Assert.Contains(missingType ? "does not provide" : "UnknownQueryKey", error.Message);
    }

    [Fact]
    public void Cli_generation_ignores_invalid_module_defaults()
    {
        var (world, _, _, consumer, _, _) = EvaluationCopyWorkspace(8);
        var saved = Sortopia.Workspaces.Persistence.WorkspaceFileStore.LoadDocument(Root);
        var edit = Sortopia.Core.Serialization.MessagePackSetup.deserialize<WorkspaceDocument>(Sortopia.Core.Serialization.MessagePackSetup.serialize(saved));
        edit.Modules[0].Definition.localOverrides.resultsFunctionName = "unfinished-module-function";
        Sortopia.Workspaces.Persistence.WorkspaceFileStore.SaveDocument(Root, edit, saved, Token);
        Assert.Equal(0, Sortopia.Cli.Program.main(["generate-parameters", "--workspace", Root, "--run", consumer.Runs[0].Id.ToString()]));
        var folder = WorkspaceData.projectFolder(world, Root, consumer.Id);
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
    }
}
