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
using WorkspaceModule = Sortopia.Workspaces.Serialization.ModuleDocument;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceInheritanceTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-project-inheritance-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ProjectDocument Parent(bool branch, string name = "P", string description = "Project description")
    {
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.description = description;
        settings.resultsParameterDefaults = []; settings.setupParameterDefaults = [];
        foreach (var output in settings.resultsDataTypes) output.queryParameterKeys = ["ChannelCount", "SortableDataFormat"];
        foreach (var output in settings.setupDataTypes) output.queryParameterKeys = ["ChannelCount", "SortableDataFormat"];
        return WorkspaceFixtures.Project(Guid.NewGuid(), name, SettingsDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
    }
    private static runDto Inputs(ProjectDocument parent, string name = "R")
    {
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun(parent.Definition.projectName, name, parent.Family() == hierarchyKind.Branch));
        dto.id = Guid.NewGuid();
        dto.parentProjectId = parent.Definition.id;
        return dto;
    }
    private static ProjectDocument WithRuns(ProjectDocument parent, params runDto[] inputs) =>
        WorkspaceFixtures.Project(parent.Definition.id, parent.Definition.projectName, parent.Configuration(), inputs.Select(input => RunDto.toDomain(parent.Snapshot(), input)).ToArray());

    [Fact]
    public void Root_run_constructor_accepts_its_parent_and_only_run_inputs()
    {
        var parent = WorkspaceFixtures.Seal(Parent(false));
        var spans = WorkspaceHierarchyTests.MakeRun("P", "R").ResultsParameterSpans;
        var id = Guid.NewGuid();
        var inherited = Run.create(parent.Snapshot(), new runConfig(id, "Inherited", parent.Definition.id, null, spans, null, null, null));
        Assert.Equal("Project description", inherited.Description);
        Assert.Equal(parent.Configuration().ResultsDatabaseName, inherited.ResultsDatabaseName);
        Assert.Equal(parent.Configuration().ResultsFunctionName, inherited.ResultsFunctionName);
        Assert.Equal(parent.Configuration().ResultsSortopiaDataTypes, inherited.ResultsSortopiaDataTypes);
        Assert.Equal(parent.Configuration().ResultsQueryParameterKeys, inherited.ResultsQueryParameterKeys);
        Assert.Null(inherited.Configuration.DescriptionOverride);
        Assert.Equal(id, inherited.Id);
        var blank = Run.create(parent.Snapshot(), new runConfig(Guid.NewGuid(), "Blank", parent.Definition.id, FSharpOption<string>.Some(""), spans, null, null, null));
        Assert.Equal("", blank.Description);
        Assert.NotNull(blank.Configuration.DescriptionOverride);
    }

    [Fact]
    public void Branch_run_constructor_fixes_setup_function_and_allows_description_override()
    {
        var parent = WorkspaceFixtures.Seal(Parent(true));
        var template = WorkspaceHierarchyTests.MakeRun("P", "R", true);
        var inherited = Run.create(parent.Snapshot(), new runConfig(Guid.NewGuid(), "Inherited", parent.Definition.id, null,
            template.ResultsParameterSpans, template.SetupParameterSpans, null, null));
        Assert.Equal(parent.Configuration().Description, inherited.Description);
        Assert.Equal(parent.Configuration().Setup.Value.DatabaseName, inherited.SetupDatabaseName.Value);
        Assert.Equal(parent.Configuration().Setup.Value.FunctionName, inherited.SetupFunctionName.Value);
        Assert.Equal(parent.Configuration().Setup.Value.SortopiaDataTypes, inherited.SetupSortopiaDataTypes.Value);
        Assert.Equal(parent.Configuration().Setup.Value.QueryParameterKeys, inherited.SetupQueryParameterKeys.Value);
        var descriptionOverride = Run.create(parent.Snapshot(), new runConfig(Guid.NewGuid(), "Override description", parent.Definition.id, FSharpOption<string>.Some("Custom description"),
            template.ResultsParameterSpans, template.SetupParameterSpans, null, null));
        Assert.Equal("Setup", descriptionOverride.SetupDatabaseName.Value);
        Assert.Equal("sortable-tests-with-prefix.standard", descriptionOverride.SetupFunctionName.Value);
        Assert.Equal("Custom description", descriptionOverride.Description);
        Assert.Null(typeof(runConfig).GetProperty("SetupFunctionNameOverride"));
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
        var dto = WorkspaceDocumentModule.cloneProject(parent);
        Assert.Null(dto.Runs[0].descriptionOverride);
        Assert.Equal("", dto.Runs[1].descriptionOverride);
        Assert.Null(typeof(runDto).GetProperty("setupDatabaseNameOverride"));
        dto.Definition.state = projectStateDto.NewSealed(dto.Definition.configuration, Microsoft.FSharp.Core.FSharpOption<projectLocalSettingsDto>.Some(ProjectLocalSettingsDto.fromDomain(parent.LocalSettings())));
        dto.Definition.localSettings.description = "New project description";
        dto.Definition.localSettings.resultsDatabaseName = "New results";
        if (branch) { dto.Definition.localSettings.setupDatabaseName = "New setup"; }
        var updated = dto;
        Assert.Equal("Project description", updated.ResolvedRuns()[0].Description);
        Assert.Equal("", updated.ResolvedRuns()[1].Description);
        Assert.All(updated.ResolvedRuns(), item => Assert.Equal("Results", item.ResultsDatabaseName));
        Assert.All(updated.ResolvedRuns(), item => Assert.Equal("sortable-tests.standard", item.ResultsFunctionName));
        Assert.Equal(parent.Runs.Select(item => item.id), updated.Runs.Select(item => item.id));
        if (branch)
        {
            Assert.Equal("Setup", updated.ResolvedRuns()[0].SetupDatabaseName.Value);
            Assert.Equal("sortable-tests-with-prefix.standard", updated.ResolvedRuns()[0].SetupFunctionName.Value);
            Assert.Equal("Setup", updated.ResolvedRuns()[1].SetupDatabaseName.Value);
            Assert.Equal("sortable-tests-with-prefix.standard", updated.ResolvedRuns()[1].SetupFunctionName.Value);
        }
        var fileJson = JsonSerializer.SerializeToNode(RunDto.fromDomain(updated.ResolvedRuns()[0]))!.AsObject();
        Assert.False(fileJson.ContainsKey("resultsDatabaseName"));
        Assert.False(fileJson.ContainsKey("resultsFunctionName"));
        Assert.False(fileJson.ContainsKey("resultsSortopiaDataTypes"));
        Assert.False(fileJson.ContainsKey("setupSortopiaDataTypes"));
        Assert.Equal(updated.ResolvedRuns()[0].Description, RunCodec.decodeRun(updated.Snapshot(), RunCodec.encodeRun(updated.ResolvedRuns()[0])).Description);
    }

    [Fact]
    public void Each_setup_output_uses_its_own_query_keys()
    {
        var value = RunExecutionTests.MakeRun(true, FunctionOutputContractTests.Combined(true));
        var parameters = Run.makeSetupParameterSets(value)[0];
        var tests = QueryParams.forSetup(value, sortopiaDataType.NewSortableTests(""), parameters);
        var models = QueryParams.forSetup(value, sortopiaDataType.NewSorterModelSet(""), parameters);
        Assert.Equal(new[] { "ChannelCount", "SortableDataFormat" }, tests.Properties.Keys);
        Assert.Contains("Repl", models.Properties.Keys); Assert.DoesNotContain("Repl", tests.Properties.Keys);
        Assert.NotEqual(tests.Id, models.Id);
    }

    [Fact]
    public void Run_parameters_must_satisfy_parent_query_keys_and_blank_setup_overrides_are_rejected()
    {
        var parent = Parent(true); var inputs = Inputs(parent); inputs.resultsParameterSpans = [];
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent.Snapshot(), inputs)).Message);
        inputs = Inputs(parent); inputs.setupParameterSpans = [];
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent.Snapshot(), inputs)).Message);
        Assert.Null(typeof(runDto).GetProperty("setupFunctionNameOverride"));
        Assert.Null(typeof(projectLocalSettingsDto).GetProperty("setupDataTypes"));
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
        var destinationDto = Parent(branch, "Destination", "Destination description");
        destinationDto.Definition.localSettings.resultsDatabaseName = "Destination results";
        if (branch) destinationDto.Definition.localSettings.setupDatabaseName = "Destination setup";
        var destination = destinationDto;
        var module = WorkspaceHierarchyTests.MakeModule(source.Family(), Guid.NewGuid(), "M", [], [source, destination]);
        WorkspaceFileStore.CreateDocument(Root, WorkspaceFixtures.Workspace(Guid.NewGuid(), "W", "Workspace", [module]), Token);
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
        var saved = WorkspaceTestFiles.LoadRun(Root, document => document.Modules.Single().Projects[0].Runs[0]);
        Assert.Equal(source.Definition.id, saved.ParentProjectId);
        Assert.Equal("Source description", saved.Description);
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
        Assert.Equal("Edited project description", WorkspaceTestFiles.LoadRun(Root, document => document.Modules[0].Projects[0].Runs[0]).Description);
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
        Assert.All(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Projects[0].Runs[0].resultsParameterSpans, span => Assert.Empty(span.values));
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
