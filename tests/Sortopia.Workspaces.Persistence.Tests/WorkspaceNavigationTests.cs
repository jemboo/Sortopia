using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.Workspaces.Persistence.Tests;

public sealed class WorkspaceNavigationTests
{
    internal static WorkspaceDocument Nested()
    {
        var value = WorkspacePersistenceTests.Sample();
        var ancestor = WorkspaceFixtures.Module(hierarchyKind.Root, Guid.NewGuid(), "Ancestor", [], []);
        ancestor = WorkspaceDocumentModule.withModuleChildren(ancestor, value.Modules, []);
        var sibling = WorkspacePersistenceTests.Sample(true).Modules[0];
        sibling.Definition.moduleName = "Other";
        return WorkspaceDocumentModule.withModules(value, [ancestor, sibling]);
    }

    [Fact]
    public void Navigation_finds_parents_paths_and_runs_without_parsing_draft_values()
    {
        var value = Nested();
        var project = value.Modules[0].Modules[0].Projects[0];
        project.Runs[0].resultsParameterSpans[0].values = ["still typing"];
        var index = new WorkspaceDocumentIndex(value);
        Assert.Equal(new[] { "Ancestor", "sorting", "Other" }, index.Modules.Select(entry => entry.Document.Definition.moduleName));
        var entry = index.FindProject(project.Definition.id);
        Assert.Same(value.Modules[0].Modules[0], entry.Parent);
        Assert.Equal(new[] { "Ancestor", "sorting" }, entry.ModulePath.Select(module => module.moduleName));
        Assert.Equal(Path.Combine("Ancestor", "sorting", "search"), entry.RelativeFolder);
        var run = index.TryFindRun(project.Runs[0].id).Value;
        Assert.Same(entry, run.Parent);
        Assert.Equal(Path.Combine(entry.RelativeFolder, "runs", WorkspaceDefinitionFiles.Name(run.Document.id)), run.RelativePath);
        Assert.Equal(value.Modules[0].Definition.id, index.TryFindModule(entry.Parent.Definition.id).Value.ParentId.Value);
        Assert.Null(index.TryFindProject(Guid.NewGuid()));
    }

    [Fact]
    public void Replacing_a_project_refreshes_navigation_without_mutating_the_original()
    {
        var value = Nested(); var original = value.Modules[0].Modules[0].Projects[0];
        var before = new WorkspaceDocumentIndex(value);
        var edited = WorkspaceDocumentModule.cloneProject(original);
        edited.Definition.projectName = "Renamed";
        var second = Sortopia.Core.Serialization.MessagePackSetup.deserialize<runDto>(Sortopia.Core.Serialization.MessagePackSetup.serialize(edited.Runs[0]));
        second.id = Guid.NewGuid(); second.runName = "Second";
        // Child collections alone determine membership and order.
        edited = new(edited.Definition, [second, edited.Runs[0]]);
        var result = WorkspaceNavigation.replaceProject(edited, value);
        WorkspaceDocumentModule.validate(result);
        var after = new WorkspaceDocumentIndex(result);
        Assert.EndsWith("search", before.FindProject(original.Definition.id).RelativeFolder);
        Assert.EndsWith("Renamed", after.FindProject(original.Definition.id).RelativeFolder);
        Assert.Equal("search", value.Modules[0].Modules[0].Projects[0].Definition.projectName);
        Assert.Equal("Renamed", result.Modules[0].Modules[0].Projects[0].Definition.projectName);
        Assert.Equal(new[] { second.id, original.Runs[0].id }, after.FindProject(original.Definition.id).Document.Runs.Select(run => run.id));
        Assert.Null(before.TryFindRun(second.id));
        Assert.Equal(original.Definition.id, after.TryFindRun(second.id).Value.Parent.Document.Definition.id);
        var missing = WorkspaceDocumentModule.cloneProject(edited);
        missing.Definition.id = Guid.NewGuid();
        Assert.Throws<InvalidOperationException>(() => WorkspaceNavigation.replaceProject(missing, result));
    }
}
