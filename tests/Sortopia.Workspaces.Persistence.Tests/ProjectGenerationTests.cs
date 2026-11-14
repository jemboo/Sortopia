using Sortopia.Core.Serialization;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class ProjectGenerationTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-generation-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static WorkspaceDocument Clone(WorkspaceDocument value) => MessagePackSetup.deserialize<WorkspaceDocument>(MessagePackSetup.serialize(value));
    private WorkspaceDocument Create(bool branch = false)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample(branch)), Token).Definition;
        return WorkspaceFileStore.ResumeProjectEditing(Root, saved, saved.Modules[0].Projects[0].Definition.id, Token).Definition;
    }
    private string ProjectFolder => Path.Combine(Root, "sorting", "search");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Generation_seals_only_after_validation_and_keeps_incomplete_sibling_inputs(bool branch)
    {
        var saved = Create(branch);
        var selected = saved.Modules[0].Projects[0].Runs[0];
        var sibling = MessagePackSetup.deserialize<runDto>(MessagePackSetup.serialize(selected));
        sibling.id = Guid.NewGuid(); sibling.runName = "Unfinished"; sibling.resultsParameterSpans[0].values = ["unfinished"];
        var p = saved.Modules[0].Projects[0];
        p.Definition.runs = [selected.id, sibling.id];
        var edit = new WorkspaceDocument(saved.Definition, [new ModuleDocument(saved.Modules[0].Definition, [], [new ProjectDocument(p.Definition, [selected, sibling])])]);
        saved = WorkspaceFileStore.SaveDocument(Root, edit, saved, Token).Definition;
        Assert.False(saved.Modules[0].Projects[0].Definition.isSealed);
        var generated = WorkspaceRunGeneration.generate(Root, saved, selected.id, null!, Token);
        Assert.Equal(runExecutionStatus.Completed, generated.Report.Execution.Status);
        Assert.NotEmpty(generated.Report.WorkItemIds);
        var restored = WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0];
        Assert.True(restored.Definition.isSealed); Assert.Equal(2, restored.Runs.Length);
        Assert.Equal("unfinished", restored.Runs[1].resultsParameterSpans[0].values[0]);
        Assert.Contains("generated", WorkspaceFileStore.ResumeProjectEditingReason(Root, generated.Document, restored.Definition.id));
        Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.ResumeProjectEditing(Root, generated.Document, restored.Definition.id, Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Validation_or_cancellation_before_writes_does_not_seal(bool cancel)
    {
        var saved = Create();
        var selected = saved.Modules[0].Projects[0].Runs[0];
        if (!cancel)
        {
            var edit = Clone(saved); edit.Modules[0].Projects[0].Runs[0].resultsParameterSpans[0].values = ["unfinished"];
            saved = WorkspaceFileStore.SaveDocument(Root, edit, saved, Token).Definition;
        }
        var before = File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ProjectFolder, "project"));
        using var cancellation = new CancellationTokenSource(); if (cancel) cancellation.Cancel();
        try
        {
            var result = WorkspaceRunGeneration.generate(Root, saved, selected.id, null!, cancellation.Token);
            Assert.NotEqual(runExecutionStatus.Completed, result.Report.Execution.Status);
        }
        catch (ArgumentException) when (!cancel) { }
        Assert.False(WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Definition.isSealed);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceDefinitionFiles.Require(ProjectFolder, "project")));
        Assert.False(Directory.Exists(Path.Combine(ProjectFolder, "runParameters")));
        Assert.False(Directory.Exists(Path.Combine(ProjectFolder, "data")));
    }

    [Fact]
    public void Failed_sealing_write_generates_no_artifacts_and_can_be_retried()
    {
        var saved = Create(); var id = saved.Modules[0].Projects[0].Runs[0].id;
        using (var locked = new FileStream(WorkspaceDefinitionFiles.Require(ProjectFolder, "project"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = WorkspaceRunGeneration.generate(Root, saved, id, null!, Token);
            Assert.Equal(runExecutionStatus.Failed, result.Report.Execution.Status);
            Assert.False(result.Document.Modules[0].Projects[0].Definition.isSealed);
            Assert.False(Directory.Exists(Path.Combine(ProjectFolder, "runParameters")));
        }
        Assert.Equal(runExecutionStatus.Completed, WorkspaceRunGeneration.generate(Root, saved, id, null!, Token).Report.Execution.Status);
    }

    [Theory]
    [InlineData("data/setup/SortableTests/test.msgpack")]
    [InlineData("runParameters/test.msgpack")]
    public void Existing_generated_files_prevent_resume_even_if_unreadable(string relative)
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample()), Token).Definition;
        var path = Path.Combine(ProjectFolder, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "unknown");
        Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.ResumeProjectEditing(Root, saved, saved.Modules[0].Projects[0].Definition.id, Token));
        Assert.True(WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Definition.isSealed);
    }

    [Fact]
    public void Sealed_consumers_prevent_resuming_their_source_project()
    {
        var document = WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample());
        var source = document.Modules[0].Projects[0];
        var consumer = WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample(true)).Modules[0];
        consumer.Definition.moduleName = "Consumer"; consumer.Definition.folderName = "consumer";
        consumer.Projects[0].Definition.configuration.setupDataTypes[0].sourceProjectId = source.Definition.id;
        document.Definition.modules = [document.Definition.modules[0], new() { id = consumer.Definition.id, folderName = "consumer" }];
        document = new(document.Definition, [document.Modules[0], consumer]);
        var saved = WorkspaceFileStore.CreateDocument(Root, document, Token).Definition;
        Assert.Contains("use this source", WorkspaceFileStore.ResumeProjectEditingReason(Root, saved, source.Definition.id));
        Assert.Throws<InvalidOperationException>(() => WorkspaceFileStore.ResumeProjectEditing(Root, saved, source.Definition.id, Token));
    }

    [Fact]
    public void Resuming_recaptures_current_module_functions_instead_of_the_old_sealed_function()
    {
        var saved = WorkspaceFileStore.CreateDocument(Root, WorkspaceDocumentModule.fromDomain(WorkspacePersistenceTests.Sample()), Token).Definition;
        var edit = Clone(saved);
        edit.Modules[0].Definition.localOverrides.resultsFunctionName = ResultsFunctionNames.mergeSortableTests;
        saved = WorkspaceFileStore.SaveDocument(Root, edit, saved, Token).Definition;
        var resumed = WorkspaceFileStore.ResumeProjectEditing(Root, saved, saved.Modules[0].Projects[0].Definition.id, Token).Definition;
        Assert.Equal(ResultsFunctionNames.mergeSortableTests, resumed.Modules[0].Projects[0].Definition.inheritedDefaults.resultsFunctionName);
    }
    public void Dispose() => temporary.Delete(true);
}
