using System.IO;
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

public sealed class WorkspaceModuleDefaultsTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-module-defaults-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open(Func<WorkspaceFolderRequest, string?>? pick = null)
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings, pickExistingFolder: pick);
    }
    private static void AddModule(WorkspacePageViewModel page, bool branch) =>
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);

    private static WorkspaceModule Hierarchy(bool branch)
    {
        var kind = branch ? hierarchyKind.Branch : hierarchyKind.Root;
        var config = SettingsDto.toDomain(kind, WorkspaceHierarchyTests.ProjectSettings(branch));
        var parent = WorkspaceFixtures.DraftProject(Guid.NewGuid(), "P", config, ProjectResolution.initial(config), []);
        var inputs = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch));
        inputs.parentProjectId = parent.Definition.id;
        inputs.resultsParameterSpans = []; inputs.setupParameterSpans = [];
        parent = WorkspaceFixtures.DraftProject(parent.Definition.id, parent.Definition.projectName, config, parent.LocalSettings(), [RunDto.toDomain(parent.Snapshot(), inputs)]);
        var nested = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Nested", ModuleSettingsDto.toDomain(kind, WorkspaceHierarchyTests.ModuleSettings(branch)), ModuleDefaults.inherited(branch), [], [parent]);
        var settings = WorkspaceHierarchyTests.ModuleSettings(branch);
        var moduleLocal = new moduleOverridesDto { description = settings.description, resultsFunctionName = settings.resultsFunctionName,
            resultsParameterDefaults = settings.resultsParameterDefaults, setupFunctionName = branch ? settings.setupFunctionName : null,
            setupParameterDefaults = branch ? settings.setupParameterDefaults : null, setupSources = branch ? settings.setupSources : null };
        return WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Top", ModuleDefaults.configuration(branch), ModuleOverridesDto.toDomain(kind, moduleLocal), [nested], []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialization_resolves_modules_but_preserves_sealed_project_snapshots(bool branch)
    {
        var original = WorkspaceFixtures.Workspace(Guid.NewGuid(), "W", "Workspace", [Hierarchy(branch)]);
        var dto = WorkspaceDocumentModule.clone(original);
        dto.Modules[0].Definition.localOverrides.description = "Changed ancestor";
        dto.Modules[0].Definition.localOverrides.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiSimpleEvaluationInputs;
        dto.Modules[0].Modules[0].Definition.localOverrides.description = "Nested description";
        var nested = dto.Modules[0].Modules[0]; var saved = nested.Projects[0];
        Assert.Equal("Nested description", nested.Configuration().Description);
        Assert.Equal("Keep every setting: α", saved.Configuration().Description);
        Assert.Equal(Sortopia.Functions.ResultsFunctionNames.standardSortableTests, saved.Configuration().ResultsFunctionName);
        Assert.Equal("1", saved.ResultsSnapshot().Results.FunctionVersion);
        Assert.Null(typeof(moduleOverridesDto).GetProperty("resultsSortopiaDataTypes"));
        Assert.Null(typeof(projectLocalSettingsDto).GetProperty("resultsFunctionName"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_cascades_allowed_defaults_and_reports_their_source(bool branch)
    {
        var page = Open(); AddModule(page, branch); var top = page.Selected!;
        top.Editor!.LoadFixtureSettings(WorkspaceHierarchyTests.ProjectSettings(branch));
        AddModule(page, branch); var child = page.Selected!;
        child.Editor!.Description = "Local description";
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        Assert.Same(top.Editor, run.Editor!.OutputTypesOverride.InheritedSource);
        Assert.Same(child.Editor, run.Editor.DescriptionOverride.InheritedSource);
        Assert.False(project.Editor!.OutputTypesOverride.CanOverride);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var document = WorkspaceFileStore.LoadWorkspace(Root);
        var saved = document.CreateEditDocument().Modules[0].Modules[0];
        Assert.Equal("Local description", document.ResolveRun(saved.Projects[0].Runs[0].id).Run.Description);
        Assert.Null(saved.Definition.localOverrides.resultsFunctionName);
        Assert.Null(typeof(moduleOverridesDto).GetProperty("resultsQueryParameterKeys"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_and_copying_modules_keeps_sealed_project_settings(bool branch)
    {
        var source = Hierarchy(branch);
        var kind = source.Family();
        var settings = WorkspaceHierarchyTests.ModuleSettings(branch);
        settings.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiSimpleEvaluationInputs;
        settings.description = "Destination description";
        var local = new moduleOverridesDto { description = settings.description, resultsParameterDefaults = settings.resultsParameterDefaults,
            setupParameterDefaults = branch ? settings.setupParameterDefaults : null };
        var destination = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Destination", ModuleSettingsDto.toDomain(kind, settings),
            ModuleOverridesDto.toDomain(kind, local), [], []);
        WorkspaceFileStore.CreateDocument(Root, WorkspaceFixtures.Workspace(Guid.NewGuid(), "W", "Workspace", [source, destination]), Token);
        var page = Open();
        var nested = page.Roots[0].Children[0].Children[0];
        nested.Editor!.Description = "Keep this local description";
        page.Selected = nested; page.CutCommand.Execute(null);
        page.Selected = page.Roots[0].Children[1]; await page.PasteAsync();
        Assert.Equal("sortable-tests.standard", nested.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep every setting: α", nested.Children[0].Children[0].Editor!.Description);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[1].Modules[0];
        Assert.Equal(nested.Id, saved.Definition.id);
        Assert.Equal("Results", saved.Projects[0].Definition.configuration.resultsDatabaseName);
        Assert.Null(typeof(moduleSettingsDto).GetProperty("resultsDatabaseName"));

        var importFolder = Path.Combine(Root, "Destination", "Nested");
        var importing = Open(_ => importFolder);
        importing.Selected = importing.Roots[0].Children[0];
        await importing.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(importing.Error);
        Assert.Equal("sortable-tests.standard", importing.Selected!.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep this local description", importing.Selected.Editor!.Description);
        await importing.SaveWorkspaceAsync(); Assert.Empty(importing.Error);
        var copied = WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument().Modules[0].Modules[0];
        Assert.NotEqual(saved.Definition.id, copied.Definition.id);
        Assert.Equal("Results", copied.Projects[0].Definition.configuration.resultsDatabaseName);
        Assert.Null(copied.Definition.localOverrides.resultsFunctionName);
        Assert.Null(typeof(projectLocalSettingsDto).GetProperty("resultsFunctionName"));
    }

    [Fact]
    public async Task Empty_modules_can_leave_functions_inherited_but_projects_require_resolved_values()
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null);
        Assert.Empty(page.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        var module = page.Selected!;
        page.AddProjectCommand.Execute(null);
        Assert.Contains("No results function is selected", page.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        page.EditProjectDefaultsCommand.Execute(null);
        Assert.Same(module, page.Selected);
        module.Editor!.ResultsFunctionName = "sortable-tests.standard";
        module.Editor.SetupFunctionName = "sortable-tests-with-prefix.standard";
        Assert.Empty(page.Error);
        module.Editor.ResultsFunctionOverride.IsOverridden = false;
        Assert.NotEmpty(page.Error);
        module.Editor.ResultsFunctionOverride.IsOverridden = true;
        Assert.Empty(page.Error);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
