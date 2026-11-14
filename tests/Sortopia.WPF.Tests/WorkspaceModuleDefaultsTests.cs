using System.IO;
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
        var local = branch ? configurationOverrides.NewBranchOverrides(Defaults.localBranch(((projectConfiguration.BranchProjectConfig)config).Item))
            : configurationOverrides.NewRootOverrides(Defaults.localRoot(((projectConfiguration.RootProjectConfig)config).Item));
        var parent = project.createInherited(Guid.NewGuid(), "P", "P", config, Defaults.inherited(branch), []);
        var inputs = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch));
        inputs.parentProjectId = parent.Id;
        inputs.resultsParameterSpans = []; inputs.setupParameterSpans = [];
        parent = project.createInherited(parent.Id, parent.ProjectName, parent.FolderName, config, parent.LocalOverrides, [RunDto.toDomain(parent, inputs)]);
        var nested = WorkspaceModule.createInherited(Guid.NewGuid(), "Nested", "Nested", ModuleDefaults.fromProject(config), ModuleDefaults.fromOverrides(Defaults.inherited(branch)), [], [parent]);
        var moduleLocal = ModuleOverridesDto.fromDomain(ModuleDefaults.fromOverrides(local));
        moduleLocal.resultsFunctionName = "sortable-tests.standard"; moduleLocal.setupFunctionName = branch ? "sortable-tests-with-prefix.standard" : null;
        return WorkspaceModule.createInherited(Guid.NewGuid(), "Top", "Top", ModuleDefaults.configuration(branch), ModuleOverridesDto.toDomain(kind, moduleLocal), [nested], []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialization_resolves_modules_but_preserves_sealed_project_snapshots(bool branch)
    {
        var original = workspace.create(Guid.NewGuid(), "W", "Workspace", [Hierarchy(branch)]);
        var dto = WorkspaceDto.fromDomain(original);
        dto.modules[0].localOverrides.description = "Changed ancestor";
        dto.modules[0].localOverrides.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiEvaluationInputs;
        dto.modules[0].modules[0].localOverrides.description = "Nested description";
        var restored = WorkspaceCodec.decodeWorkspace(MessagePackSetup.serialize(dto));
        var nested = restored.Modules[0].Modules[0]; var saved = nested.Projects[0];
        Assert.Equal("Nested description", nested.Description);
        Assert.Equal("Keep every setting: α", saved.Description);
        Assert.Equal(Sortopia.Functions.ResultsFunctionNames.standardSortableTests, saved.ResultsFunctionName);
        Assert.Equal("1", saved.Snapshot.Results.FunctionVersion);
        Assert.Null(typeof(moduleOverridesDto).GetProperty("resultsSortopiaDataTypes"));
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsFunctionName"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_cascades_allowed_defaults_and_reports_their_source(bool branch)
    {
        var page = Open(); AddModule(page, branch); var top = page.Selected!;
        top.Editor!.LoadProject(WorkspaceHierarchyTests.ProjectSettings(branch));
        AddModule(page, branch); var child = page.Selected!;
        child.Editor!.Description = "Local description";
        page.AddProjectCommand.Execute(null); var project = page.Selected!;
        page.AddRunCommand.Execute(null); var run = page.Selected!;
        WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, false); if (branch) WorkspaceParameterSetsPreviewTests.ConfigureExplicitInputs(run, true);
        Assert.Same(top.Editor, run.Editor!.OutputTypesOverride.InheritedSource);
        Assert.Same(child.Editor, run.Editor.DescriptionOverride.InheritedSource);
        Assert.False(project.Editor!.OutputTypesOverride.CanOverride);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceTestFiles.LoadRuntimeSnapshot(Root).Modules[0].Modules[0];
        Assert.Equal("Local description", saved.Projects[0].Description);
        Assert.Null(ModuleOverridesDto.fromDomain(saved.LocalOverrides).resultsFunctionName);
        Assert.Null(typeof(moduleOverridesDto).GetProperty("resultsQueryParameterKeys"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moving_and_copying_modules_keeps_sealed_project_settings(bool branch)
    {
        var source = Hierarchy(branch);
        var kind = source.Kind;
        var settings = WorkspaceHierarchyTests.ProjectSettings(branch);
        settings.resultsDatabaseName = "Destination results";
        settings.resultsFunctionName = Sortopia.Functions.ResultsFunctionNames.standardMssiEvaluationInputs;
        settings.description = "Destination description";
        var config = SettingsDto.toDomain(kind, settings);
        var destination = WorkspaceModule.createInherited(Guid.NewGuid(), "Destination", "Destination", ModuleDefaults.fromProject(config),
            ModuleDefaults.fromOverrides(branch ? configurationOverrides.NewBranchOverrides(Defaults.localBranch(((projectConfiguration.BranchProjectConfig)config).Item))
                : configurationOverrides.NewRootOverrides(Defaults.localRoot(((projectConfiguration.RootProjectConfig)config).Item))), [], []);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "W", "Workspace", [source, destination]), Token);
        var page = Open();
        var nested = page.Roots[0].Children[0].Children[0];
        nested.Editor!.Description = "Keep this local description";
        page.Selected = nested; page.CutCommand.Execute(null);
        page.Selected = page.Roots[0].Children[1]; await page.PasteAsync();
        Assert.Equal("sortable-tests.standard", nested.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep every setting: α", nested.Children[0].Children[0].Editor!.Description);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[1].Modules[0];
        Assert.Equal(nested.Id, saved.Id);
        Assert.Equal("Results", saved.Projects[0].Runs[0].ResultsDatabaseName);
        Assert.Null(typeof(moduleSettingsDto).GetProperty("resultsDatabaseName"));

        var importFolder = Path.Combine(Root, "Destination", "Nested");
        var importing = Open(_ => importFolder);
        importing.Selected = importing.Roots[0].Children[0];
        await importing.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(importing.Error);
        Assert.Equal("sortable-tests.standard", importing.Selected!.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep this local description", importing.Selected.Editor!.Description);
        await importing.SaveWorkspaceAsync(); Assert.Empty(importing.Error);
        var copied = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0];
        Assert.NotEqual(saved.Id, copied.Id);
        Assert.Equal("Results", copied.Projects[0].Runs[0].ResultsDatabaseName);
        Assert.Null(ModuleOverridesDto.fromDomain(copied.LocalOverrides).resultsFunctionName);
        Assert.Null(typeof(projectOverridesDto).GetProperty("resultsFunctionName"));
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
