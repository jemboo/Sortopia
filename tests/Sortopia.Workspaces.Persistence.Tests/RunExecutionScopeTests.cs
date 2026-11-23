using Sortopia.Runs;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class RunExecutionScopeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resolving_a_draft_keeps_editing_state_and_sibling_inputs_in_the_document(bool setup)
    {
        var document = WorkspacePersistenceTests.Sample(setup);
        var saved = document.Modules[0].Projects[0];
        var configuration = saved.Configuration();
        var local = ProjectLocalSettingsDto.fromDomain(ProjectResolution.local(configuration));
        saved.Definition.state = projectStateDto.NewDraft(local, SettingsDto.fromDomain(configuration), saved.Definition.inheritedFrom);
        var sibling = WorkspaceDocumentModule.cloneProject(saved).Runs[0];
        sibling.id = Guid.NewGuid(); sibling.runName = "Unfinished";
        sibling.resultsParameterSpans[0].values = ["unfinished"];
        document = WorkspaceNavigation.replaceProject(WorkspaceDocumentModule.withRuns(saved, [.. saved.Runs, sibling]), document);
        var before = WorkspaceDocumentModule.clone(document);

        var scope = WorkspaceRunDocument.resolveByRunId(document, saved.Runs[0].id);

        Assert.Equal(typeof(projectSnapshot), typeof(RunExecutionScope).GetProperty("Parent")!.PropertyType);
        Assert.Null(typeof(projectSnapshot).GetProperty("Runs"));
        Assert.Null(typeof(projectSnapshot).GetProperty("LocalSettings"));
        Assert.Equal(configuration, scope.Parent.Configuration);
        Assert.Same(scope.Run.Parent, scope.Parent);
        Assert.Equal(saved.Runs[0].id, scope.Run.Id);
        Assert.True(WorkspaceDocumentModule.sameContent(before, document));
        Assert.False(new WorkspaceDocumentIndex(document).FindProject(saved.Definition.id).Document.Definition.isSealed);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("configuration")]
    public void Scope_rejects_a_parent_location_that_does_not_match_the_run_snapshot(string change)
    {
        var document = WorkspacePersistenceTests.Sample();
        var scope = WorkspaceRunDocument.resolveByRunId(document, document.Modules[0].Projects[0].Runs[0].id);
        var settings = SettingsDto.fromDomain(scope.Parent.Configuration);
        if (change == "configuration") settings.resultsDatabaseName = "Different database";
        var parent = ProjectSnapshot.create(change == "id" ? Guid.NewGuid() : scope.Parent.Id,
            change == "name" ? "Different name" : scope.Parent.ProjectName, SettingsDto.toDomain(hierarchyKind.Root, settings));
        var locations = new[] { new runProjectLocation(parent.ResultsSnapshot, "sorting/search") };
        if (change == "id")
            Assert.Contains("not found", Assert.Throws<InvalidOperationException>(() => RunExecutionScopeModule.create(scope.Run, locations)).Message);
        else
            Assert.Equal("project", Assert.Throws<ArgumentException>(() => RunExecutionScopeModule.create(scope.Run, locations)).ParamName);
    }
}
