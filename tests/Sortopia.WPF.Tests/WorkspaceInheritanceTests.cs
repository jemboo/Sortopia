using System.IO;
using System.Text.Json;
using Microsoft.FSharp.Core;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
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
        return project.create(Guid.NewGuid(), name, name, ProjectDto.settingsToDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, settings), []);
    }
    private static runDto Inputs(project parent, string name = "R")
    {
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun(parent.ProjectName, name, parent.Kind == hierarchyKind.Branch));
        dto.id = Guid.NewGuid();
        return dto;
    }
    private static project WithRuns(project parent, params runDto[] inputs) =>
        project.create(parent.Id, parent.ProjectName, parent.FolderName, parent.Configuration, inputs.Select(input => RunDto.toDomain(parent, input)).ToArray());

    [Fact]
    public void Root_run_constructor_accepts_its_parent_and_only_run_inputs()
    {
        var parent = ((project.RootProject)Parent(false)).Item;
        var spans = ((run.RootRun)WorkspaceHierarchyTests.MakeRun("P", "R")).Item.OutputParameterSpans;
        var id = Guid.NewGuid();
        var inherited = RootRun.create(parent, new rootRunConfig(id, "Inherited", null, spans));
        Assert.Equal("Project description", inherited.Description);
        Assert.Equal(parent.ResultsDatabaseName, inherited.ResultsDatabaseName);
        Assert.Equal(parent.ExecuteFunctionName, inherited.ExecuteFunctionName);
        Assert.Equal(parent.OutputDataTypes, inherited.OutputDataTypes);
        Assert.Equal(parent.OutputQueryParameterKeys, inherited.OutputQueryParameterKeys);
        Assert.Null(inherited.Configuration.Description);
        Assert.Equal(id, inherited.Id);
        var blank = RootRun.create(parent, new rootRunConfig(Guid.NewGuid(), "Blank", FSharpOption<string>.Some(""), spans));
        Assert.Equal("", blank.Description);
        Assert.NotNull(blank.Configuration.Description);
    }

    [Fact]
    public void Branch_run_constructor_inherits_and_overrides_setup_fields_independently()
    {
        var parent = ((project.BranchProject)Parent(true)).Item;
        var template = ((run.BranchRun)WorkspaceHierarchyTests.MakeRun("P", "R", true)).Item;
        var inherited = BranchRun.create(parent, new branchRunConfig(Guid.NewGuid(), "Inherited", null,
            template.OutputParameterSpans, null, null, template.SetupParameterSpans));
        Assert.Equal(parent.Description, inherited.Description);
        Assert.Equal(parent.SetupDatabaseName, inherited.SetupDatabaseName);
        Assert.Equal(parent.SetupFunctionName, inherited.SetupFunctionName);
        Assert.Equal(parent.SetupOutputDataTypes, inherited.SetupOutputDataTypes);
        Assert.Equal(parent.SetupQueryParameterKeys, inherited.SetupQueryParameterKeys);
        var databaseOverride = BranchRun.create(parent, new branchRunConfig(Guid.NewGuid(), "Override database", FSharpOption<string>.Some("Custom description"),
            template.OutputParameterSpans, FSharpOption<string>.Some("Other setup"), null, template.SetupParameterSpans));
        Assert.Equal("Other setup", databaseOverride.SetupDatabaseName);
        Assert.Equal("Generate", databaseOverride.SetupFunctionName);
        Assert.Equal("Custom description", databaseOverride.Description);
        var functionOverride = BranchRun.create(parent, new branchRunConfig(Guid.NewGuid(), "Override function", null,
            template.OutputParameterSpans, null, FSharpOption<string>.Some("Other generator"), template.SetupParameterSpans));
        Assert.Equal("Setup", functionOverride.SetupDatabaseName);
        Assert.Equal("Other generator", functionOverride.SetupFunctionName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Round_trip_preserves_inheritance_and_explicit_overrides_when_project_defaults_change(bool branch)
    {
        var parent = Parent(branch);
        var inherited = Inputs(parent, "Inherited");
        var explicitValues = Inputs(parent, "Explicit");
        explicitValues.description = "";
        if (branch) { explicitValues.setupDatabaseName = "Fixed setup"; explicitValues.setupFunctionName = "Fixed generator"; }
        parent = WithRuns(parent, inherited, explicitValues);
        var dto = ProjectDto.fromDomain(WorkspaceCodec.decodeProject(WorkspaceCodec.encodeProject(parent)));
        Assert.Null(dto.runs[0].description);
        Assert.Equal("", dto.runs[1].description);
        Assert.Null(dto.runs[0].setupDatabaseName);
        dto.localOverrides.description = "New project description";
        dto.localOverrides.resultsDatabaseName = "New results";
        dto.localOverrides.executeFunctionName = "New executor";
        if (branch) { dto.localOverrides.setupDatabaseName = "New setup"; dto.localOverrides.setupFunctionName = "New generator"; }
        var updated = ProjectDto.toDomain(dto);
        Assert.Equal("New project description", updated.Runs[0].Description);
        Assert.Equal("", updated.Runs[1].Description);
        Assert.All(updated.Runs, item => Assert.Equal("New results", item.ResultsDatabaseName));
        Assert.All(updated.Runs, item => Assert.Equal("New executor", item.ExecuteFunctionName));
        Assert.Equal(parent.Runs.Select(item => item.Id), updated.Runs.Select(item => item.Id));
        if (branch)
        {
            Assert.Equal("New setup", updated.Runs[0].SetupDatabaseName.Value);
            Assert.Equal("New generator", updated.Runs[0].SetupFunctionName.Value);
            Assert.Equal("Fixed setup", updated.Runs[1].SetupDatabaseName.Value);
            Assert.Equal("Fixed generator", updated.Runs[1].SetupFunctionName.Value);
        }
        var fileJson = JsonSerializer.SerializeToNode(RunDto.fromDomain(updated.Runs[0]))!.AsObject();
        Assert.False(fileJson.ContainsKey("resultsDatabaseName"));
        Assert.False(fileJson.ContainsKey("executeFunctionName"));
        Assert.False(fileJson.ContainsKey("outputDataTypes"));
        Assert.False(fileJson.ContainsKey("setupOutputDataTypes"));
        Assert.Equal(updated.Runs[0].Description, WorkspaceCodec.decodeRun(updated, WorkspaceCodec.encodeRun(updated.Runs[0])).Description);
    }

    [Fact]
    public void Shared_setup_query_keys_apply_to_each_setup_output_type()
    {
        var dto = ProjectDto.fromDomain(Parent(true));
        dto.localOverrides.setupOutputDataTypes = [new() { kind = "SorterModelSet", value = "" }, new() { kind = "SorterSetEval", value = "" }];
        var parent = ProjectDto.toDomain(dto);
        var inputs = Inputs(parent);
        inputs.setupDatabaseName = "Run setup";
        var run = ((run.BranchRun)RunDto.toDomain(parent, inputs)).Item;
        var parameters = BranchRun.makeSetupParameterSets(run).First();
        var first = QueryParams.forSetup(run, sortopiaDataType.NewSorterModelSet(""), parameters);
        var second = QueryParams.forSetup(run, sortopiaDataType.NewSorterSetEval(""), parameters);
        Assert.Equal("Run setup", first.DatabaseName);
        Assert.Equal("Width", first.Properties.Single().Key);
        Assert.Equal("8", first.Properties.Single().Value);
        Assert.Equal(first.Properties, second.Properties);
        Assert.NotEqual(first.Id, second.Id);
        var result = QueryParams.forResults(Sortopia.Runs.run.NewBranchRun(run), sortopiaDataType.NewSorterSetEval(""), BranchRun.makeParameterSets(run).First());
        Assert.Equal("Results", result.DatabaseName);
        Assert.Equal("Seed", result.Properties.Single().Key);
    }

    [Fact]
    public void Run_parameters_must_satisfy_parent_query_keys_and_blank_setup_overrides_are_rejected()
    {
        var parent = Parent(true);
        var inputs = Inputs(parent);
        inputs.outputParameterSpans = [];
        Assert.Contains("Seed", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        inputs = Inputs(parent);
        inputs.setupParameterSpans = [];
        Assert.Contains("Width", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        inputs = Inputs(parent);
        inputs.setupFunctionName = "";
        Assert.Contains("override", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        inputs = Inputs(parent);
        inputs.setupDatabaseName = " ";
        Assert.Contains("override", Assert.Throws<ArgumentException>(() => RunDto.toDomain(parent, inputs)).Message);
        var projectDto = ProjectDto.fromDomain(parent);
        projectDto.localOverrides.setupQueryParameterKeys = ["Width", "Width"];
        Assert.Throws<ArgumentException>(() => ProjectDto.toDomain(projectDto));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_a_run_uses_destination_project_defaults_and_preserves_its_overrides(bool branch)
    {
        var source = Parent(branch, "Source", "Source description");
        var inherited = Inputs(source, "Inherited");
        var overridden = Inputs(source, "Overridden");
        overridden.description = "Explicit description";
        if (branch) overridden.setupDatabaseName = "Explicit database";
        source = WithRuns(source, inherited, overridden);
        var destinationDto = ProjectDto.fromDomain(Parent(branch, "Destination", "Destination description"));
        destinationDto.localOverrides.resultsDatabaseName = "Destination results";
        if (branch) destinationDto.localOverrides.setupDatabaseName = "Destination setup";
        var destination = ProjectDto.toDomain(destinationDto);
        var module = WorkspaceModule.create(source.Kind, Guid.NewGuid(), "M", "M", [], [source, destination]);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "W", "Workspace", [module]), Token);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => null, settings);
        var nodes = page.Roots.Single().Children.Single().Children;
        foreach (var item in nodes[0].Children.ToArray())
        {
            page.Selected = item;
            page.CutCommand.Execute(null);
            page.Selected = nodes[1];
            await page.PasteAsync();
        }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules.Single().Projects[1];
        Assert.Equal("Destination description", saved.Runs[0].Description);
        Assert.Equal("Explicit description", saved.Runs[1].Description);
        Assert.Equal(new[] { inherited.id, overridden.id }, saved.Runs.Select(item => item.Id));
        Assert.All(saved.Runs, item => Assert.Equal("Destination results", item.ResultsDatabaseName));
        if (branch)
        {
            Assert.Equal("Destination setup", saved.Runs[0].SetupDatabaseName.Value);
            Assert.Equal("Explicit database", saved.Runs[1].SetupDatabaseName.Value);
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(Root, "modules", "M", "projects", "Source", "runs")));
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
        Assert.Equal("Evaluate", run.Editor.ExecuteFunctionName);
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
        run.Editor!.ExecuteFunctionName = "Not an allowed run override";
        Assert.Equal("Evaluate", run.Editor.ExecuteFunctionName);
        run.Editor.OutputParameters.Clear();
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        await page.SaveWorkspaceAsync();
        Assert.Contains("Seed", page.Error);
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
