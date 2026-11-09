using System.IO;
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
        parent = project.createInherited(parent.Id, parent.ProjectName, parent.FolderName, config, parent.LocalOverrides, [RunDto.toDomain(parent, inputs)]);
        var nested = WorkspaceModule.createInherited(Guid.NewGuid(), "Nested", "Nested", config, Defaults.inherited(branch), [], [parent]);
        return WorkspaceModule.createInherited(Guid.NewGuid(), "Top", "Top", Defaults.configuration(branch), local, [nested], []);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deserialization_resolves_modules_but_preserves_sealed_project_snapshots(bool branch)
    {
        var original = workspace.create(Guid.NewGuid(), "W", "Workspace", [Hierarchy(branch)]);
        var dto = WorkspaceDto.fromDomain(original);
        var top = dto.modules[0];
        top.localOverrides.description = "Changed at the top";
        top.localOverrides.resultsDatabaseName = "Shared results";
        top.localOverrides.resultsFunctionName = "New evaluator";
        top.localOverrides.resultsSortopiaDataTypes = [new() { kind = "SorterModelSet", value = "" }, new() { kind = "SorterSetEval", value = "" }];
        var nested = top.modules[0];
        nested.localOverrides.description = "Nested description";
        nested.projects[0].localOverrides.description = "";
        if (branch)
        {
            top.localOverrides.setupDatabaseName = "Shared setup";
            top.localOverrides.setupFunctionName = "New generator";
            top.localOverrides.setupDataTypes = [];
            nested.localOverrides.setupFunctionName = "Nested generator";
            nested.projects[0].runs[0].setupDatabaseNameOverride = "Run setup";
        }
        var restored = WorkspaceCodec.decodeWorkspace(MessagePackSetup.serialize(dto));
        var savedModule = restored.Modules[0].Modules[0];
        var project = savedModule.Projects[0];
        var run = project.Runs[0];
        Assert.Equal("Nested description", savedModule.Description);
        Assert.Equal("Keep every setting: α", project.Description);
        Assert.Equal("Keep every setting: α", run.Description);
        Assert.Equal("Results", run.ResultsDatabaseName);
        Assert.Equal("Evaluate", run.ResultsFunctionName);
        Assert.Equal(new[] { sortopiaDataType.NewSorterSetEval("") }, run.ResultsSortopiaDataTypes);
        Assert.Equal(new[] { "Seed" }, project.ResultsQueryParameterKeys);
        Assert.Equal("Evaluate", OverridesDto.fromDomain(project.LocalOverrides).resultsFunctionName);
        Assert.Null(OverridesDto.fromDomain(savedModule.LocalOverrides).resultsSortopiaDataTypes);
        if (branch)
        {
            var branchModule = ((WorkspaceModule.Branch)savedModule).Item;
            Assert.Equal("Shared setup", branchModule.SetupDatabaseName);
            Assert.Equal("Generate", run.SetupFunctionName.Value);
            Assert.Equal("Run setup", run.SetupDatabaseName.Value);
            Assert.Empty(branchModule.SetupSortopiaDataTypes);
            Assert.Empty(branchModule.SetupQueryParameterKeys);
        }
        // Clearing ancestor overrides cannot change an already sealed project.
        var cleared = WorkspaceDto.fromDomain(restored);
        cleared.modules[0].modules[0].localOverrides.description = null;
        cleared.modules[0].modules[0].projects[0].localOverrides.description = null;
        Assert.Equal("Keep every setting: α", WorkspaceDto.toDomain(cleared).Modules[0].Modules[0].Projects[0].Runs[0].Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_cascades_each_property_reports_its_source_and_persists_empty_list_overrides(bool branch)
    {
        var page = Open();
        AddModule(page, branch);
        var top = page.Selected!; top.Name = "Shared defaults";
        Assert.True(top.HasConfiguration);
        Assert.Equal("Module defaults", top.Editor!.Title);
        top.Editor.LoadProject(WorkspaceHierarchyTests.ProjectSettings(branch));
        AddModule(page, branch);
        var nested = page.Selected!; nested.Name = "Nested";
        page.AddProjectCommand.Execute(null);
        var parent = page.Selected!; parent.Name = "Project";
        Assert.True(parent.Editor!.ResultsFunctionOverride.IsInherited);
        Assert.Equal("Inherited from Shared defaults", parent.Editor.ResultsFunctionOverride.SourceLabel);
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        run.Editor!.LoadRun(RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("Project", "Run", branch)));
        Assert.Empty(page.Error);
        var notifications = new List<string?>();
        run.Editor.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        top.Editor.ResultsFunctionName = "Cascade";
        top.Name = "Renamed defaults";
        Assert.Equal("Cascade", run.Editor.ResultsFunctionName);
        Assert.Equal("Inherited from Renamed defaults", run.Editor.ResultsFunctionOverride.SourceLabel);
        Assert.Same(top.Editor, run.Editor.ResultsFunctionOverride.InheritedSource);
        Assert.NotEmpty(notifications);
        nested.Editor!.Description = "Closer description";
        Assert.Equal("Closer description", run.Editor.Description);
        Assert.Equal("Inherited from Nested", run.Editor.DescriptionOverride.SourceLabel);
        Assert.Same(nested.Editor, run.Editor.DescriptionOverride.InheritedSource);
        parent.Editor.Description = "";
        Assert.Equal("", run.Editor.Description);
        Assert.Equal("Inherited from Project", run.Editor.DescriptionOverride.SourceLabel);
        Assert.Same(parent.Editor, run.Editor.DescriptionOverride.InheritedSource);
        parent.Editor.DescriptionOverride.IsOverridden = false;
        Assert.Equal("Closer description", run.Editor.Description);
        if (branch)
        {
            nested.Editor.SetupFunctionName = "Closer generator";
            top.Editor.SetupDatabaseName = "Shared setup";
            Assert.Equal("Closer generator", run.Editor.SetupFunctionName);
            Assert.Equal("Inherited from Nested", run.Editor.SetupFunctionOverride.SourceLabel);
            Assert.Equal("Shared setup", run.Editor.SetupDatabaseName);
        }
        nested.Editor.ResultsQueryKeysOverride.IsOverridden = true;
        Assert.Equal("Seed", nested.Editor.ResultsQueryKeys.Single().Text); // Seed a new override from the effective list.
        nested.Editor.ResultsQueryKeys.Clear(); // Some [] clears the parent's requirements.
        run.Editor.ResultsParameters.Clear();
        Assert.Empty(page.Error);
        nested.Editor.OutputTypesOverride.IsOverridden = true;
        Assert.Equal("SorterSetEval", Assert.Single(nested.Editor.OutputTypes).Kind);
        nested.Editor.OutputTypes.Clear();
        Assert.Equal("None", run.Editor.OutputTypesSummary);
        if (branch)
        {
            nested.Editor.SetupDataTypesOverride.IsOverridden = true;
            nested.Editor.SetupDataTypes.Clear(); run.Editor.SetupParameters.Clear();
            Assert.Empty(page.Error);
        }
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0];
        var local = OverridesDto.fromDomain(saved.LocalOverrides);
        Assert.Empty(local.resultsQueryParameterKeys);
        Assert.Empty(local.resultsSortopiaDataTypes);
        if (branch) Assert.Empty(local.setupDataTypes);
        Assert.Empty(OverridesDto.fromDomain(saved.Projects[0].LocalOverrides).resultsSortopiaDataTypes);
        Assert.Equal("Cascade", saved.Projects[0].Runs[0].ResultsFunctionName);
        var reopened = Open();
        nested = reopened.Roots[0].Children[0].Children[0];
        run = nested.Children[0].Children[0];
        Assert.True(nested.Editor!.ResultsQueryKeysOverride.IsOverridden);
        Assert.Equal("None", run.Editor!.ResultsQueryKeysSummary);
        nested.Editor.ResultsQueryKeysOverride.IsOverridden = false;
        nested.Editor.OutputTypesOverride.IsOverridden = false;
        Assert.Equal("None", run.Editor.ResultsQueryKeysSummary);
        Assert.Empty(run.Editor.InheritedOutputTypes);
        Assert.True(run.Parent!.IsSealed);
        Assert.Empty(reopened.Error);
        if (branch) nested.Editor.SetupDataTypesOverride.IsOverridden = false;
        await reopened.SaveWorkspaceAsync();
        Assert.Empty(reopened.Error);
        local = OverridesDto.fromDomain(WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0].LocalOverrides);
        Assert.Null(local.resultsSortopiaDataTypes);
        Assert.Null(local.resultsQueryParameterKeys);
        if (branch) Assert.Null(local.setupDataTypes);
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
        settings.resultsFunctionName = "Destination executor";
        settings.description = "Destination description";
        var config = SettingsDto.toDomain(kind, settings);
        var destination = WorkspaceModule.createInherited(Guid.NewGuid(), "Destination", "Destination", config,
            branch ? configurationOverrides.NewBranchOverrides(Defaults.localBranch(((projectConfiguration.BranchProjectConfig)config).Item))
                : configurationOverrides.NewRootOverrides(Defaults.localRoot(((projectConfiguration.RootProjectConfig)config).Item)), [], []);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "W", "Workspace", [source, destination]), Token);
        var page = Open();
        var nested = page.Roots[0].Children[0].Children[0];
        nested.Editor!.Description = "Keep this local description";
        page.Selected = nested; page.CutCommand.Execute(null);
        page.Selected = page.Roots[0].Children[1]; await page.PasteAsync();
        Assert.Equal("Evaluate", nested.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep every setting: α", nested.Children[0].Children[0].Editor!.Description);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules[1].Modules[0];
        Assert.Equal(nested.Id, saved.Id);
        Assert.Equal("Results", saved.Projects[0].Runs[0].ResultsDatabaseName);
        Assert.Null(OverridesDto.fromDomain(saved.LocalOverrides).resultsDatabaseName);

        var importFolder = Path.Combine(Root, "modules", "Destination", "modules", "Nested");
        var importing = Open(_ => importFolder);
        importing.Selected = importing.Roots[0].Children[0];
        await importing.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(importing.Error);
        Assert.Equal("Evaluate", importing.Selected!.Children[0].Children[0].Editor!.ResultsFunctionName);
        Assert.Equal("Keep this local description", importing.Selected.Editor!.Description);
        await importing.SaveWorkspaceAsync(); Assert.Empty(importing.Error);
        var copied = WorkspaceFileStore.LoadDefinition(Root).Modules[0].Modules[0];
        Assert.NotEqual(saved.Id, copied.Id);
        Assert.Equal("Results", copied.Projects[0].Runs[0].ResultsDatabaseName);
        Assert.Null(OverridesDto.fromDomain(copied.LocalOverrides).resultsFunctionName);
        Assert.Equal("Evaluate", OverridesDto.fromDomain(copied.Projects[0].LocalOverrides).resultsFunctionName);
    }

    [Fact]
    public async Task Empty_modules_can_leave_functions_inherited_but_projects_require_resolved_values()
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null);
        Assert.Empty(page.Error);
        Assert.True(page.SaveWorkspaceCommand.CanExecute(null));
        var module = page.Selected!;
        page.AddProjectCommand.Execute(null);
        Assert.Contains("Set this value", page.Error);
        Assert.False(page.SaveWorkspaceCommand.CanExecute(null));
        page.EditProjectDefaultsCommand.Execute(null);
        Assert.Same(module, page.Selected);
        module.Editor!.ResultsFunctionName = "Evaluate";
        module.Editor.SetupFunctionName = "Generate";
        Assert.Empty(page.Error);
        module.Editor.ResultsFunctionOverride.IsOverridden = false;
        Assert.NotEmpty(page.Error);
        module.Editor.ResultsFunctionOverride.IsOverridden = true;
        Assert.Empty(page.Error);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
