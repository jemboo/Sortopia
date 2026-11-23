using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceSourceOverridesTests
{
    private static WorkspaceTreeNode Add(WorkspaceTreeNode parent, WorkspaceEntityKind kind, string name)
    {
        var node = new WorkspaceTreeNode(kind, name, family: hierarchyKind.Branch); parent.Children.Add(node); return node;
    }
    private static (WorkspaceTreeNode Root, WorkspaceTreeNode Tests, WorkspaceTreeNode Models, WorkspaceTreeNode Project, WorkspaceTreeNode Run) Tree()
    {
        var root = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, "Workspace", workspaceFolderName: "Workspace");
        var tests = Add(root, WorkspaceEntityKind.Module, "Tests owner");
        tests.Editor!.SetupFunctionName = SetupFunctionNames.evaluationSource;
        tests.Editor.ResultsFunctionName = ResultsFunctionNames.sorterEval;
        var models = Add(tests, WorkspaceEntityKind.Module, "Models owner");
        var project = Add(models, WorkspaceEntityKind.Project, "Project");
        var run = Add(project, WorkspaceEntityKind.Run, "Run");
        return (root, tests, models, project, run);
    }
    private static WorkspaceSetupDataTypeEntry Source(WorkspaceTreeNode node, string kind) => node.Editor!.SetupDataTypes.Single(row => row.DataType.Kind == kind);
    private static Guid Effective(WorkspaceTreeNode node, string kind) => node.Editor!.ToProjectSettings().setupDataTypes.Single(row => row.dataType.kind == kind).sourceProjectId;
    private static void Set(WorkspaceTreeNode node, string kind, Guid id)
    {
        var row = Source(node, kind); row.SourceOverride!.IsOverridden = true; row.SourceProjectId = id;
    }

    [Fact]
    public void Modules_and_projects_override_each_source_independently_including_explicit_blanks()
    {
        var tree = Tree(); var tests = Guid.NewGuid(); var models = Guid.NewGuid();
        // An explicitly unassigned ancestor is still not a source selection to override.
        Set(tree.Tests, "SortableTests", Guid.Empty);
        foreach (var node in new[] { tree.Models, tree.Project })
            foreach (var row in node.Editor!.SetupDataTypes.Where(row => row.RequiresSource))
            {
                Assert.False(row.SourceOverride!.IsOverridden);
                Assert.Equal(node.Editor.IsModule ? "Define on this module" : "Define on this project", row.SourceOverride.OverrideCaption);
            }
        Set(tree.Tests, "SortableTests", tests); Set(tree.Models, "SorterModelSet", models);
        foreach (var node in new[] { tree.Models, tree.Project })
        {
            var state = Source(node, "SortableTests").SourceOverride!;
            Assert.False(state.IsOverridden);
            Assert.Equal(node.Editor!.IsModule ? "Override on this module" : "Override on this project", state.OverrideCaption);
            Assert.False(state.CanEditValue);
        }
        Assert.All(tree.Project.Editor!.SetupDataTypes, row => Assert.False(row.SourceOverride!.IsOverridden));
        // Descendants created after the parent selection also start with inheritance enabled.
        var added = Add(tree.Models, WorkspaceEntityKind.Module, "New child");
        Assert.All(added.Editor!.SetupDataTypes, row => Assert.False(row.SourceOverride!.IsOverridden));
        Set(tree.Tests, "SortableTests", Guid.Empty);
        Assert.Equal("Define on this module", Source(tree.Models, "SortableTests").SourceOverride!.OverrideCaption);
        Assert.False(Source(tree.Models, "SortableTests").SourceOverride!.IsOverridden);
        Set(tree.Tests, "SortableTests", tests);
        foreach (var node in new[] { tree.Models, tree.Project, tree.Run })
        {
            Assert.Equal(tests, Effective(node, "SortableTests")); Assert.Equal(models, Effective(node, "SorterModelSet"));
            var inputs = node.Editor!.SetupFunctionInputs;
            Assert.Same(tree.Tests.Editor, inputs.Single(row => row.SourceKind == "SortableTests").TargetEditor);
            Assert.Same(tree.Models.Editor, inputs.Single(row => row.SourceKind == "SorterModelSet").TargetEditor);
        }
        Assert.Equal("SortableTests", Assert.Single(tree.Tests.Editor!.ToModuleOverrides().setupSources).dataType.kind);
        Assert.Equal("SorterModelSet", Assert.Single(tree.Models.Editor!.ToModuleOverrides().setupSources).dataType.kind);
        Assert.Null(tree.Project.Editor!.ToLocalSettings().setupSources);
        var replacement = Guid.NewGuid(); Set(tree.Project, "SortableTests", replacement);
        var projectRow = Source(tree.Project, "SortableTests");
        Assert.Equal(models, Effective(tree.Run, "SorterModelSet")); Assert.Equal(replacement, Effective(tree.Run, "SortableTests"));
        var changed = Guid.NewGuid(); Set(tree.Tests, "SortableTests", changed);
        Assert.Equal(replacement, Effective(tree.Run, "SortableTests"));
        projectRow.SourceOverride!.IsOverridden = false;
        Assert.Equal(changed, Effective(tree.Run, "SortableTests")); Assert.Null(tree.Project.Editor.ToLocalSettings().setupSources);
        Assert.Same(tree.Tests.Editor, projectRow.SourceOverride.InheritedSource);
        projectRow.SourceOverride.IsOverridden = true;
        Assert.Equal(replacement, Effective(tree.Run, "SortableTests")); // Restore this row's editing draft.
        projectRow.SourceProjectId = Guid.Empty;
        Assert.Equal(Guid.Empty, Effective(tree.Run, "SortableTests")); Assert.Equal(models, Effective(tree.Run, "SorterModelSet"));
        Assert.Equal(Guid.Empty, Assert.Single(tree.Project.Editor.ToLocalSettings().setupSources).sourceProjectId);
        Assert.Contains("Choose a source", tree.Project.Editor.InheritedSetupDataTypes.Single(row => row.DataType.Kind == "SortableTests").Error);

        // Domain resolution agrees with the editor, including an explicitly blank source.
        var inherited = SettingsDto.toDomain(hierarchyKind.Branch, tree.Models.Editor.ToProjectSettings());
        var local = ProjectLocalSettingsDto.toDomain(inherited, tree.Project.Editor.ToLocalSettings());
        var resolved = SettingsDto.fromDomain(ProjectResolution.resolve(inherited, local));
        Assert.Equal(models, resolved.setupDataTypes.Single(row => row.dataType.kind == "SorterModelSet").sourceProjectId);
        Assert.Equal(Guid.Empty, resolved.setupDataTypes.Single(row => row.dataType.kind == "SortableTests").sourceProjectId);
        tree.Project.Editor.Freeze(); Set(tree.Tests, "SortableTests", Guid.NewGuid());
        Assert.False(projectRow.SourceOverride.CanOverride); Assert.False(projectRow.SourceOverride.CanEditValue);
        projectRow.SourceOverride.IsOverridden = false; projectRow.SourceProjectId = tests;
        Assert.Equal(Guid.Empty, Effective(tree.Run, "SortableTests")); Assert.Equal(models, Effective(tree.Run, "SorterModelSet"));
    }

    [Fact]
    public void Query_parameters_combine_sources_from_different_modules_and_follow_source_changes()
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount", "Repl");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "SortableDataFormat");
        var alternate = SourceEvaluationInputsTests.Source("SortableTests", "PrefixLibId");
        WorkspaceSourceProject[] sources = [models, tests, alternate];
        var parent = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Tests", () => sources);
        parent.SetupFunctionName = SetupFunctionNames.evaluationSource;
        var testChoice = parent.SetupDataTypes.Single(row => row.DataType.Kind == "SortableTests");
        testChoice.SourceOverride!.IsOverridden = true; testChoice.SourceProjectId = tests.Id;
        var child = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => parent, () => "Models", () => sources);
        var modelChoice = child.SetupDataTypes.Single(row => row.DataType.Kind == "SorterModelSet");
        modelChoice.SourceOverride!.IsOverridden = true; modelChoice.SourceProjectId = models.Id;
        Assert.Equal(new[] { "ChannelCount", "Repl", "SortableDataFormat" }, child.SetupFunctionParameters.Select(row => row.Name).Order());
        testChoice.SourceProjectId = alternate.Id; child.RefreshInherited();
        Assert.Equal(new[] { "ChannelCount", "PrefixLibId", "Repl" }, child.SetupFunctionParameters.Select(row => row.Name).Order());
        Assert.Equal(models.Id, modelChoice.SourceProjectId);
        Assert.Single(child.ToModuleOverrides().setupSources);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Partial_source_grid_shows_known_parameters_without_discarding_defaults_for_later_sources(bool modelsFirst)
    {
        var models = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount", "Repl");
        var tests = SourceEvaluationInputsTests.Source("SortableTests", "ChannelCount", "SortableDataFormat");
        var first = modelsFirst ? models : tests;
        var second = modelsFirst ? tests : models;
        var firstType = modelsFirst ? "SorterModelSet" : "SortableTests";
        var secondType = modelsFirst ? "SortableTests" : "SorterModelSet";
        var deferredKey = modelsFirst ? "SortableDataFormat" : "Repl";
        var parent = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => null, () => "Parent", () => [models, tests]);
        parent.SetupFunctionName = SetupFunctionNames.evaluationSource;
        WorkspaceParameterInheritanceTests.AddDefault(parent, deferredKey, "", true);
        Assert.Empty(parent.SetupFunctionParameters);
        var selected = parent.SetupDataTypes.Single(row => row.DataType.Kind == firstType);
        selected.SourceOverride!.IsOverridden = true; selected.SourceProjectId = first.Id;
        Assert.Equal(first.Settings.resultsDataTypes.Single().queryParameterKeys.Order(), parent.SetupFunctionParameters.Select(row => row.Name).Order());
        Assert.All(parent.SetupFunctionInputs.Where(row => row.IsSourceParameter), row => Assert.Equal(firstType, row.RequiredBy));
        Assert.All(parent.SetupFunctionInputs.Where(row => row.IsSourceInput), row => Assert.True(row.IsSourceHeading));
        Assert.All(parent.SetupFunctionParameters, row => Assert.True(row.CanNavigate));
        Assert.DoesNotContain(parent.SetupFunctionParameters, row => row.Name == deferredKey);
        Assert.Equal(deferredKey, Assert.Single(parent.ToProjectSettings().setupParameterDefaults).keyName);
        Assert.True(parent.PrepareMissingFunctionParameter("ChannelCount", true));
        var added = parent.SetupParameterDefaults.Single(row => row.Key == "ChannelCount");
        Assert.Equal(Sortopia.Runs.parameterValueType.Integer, added.ValueType);
        added.Value.Text = "16";
        Assert.Equal(WorkspaceParameterSupply.Default, parent.SetupFunctionParameters.Single(row => row.Name == "ChannelCount").Supply);

        var child = new WorkspaceConfigurationViewModel(WorkspaceEntityKind.Module, true, () => parent, () => "Child", () => [models, tests]);
        var other = child.SetupDataTypes.Single(row => row.DataType.Kind == secondType);
        other.SourceOverride!.IsOverridden = true; other.SourceProjectId = second.Id;
        Assert.Equal(new[] { "ChannelCount", "Repl", "SortableDataFormat" }, child.SetupFunctionParameters.Select(row => row.Name).Order());
        Assert.Equal(new[] { "SorterModelSet", "ChannelCount", "Repl", "SortableTests", "ChannelCount", "SortableDataFormat" },
            child.SetupFunctionInputs.Select(row => row.Name));
        var sharedRows = child.SetupFunctionInputs.Where(row => row.Name == "ChannelCount").ToArray();
        Assert.Equal(new[] { "SorterModelSet", "SortableTests" }, sharedRows.Select(row => row.RequiredBy));
        Assert.All(sharedRows, row => { Assert.Equal("16", row.Value); Assert.Same(parent, row.TargetEditor); });
        Assert.Contains("Required by SortableTests", sharedRows[1].Hint);
        // An inherited blank default remains editable on the parent, even if only the child knows its source.
        var inheritedBlank = child.SetupFunctionParameters.Single(row => row.Name == deferredKey);
        Assert.Same(parent, inheritedBlank.TargetEditor); Assert.True(inheritedBlank.CanNavigate);
        Assert.True(parent.PrepareMissingFunctionParameter(deferredKey, true));
        Assert.Equal(2, parent.SetupParameterDefaults.Count);

        selected.SourceProjectId = Guid.Empty; child.RefreshInherited();
        Assert.Empty(parent.SetupFunctionParameters);
        Assert.Equal(second.Settings.resultsDataTypes.Single().queryParameterKeys.Order(), child.SetupFunctionParameters.Select(row => row.Name).Order());
        Assert.All(child.SetupFunctionInputs.Where(row => row.IsSourceParameter), row => Assert.Equal(secondType, row.RequiredBy));
        Assert.Equal(2, parent.ToProjectSettings().setupParameterDefaults.Length);
        selected.SourceProjectId = Guid.NewGuid();
        Assert.Empty(parent.SetupFunctionParameters); // A missing project contributes no invented keys.
        selected.SourceProjectId = first.Id;
        Assert.Equal("16", parent.SetupFunctionParameters.Single(row => row.Name == "ChannelCount").Value);
    }

    [Fact]
    public void Save_reload_keeps_sparse_overrides_and_inherited_updates_without_touching_payload_files()
    {
        var folder = Directory.CreateTempSubdirectory("Sortopia-source-overrides-");
        try
        {
            var tree = Tree(); var tests = Guid.NewGuid(); var models = Guid.NewGuid();
            Set(tree.Tests, "SortableTests", tests); Set(tree.Models, "SorterModelSet", models);
            var path = Path.Combine(folder.FullName, "Workspace");
            WorkspaceFileStore.CreateDocument(path, tree.Root.ToDocument(), TestContext.Current.CancellationToken);
            var payload = Path.Combine(path, "Tests owner", "Models owner", "Project", "data", "snapshot.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(payload)!); File.WriteAllBytes(payload, [1, 3, 5, 7]);
            var loaded = WorkspaceFileStore.LoadWorkspace(path);
            var reopened = WorkspaceTreeNode.FromDocument(loaded.CreateEditDocument());
            var top = reopened.Children.Single(); var child = top.Children.Single(); var project = child.Children.Single();
            Assert.Equal(tests, Effective(project, "SortableTests")); Assert.Equal(models, Effective(project, "SorterModelSet"));
            Assert.False(Source(child, "SortableTests").SourceOverride!.IsOverridden);
            Assert.False(Source(project, "SorterModelSet").SourceOverride!.IsOverridden);
            var changed = Guid.NewGuid(); Set(top, "SortableTests", changed); Set(project, "SorterModelSet", Guid.Empty);
            WorkspaceFileStore.SaveDocument(reopened.ToDocument(), loaded, TestContext.Current.CancellationToken);
            reopened = WorkspaceTreeNode.FromDocument(WorkspaceFileStore.LoadWorkspace(path).CreateEditDocument());
            project = reopened.Children.Single().Children.Single().Children.Single();
            Assert.Equal(changed, Effective(project, "SortableTests")); Assert.Equal(Guid.Empty, Effective(project, "SorterModelSet"));
            Assert.Equal(new byte[] { 1, 3, 5, 7 }, File.ReadAllBytes(payload));
            Assert.Equal(tree.Run.Id, project.Children.Single().Id);
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Previous_module_and_project_formats_are_rejected(bool project)
    {
        var document = Tree().Root.ToDocument();
        foreach (var version in new[] { 18, 19 })
        {
            if (project)
            {
                var dto = WorkspaceDocumentModule.projectFile(document.Modules.Single().Modules.Single().Projects.Single());
                dto.schemaVersion = version;
                Assert.Equal("schemaVersion", Assert.Throws<ArgumentException>(() =>
                    WorkspaceDefinitionCodec.decodeProject(MessagePackSetup.serialize(dto))).ParamName);
            }
            else
            {
                var dto = WorkspaceDocumentModule.moduleFile(document.Modules.Single().Modules.Single());
                dto.schemaVersion = version;
                Assert.Equal("schemaVersion", Assert.Throws<ArgumentException>(() =>
                    WorkspaceDefinitionCodec.decodeModule(MessagePackSetup.serialize(dto))).ParamName);
            }
        }
    }
}
