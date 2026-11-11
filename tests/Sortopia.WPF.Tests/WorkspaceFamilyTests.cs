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

public sealed class WorkspaceFamilyTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-families-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static project Project(hierarchyKind kind)
    {
        var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", kind == hierarchyKind.Branch));
        dto.id = Guid.NewGuid();
        return WorkspaceHierarchyTests.MakeProject(kind, Guid.NewGuid(), "P", "p", [WorkspaceHierarchyTests.FromRunDto(dto, "P")]);
    }
    private static WorkspaceModule Module(hierarchyKind kind) => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), kind.ToString(), kind.ToString(),
        [WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "Nested", "nested", [], [Project(kind)])], [Project(kind)]);

    [Fact]
    public void Concrete_types_express_the_allowed_children()
    {
        Assert.Equal(typeof(rootRun[]), typeof(rootProject).GetProperty("Runs")!.PropertyType);
        Assert.Equal(typeof(branchRun[]), typeof(branchProject).GetProperty("Runs")!.PropertyType);
        Assert.Equal(typeof(rootModule[]), typeof(rootModule).GetProperty("Modules")!.PropertyType);
        Assert.Equal(typeof(branchModule[]), typeof(branchModule).GetProperty("Modules")!.PropertyType);
        Assert.Equal(typeof(rootProject[]), typeof(rootModule).GetProperty("Projects")!.PropertyType);
        Assert.Equal(typeof(branchProject[]), typeof(branchModule).GetProperty("Projects")!.PropertyType);
        Assert.Null(typeof(rootProject).GetProperty("Projects"));
        Assert.Null(typeof(branchProject).GetProperty("Projects"));
    }

    [Fact]
    public void A_workspace_round_trips_both_families_and_guid_filenames()
    {
        var value = workspace.create(Guid.NewGuid(), "Both families", "Workspace", [Module(hierarchyKind.Root), Module(hierarchyKind.Branch)]);
        WorkspaceFileStore.CreateWorkspace(Root, value, Token);
        var restored = WorkspaceFileStore.LoadDefinition(Root);
        Assert.Equal(WorkspaceCodec.encodeWorkspace(value), WorkspaceCodec.encodeWorkspace(restored));
        Assert.True(restored.Modules[0].IsRoot);
        Assert.True(restored.Modules[1].IsBranch);
        foreach (var module in restored.Modules)
        {
            Assert.Equal(module.Kind, module.Modules.Single().Kind);
            var folder = Path.Combine(Root, "modules", module.FolderName);
            Assert.Equal(module.Kind, WorkspaceFileStore.LoadModuleFolder(folder, Token).Kind);
            var project = module.Projects.Single();
            Assert.Equal(module.Kind, project.Kind);
            var run = project.Runs.Single();
            Assert.Equal(module.Kind == hierarchyKind.Branch, run.HasSetup);
            var file = Path.Combine(folder, "projects", project.FolderName, "runs", WorkspaceDefinitionFiles.Name(run.Id));
            Assert.Equal(run.Id, WorkspaceCodec.decodeRun(project, File.ReadAllBytes(file)).Id);
        }
    }

    [Theory]
    [InlineData(hierarchyKind.Root, hierarchyKind.Branch)]
    [InlineData(hierarchyKind.Branch, hierarchyKind.Root)]
    public void Factories_and_wire_contracts_reject_mixed_families(hierarchyKind kind, hierarchyKind other)
    {
        var otherProject = Project(other);
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeProject(kind, Guid.NewGuid(), "P", "p", otherProject.Runs));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "M", "m", [], [otherProject]));
        Assert.Throws<ArgumentException>(() => WorkspaceHierarchyTests.MakeModule(kind, Guid.NewGuid(), "M", "m", [Module(other)], []));
        var projectDto = ProjectDto.fromDomain(otherProject);
        projectDto.projectKind = kind.ToString().ToLowerInvariant();
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeProject(MessagePackSetup.serialize(projectDto)));
        var moduleDto = ModuleDto.fromDomain(Module(other));
        moduleDto.moduleKind = kind.ToString().ToLowerInvariant();
        Assert.Throws<ArgumentException>(() => WorkspaceCodec.decodeModule(MessagePackSetup.serialize(moduleDto)));
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
        var saved = WorkspaceFileStore.LoadDefinition(Root).Modules.Single().Projects.Single().Runs.Single();
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
        WorkspaceImportTests.WriteModule(source, "Branch library", hierarchyKind.Branch);
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
