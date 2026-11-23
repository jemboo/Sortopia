using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Copy_scope_requires_the_model_source_as_well_as_the_tests_source()
    {
        var (world, target) = Create(true);
        var document = WorkspaceDocumentModule.clone(world);
        var saved = document.Modules.Single(module => module.Projects[0].Definition.id == target.Definition.id).Projects[0];
        saved.Definition.configuration.setupDataTypes.Single(output => output.dataType.kind == "SorterModelSet").sourceProjectId = Guid.NewGuid();
        var error = Assert.Throws<InvalidOperationException>(() => WorkspaceRunDocument.resolveByRunId(document, target.Runs[0].id));
        Assert.Contains("Required project", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scoped_execution_ignores_unfinished_siblings_and_source_runs_or_setup(bool immediate)
    {
        var (world, models, tests, consumer, _, _) = EvaluationCopyWorkspace(8);
        var document = WorkspaceDocumentModule.clone(world);
        foreach (var module in document.Modules.Where(m => m.Projects[0].Definition.id != consumer.Definition.id))
        {
            module.Definition.localOverrides.resultsFunctionName = "unfinished-module-function";
            var source = module.Projects[0];
            source.Runs[0].resultsParameterSpans[0].values = ["unfinished-input"];
            // Reading materialized results does not need the producer's setup contract.
            source.Definition.configuration.setupFunctionName = "unavailable-old-setup";
            source.Definition.configuration.setupParameterDefaults = null!;
        }
        var parent = new WorkspaceDocumentIndex(document).FindProject(consumer.Definition.id).Document;
        var unfinished = RunDto.fromDomain(consumer.ResolvedRuns()[0]);
        unfinished.id = Guid.NewGuid(); unfinished.runName = "Unfinished sibling";
        unfinished.resultsParameterSpans = [new() { keyName = "UnknownQueryKey", values = ["unfinished"] }];
        document = WorkspaceNavigation.replaceProject(WorkspaceDocumentModule.withRuns(parent, [.. parent.Runs, unfinished]), document);
        Assert.ThrowsAny<Exception>(() => WorkspaceRunDocument.resolveByRunId(document, unfinished.id));
        Assert.ThrowsAny<Exception>(() => WorkspaceRunDocument.resolveByRunId(document, models.Runs[0].id));
        var scope = WorkspaceRunDocument.resolveByRunId(document, consumer.Runs[0].id);
        Assert.Equal(3, scope.ProjectLocations.Length);
        runExecutionReport execution;
        if (immediate)
        {
            execution = FileRunExecution.executeInScope(scope, Root, null!, Token);
            Completed(execution); Assert.NotEmpty(execution.Results);
        }
        else
        {
            var generated = RunParameterExecution.generateInScope(scope, Root, null!, Token);
            execution = generated.Execution;
            Completed(execution); Assert.Single(generated.WorkItemIds);
        }
        var copied = Assert.Single(execution.Setup).StoredOutputs;
        Assert.Equal(2, copied.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scoped_generation_rejects_required_source_contract_errors(bool missingType)
    {
        var (world, models, _, consumer, _, _) = EvaluationCopyWorkspace(8);
        var document = WorkspaceDocumentModule.clone(world);
        var source = document.Modules.Single(module => module.Projects[0].Definition.id == models.Definition.id).Projects[0];
        if (missingType) source.Definition.configuration.resultsDataTypes = [];
        else source.Definition.configuration.resultsDataTypes[0].queryParameterKeys = ["UnknownQueryKey"];
        var error = Assert.ThrowsAny<Exception>(() => WorkspaceRunDocument.resolveByRunId(document, consumer.Runs[0].id));
        Assert.Contains(missingType ? "does not provide" : "UnknownQueryKey", error.Message);
    }

    [Fact]
    public void Cli_generation_ignores_invalid_module_defaults()
    {
        var (world, _, _, consumer, _, _) = EvaluationCopyWorkspace(8);
        var saved = Sortopia.Workspaces.Persistence.WorkspaceFileStore.LoadWorkspace(Root);
        var edit = Sortopia.Core.Serialization.MessagePackSetup.deserialize<WorkspaceDocument>(Sortopia.Core.Serialization.MessagePackSetup.serialize(saved.CreateEditDocument()));
        edit.Modules[0].Definition.localOverrides.resultsFunctionName = "unfinished-module-function";
        Sortopia.Workspaces.Persistence.WorkspaceFileStore.SaveDocument(edit, saved, Token);
        Assert.Equal(0, Sortopia.Cli.Program.main(["generate-parameters", "--workspace", Root, "--run", consumer.Runs[0].id.ToString()]));
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(consumer.Definition.id).RelativeFolder);
        Assert.Single(Directory.GetFiles(Path.Combine(folder, "runParameters"), "*.msgpack"));
    }
}
