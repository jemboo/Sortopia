using System.IO;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.workspaceModule;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceInheritanceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-inheritance-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static project Parent(bool branch, string name = "P", string description = "Project description")
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.description = description;
        settings.resultsParameterDefaults = []; settings.setupParameterDefaults = [];
        return project.create(Guid.NewGuid(), name, name, ProjectDto.settingsToDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
    }
    private static runDto Inputs(project parent, string name = "R")
    {
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun(parent.ProjectName, name, parent.Kind == hierarchyKind.Branch));
        dto.id = Guid.NewGuid();
        dto.parentProjectId = parent.Id;
        return dto;
    }
    private static project WithRuns(project parent, params runDto[] inputs) =>
        project.create(parent.Id, parent.ProjectName, parent.FolderName, parent.Configuration, inputs.Select(input => RunDto.toDomain(parent, input)).ToArray());

    [Fact]
    public void Root_run_constructor_accepts_its_parent_and_only_run_inputs()
    {
        var parent = rootProject.seal(((project.RootProject)Parent(false)).Item);
        var spans = ((run.RootRun)WorkspaceHierarchyTests.MakeRun("P", "R")).Item.ResultsParameterSpans;
        var id = Guid.NewGuid();
        var inherited = RootRun.create(parent, new rootRunConfig(id, "Inherited", parent.Id, null, spans));
        Assert.Equal("Project description", inherited.Description);
        Assert.Equal(parent.ResultsDatabaseName, inherited.ResultsDatabaseName);
        Assert.Equal(parent.ResultsFunctionName, inherited.ResultsFunctionName);
        Assert.Equal(parent.ResultsSortopiaDataTypes, inherited.ResultsSortopiaDataTypes);
        Assert.Equal(parent.ResultsQueryParameterKeys, inherited.ResultsQueryParameterKeys);
        Assert.Null(inherited.Configuration.DescriptionOverride);
        Assert.Equal(id, inherited.Id);
        var blank = RootRun.create(parent, new rootRunConfig(Guid.NewGuid(), "Blank", parent.Id, FSharpOption<string>.Some(""), spans));
        Assert.Equal("", blank.Description);
        Assert.NotNull(blank.Configuration.DescriptionOverride);
    }

    [Fact]
    public void Branch_run_constructor_fixes_setup_function_and_allows_description_override()
    {
        var parent = branchProject.seal(((project.BranchProject)Parent(true)).Item);
        var template = ((run.BranchRun)WorkspaceHierarchyTests.MakeRun("P", "R", true)).Item;
        var inherited = BranchRun.create(parent, new branchRunConfig(Guid.NewGuid(), "Inherited", parent.Id, null,
            template.ResultsParameterSpans, template.SetupParameterSpans));
        Assert.Equal(parent.Description, inherited.Description);
        Assert.Equal(parent.SetupDatabaseName, inherited.SetupDatabaseName);
        Assert.Equal(parent.SetupFunctionName, inherited.SetupFunctionName);
        Assert.Equal(parent.SetupSortopiaDataTypes, inherited.SetupSortopiaDataTypes);
        Assert.Equal(parent.SetupQueryParameterKeys, inherited.SetupQueryParameterKeys);
        var descriptionOverride = BranchRun.create(parent, new branchRunConfig(Guid.NewGuid(), "Override description", parent.Id, FSharpOption<string>.Some("Custom description"),
            template.ResultsParameterSpans, template.SetupParameterSpans));
        Assert.Equal("Setup", descriptionOverride.SetupDatabaseName);
        Assert.Equal("sortable-tests-with-prefix.standard", descriptionOverride.SetupFunctionName);
        Assert.Equal("Custom description", descriptionOverride.Description);
        Assert.Null(typeof(branchRunConfig).GetProperty("SetupFunctionNameOverride"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Round_trip_keeps_sealed_settings_and_explicit_run_overrides(bool branch)
    {
        var parent = Parent(branch);
        var inherited = Inputs(parent, "Inherited");
        var explicitValues = Inputs(parent, "Explicit");
        explicitValues.descriptionOverride = "";
        Assert.Null(typeof(runDto).GetProperty("setupFunctionNameOverride"));
        parent = WithRuns(parent, inherited, explicitValues);
        var dto = ProjectDto.fromDomain(WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(parent)));
        Assert.Null(dto.runs[0].descriptionOverride);
        Assert.Equal("", dto.runs[1].descriptionOverride);
        Assert.Null(typeof(runDto).GetProperty("setupDatabaseNameOverride"));
        dto.localOverrides.description = "New project description";
        dto.localOverrides.resultsDatabaseName = "New results";
        if (branch) { dto.localOverrides.setupDatabaseName = "New setup"; }
        var updated = ProjectDto.toDomain(dto);
        Assert.Equal("Project description", updated.Runs[0].Description);
        Assert.Equal("", updated.Runs[1].Description);
        Assert.All(updated.Runs, item => Assert.Equal("Results", item.ResultsDatabaseName));
        Assert.All(updated.Runs, item => Assert.Equal("sortable-tests.standard", item.ResultsFunctionName));
        Assert.Equal(parent.Runs.Select(item => item.Id), updated.Runs.Select(item => item.Id));
        if (branch)
        {
            Assert.Equal("Setup", updated.Runs[0].SetupDatabaseName.Value);
            Assert.Equal("sortable-tests-with-prefix.standard", updated.Runs[0].SetupFunctionName.Value);
            Assert.Equal("Setup", updated.Runs[1].SetupDatabaseName.Value);
            Assert.Equal("sortable-tests-with-prefix.standard", updated.Runs[1].SetupFunctionName.Value);
        }
        var fileJson = JsonSerializer.SerializeToNode(RunDto.fromDomain(updated.Runs[0]))!.AsObject();
        Assert.False(fileJson.ContainsKey("resultsDatabaseName"));
        Assert.False(fileJson.ContainsKey("resultsFunctionName"));
        Assert.False(fileJson.ContainsKey("resultsSortopiaDataTypes"));
        Assert.False(fileJson.ContainsKey("setupSortopiaDataTypes"));
        Assert.Equal(updated.Runs[0].Description, WorkspaceCodec.decodeRun(updated, WorkspaceCodec.encodeRun(updated.Runs[0])).Description);
    }

    [Fact]
    public void Each_setup_output_uses_its_own_query_keys()
    {
        var value = (run.BranchRun)RunExecutionTests.MakeRun(true, FunctionOutputContractTests.Combined(true));
        var parameters = BranchRun.makeSetupParameterSets(value.Item)[0];
        var tests = QueryParams.forSetup(value.Item, sortopiaDataType.NewSortableTests(""), parameters);
        var models = QueryParams.forSetup(value.Item, sortopiaDataType.NewSorterModelSet(""), parameters);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, tests.Properties.Keys);
        Assert.Contains("Repl", models.Properties.Keys); Assert.DoesNotContain("Repl", tests.Properties.Keys);
        Assert.NotEqual(tests.Id, models.Id);
    }

    [Fact]
    public void Run_parameters_must_satisfy_parent_query_keys_and_blank_setup_overrides_are_rejected()
    {
        var parent = Parent(true); var inputs = Inputs(parent); inputs.resultsParameterSpans = [];
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        inputs = Inputs(parent); inputs.setupParameterSpans = [];
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        Assert.Null(typeof(runDto).GetProperty("setupFunctionNameOverride"));
        Assert.Null(typeof(projectOverridesDto).GetProperty("setupDataTypes"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_runs_cannot_change_their_parent_project(bool branch)
    {
        var source = Parent(branch, "Source", "Source description");
        var inherited = Inputs(source, "Inherited");
        var overridden = Inputs(source, "Overridden");
        overridden.descriptionOverride = "Explicit description";
        source = WithRuns(source, inherited, overridden);
        var destinationDto = ProjectDto.fromDomain(Parent(branch, "Destination", "Destination description"));
        destinationDto.localOverrides.resultsDatabaseName = "Destination results";
        if (branch) destinationDto.localOverrides.setupDatabaseName = "Destination setup";
        var destination = ProjectDto.toDomain(destinationDto);
        var module = WorkspaceHierarchyTests.MakeModule(source.Kind, Guid.NewGuid(), "M", "M", [], [source, destination]);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "W", "Workspace", [module]), Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        var nodes = page.Roots.Single().Children.Single().Children;
        var originalRun = nodes[0].Children[0];
        page.Selected = originalRun;
        page.CutCommand.Execute(null);
        page.Selected = nodes[1];
        await page.PasteAsync();
        Assert.Contains("parent project", page.Error);
        Assert.Same(nodes[0], originalRun.Parent);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules.Single().Projects[0];
        Assert.Equal(source.Id, saved.Runs[0].ParentProjectId);
        Assert.Equal("Source description", saved.Runs[0].Description);
    }

    [Fact]
    public async Task Editor_updates_project_defaults_and_validates_run_fields()
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        page.AddModuleCommand.Execute(null);
        page.AddProjectCommand.Execute(null);
        var parent = page.Selected!;
        WorkspaceHierarchyTests.ConfigureProject(parent);
        Assert.True(parent.HasConfiguration);
        Assert.Equal("Project defaults", parent.Editor!.Title);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        run.Editor!.LoadRun(Inputs(Parent(false)));
        Assert.Equal("sortable-tests.standard", run.Editor.ResultsFunctionName);
        page.EditProjectDefaultsCommand.Execute(null);
        Assert.Same(parent, page.Selected);
        parent.Editor!.Description = "Edited project description";
        Assert.Equal("Edited project description", run.Editor.DescriptionOverride.EffectiveValue);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.Equal("Edited project description", WorkspaceFileStore.LoadDefinition(Root).Modules[0].Projects[0].Runs[0].Description);
        var file = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var before = File.ReadAllBytes(file);
        run = page.Roots[0].Children[0].Children[0].Children[0];
        run.Editor!.ResultsFunctionName = "Not an allowed run override";
        Assert.Equal("sortable-tests.standard", run.Editor.ResultsFunctionName);
        run.Editor.ResultsParameters.Clear();
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Contains("ChannelCount", page.Error);
        Assert.Empty(page.OperationError); Assert.False(page.HasUnsavedChanges);
        Assert.Empty(WorkspaceFileStore.LoadDocument(Root).Modules[0].Projects[0].Runs[0].resultsParameterSpans);
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
