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

public sealed class WorkspaceFamilyTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-families-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static ProjectDocument Project(hierarchyKind kind)
    {
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", kind == hierarchyKind.Branch));
        dto.id = Guid.NewGuid();
        return WorkspaceHierarchyTests.MakeProject(kind, Guid.NewGuid(), "p", [WorkspaceHierarchyTests.FromRunDto(dto, "P")]);
    }
    private static WorkspaceModule Module(hierarchyKind kind) => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), kind.ToString(),
        [WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "nested", [], [Project(kind)])], [Project(kind)]);

    [Fact]
    public void Modules_projects_and_runs_use_shared_types_for_both_families()
    {
        Assert.Equal(typeof(runDto[]), typeof(ProjectDocument).GetProperty("Runs")!.PropertyType);
        Assert.Equal(typeof(ModuleDocument[]), typeof(ModuleDocument).GetProperty("Modules")!.PropertyType);
        Assert.Equal(typeof(ProjectDocument[]), typeof(ModuleDocument).GetProperty("Projects")!.PropertyType);
        Assert.Null(typeof(ProjectDocument).GetProperty("Projects"));
    }

    [Fact]
    public void A_workspace_round_trips_both_families_and_guid_filenames()
    {
        var value = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Both families", "Workspace", [Module(hierarchyKind.Root), Module(hierarchyKind.Branch)]);
        WorkspaceFileStore.CreateDocument(Root, value, Token);
        var restored = WorkspaceFileStore.LoadWorkspace(Root);
        Assert.True(WorkspaceDocumentModule.sameContent(value, restored.CreateEditDocument()));
        Assert.Equal("root", restored.CreateEditDocument().Modules[0].Definition.moduleKind);
        Assert.Equal("branch", restored.CreateEditDocument().Modules[1].Definition.moduleKind);
        foreach (var module in restored.CreateEditDocument().Modules)
        {
            Assert.Equal(module.Definition.moduleKind, module.Modules.Single().Definition.moduleKind);
            var folder = Path.Combine(Root, module.Definition.moduleName);
            Assert.Equal(module.Definition.moduleKind, WorkspaceFileStore.LoadModuleDocument(folder, Token).Definition.moduleKind);
            var project = module.Projects.Single();
            Assert.Equal(module.Definition.moduleKind, project.Definition.projectKind);
            var run = restored.ResolveRun(project.Runs.Single().id).Run;
            Assert.Equal(module.Definition.moduleKind == "branch", run.HasSetup);
            var file = Path.Combine(folder, project.Definition.projectName, "runs", WorkspaceDefinitionFiles.Name(run.Id));
            Assert.Equal(run.Id, RunCodec.decodeRun(WorkspaceDefinitionResolution.projectSnapshot(project.Definition), File.ReadAllBytes(file)).Id);
        }
    }

    [Theory]
    [InlineData(hierarchyKind.Root, hierarchyKind.Branch)]
    [InlineData(hierarchyKind.Branch, hierarchyKind.Root)]
    public void Factories_and_wire_contracts_reject_mixed_families(hierarchyKind kind, hierarchyKind other)
    {
        var otherProject = Project(other);
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeProject(kind, Guid.NewGuid(), "p", otherProject.ResolvedRuns()));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "m", [], [otherProject]));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "m", [Module(other)], []));
        var projectDto = otherProject;
        projectDto.Definition.projectKind = kind.ToString().ToLowerInvariant();
        Assert.Throws<ArgumentException>(() => WorkspaceDocumentModule.validateProject(projectDto));
        var moduleDto = Module(other);
        moduleDto.Definition.moduleKind = kind.ToString().ToLowerInvariant();
        Assert.Throws<ArgumentException>(() => WorkspaceDocumentModule.validateModule(moduleDto));
    }

    private WorkspacePageViewModel Open(Func<WorkspaceFolderRequest, string?>? pick = null)
    {
        Directory.CreateDirectory(Root);
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Root);
        return new(() => { }, _ => null, settings, pickExistingFolder: pick);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_inherits_family_and_rejects_inputs_of_the_wrong_type(bool branch)
    {
        var page = Open();
        Assert.True(page.AddModuleCommand.CanExecute(null));
        Assert.True(page.AddBranchModuleCommand.CanExecute(null));
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        Assert.Equal(branch, page.Selected!.IsBranch);
        Assert.Equal(!branch, page.AddModuleCommand.CanExecute(null));
        Assert.Equal(branch, page.AddBranchModuleCommand.CanExecute(null));
        Assert.Equal(branch ? "New branch project" : "New root project", page.AddProjectLabel);
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        Assert.False(page.AddProjectCommand.CanExecute(null));
        Assert.False(page.AddExistingProjectCommand.CanExecute(null));
        page.AddRunCommand.Execute(null);
        var run = page.Selected!;
        Assert.Equal(branch ? "Branch run" : "Root run", run.KindLabel);
        Assert.Equal(branch, run.Editor!.IsBranch);
        Assert.True(run.Editor.DescriptionOverride.IsInherited);
        run.Editor.LoadRun(RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch)));
        var id = run.Id;
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        var saved = WorkspaceTestFiles.LoadRun(Root, document => document.Modules.Single().Projects.Single().Runs.Single());
        Assert.Equal(id, saved.Id);
        Assert.Equal(branch, saved.HasSetup);
        var rootFile = WorkspaceDefinitionFiles.Require(Root, "workspace");
        var before = File.ReadAllBytes(rootFile);
        run = page.Roots.Single().Children.Single().Children.Single().Children.Single();
        var incompatible = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", !branch));
        Assert.Throws<ArgumentException>(() => run.Editor!.LoadRun(incompatible));
        Assert.Equal(before, File.ReadAllBytes(rootFile));
    }

    [Fact]
    public async Task Imports_and_paste_require_a_matching_parent()
    {
        var source = Path.Combine(temporary.FullName, "Branch library");
        WorkspaceImportTests.WriteModule(source, hierarchyKind.Branch);
        var page = Open(_ => source);
        page.AddModuleCommand.Execute(null);
        var rootModule = page.Selected!;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Contains("cannot contain", page.Error);
        Assert.Empty(rootModule.Children);
        page.AddProjectCommand.Execute(null);
        WorkspaceHierarchyTests.ConfigureProject(page.Selected!);
        var rootProject = page.Selected!;
        page.Selected = page.Roots.Single();
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error);
        var branchModule = page.Selected!;
        Assert.True(branchModule.IsBranch);
        page.Selected = rootProject;
        page.CutCommand.Execute(null);
        page.Selected = branchModule;
        Assert.False(page.PasteCommand.CanExecute(null));
        Assert.Contains("matching parent", page.PasteHint);
        await page.PasteAsync();
        Assert.Same(rootModule, rootProject.Parent);
        page.Selected = rootProject;
        Assert.False(page.PasteCommand.CanExecute(null));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
