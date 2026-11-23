using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.Serialization.ModuleDocument;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceImportTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-import-");
    private string Root => Path.Combine(temporary.FullName, "Root");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private WorkspacePageViewModel Open(Func<WorkspaceFolderRequest, string?> picker, bool empty = false)
    {
        var modules = empty ? Array.Empty<WorkspaceModule>() : [WorkspaceHierarchyTests.MakeModule(hierarchyKind.Root, Guid.NewGuid(), "existing", [],
            [WorkspaceHierarchyTests.MakeProject(hierarchyKind.Root, Guid.NewGuid(), "parent-project", [WorkspaceHierarchyTests.MakeRun("Parent project", "Trial")])])];
        WorkspaceFileStore.CreateDocument(Root, WorkspaceFixtures.Workspace(Guid.NewGuid(), "Research", "Root", modules), Token);
        var page = new WorkspacePageViewModel(() => { }, _ => Root,
            new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")), pickExistingFolder: picker);
        page.BrowseWorkspaceFolderCommand.Execute(null);
        return page;
    }
    private static WorkspaceTreeNode Parent(WorkspacePageViewModel page, WorkspaceEntityKind kind)
    {
        var node = page.Roots.Single();
        while (node.Kind != kind) node = node.Children.First();
        return node;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unfinished_imports_remain_editable_and_save_without_sealing(bool setup)
    {
        var family = setup ? hierarchyKind.Branch : hierarchyKind.Root;
        var source = new WorkspaceTreeNode(WorkspaceEntityKind.Workspace, "Library");
        var module = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Draft module", family: family); source.Children.Add(module);
        module.Editor!.LoadModule(WorkspaceHierarchyTests.ModuleSettings(setup));
        var project = new WorkspaceTreeNode(WorkspaceEntityKind.Project, "Draft project", family: family); module.Children.Add(project);
        project.Editor!.ResultsDatabaseName = "Own results";
        project.Editor.ResultsQueryOutputs.Single().FixedParameters.Single(value => value.Key == "ChannelCount").UseAsQueryKeyCommand.Execute(null);
        var run = new WorkspaceTreeNode(WorkspaceEntityKind.Run, "Unfinished", family: family); project.Children.Add(run);
        run.Editor!.ResultsParameters.Single(value => value.Key == "ChannelCount").AddValueCommand.Execute(null);
        run.Editor.ResultsParameters.Single().Values.Single().Text = "typing";
        var library = Path.Combine(temporary.FullName, "Library");
        WorkspaceFileStore.CreateDocument(library, source.ToDocument(), Token);
        var page = Open(_ => Path.Combine(library, module.Name), empty: true);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.True(page.HasUnsavedChanges);
        var imported = page.Selected!; var importedProject = imported.Children.Single();
        Assert.False(importedProject.IsSealed);
        Assert.NotEqual(project.Id, importedProject.Id);
        Assert.Equal("Own results", importedProject.Editor!.ResultsDatabaseName);
        Assert.Equal("typing", importedProject.Children.Single().Editor!.ResultsParameters.Single().Values.Single().Text);
        await page.SaveWorkspaceAsync();
        Assert.False(page.HasUnsavedChanges);
        Assert.Contains("Draft saved with configuration issues", page.Status);
        var reopened = WorkspaceTreeNode.FromDocument(WorkspaceFileStore.LoadWorkspace(Root).CreateEditDocument());
        Assert.False(reopened.Children[0].Children[0].IsSealed);
        Assert.Equal("typing", reopened.Children[0].Children[0].Children[0].Editor!.ResultsParameters.Single().Values.Single().Text);
    }
    internal static void WriteProject(string folder, hierarchyKind family = hierarchyKind.Root)
    {
        var name = Path.GetFileName(folder);
        var run = WorkspaceHierarchyTests.MakeRun(name, "Existing trial", family == hierarchyKind.Branch);
        var value = WorkspaceHierarchyTests.MakeProject(family, Guid.NewGuid(), name, [run]);
        var inputs = RunDto.fromDomain(value.ResolvedRuns()[0]);
        inputs.id = Guid.NewGuid();
        value = WorkspaceFixtures.WithRuns(value, [RunDto.toDomain(value.Snapshot(), inputs)]);
        Directory.CreateDirectory(Path.Combine(folder, "runs"));
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(value.Definition.id)), Sortopia.Core.Serialization.MessagePackSetup.serialize(WorkspaceDocumentModule.projectFile(value)));
        File.WriteAllBytes(Path.Combine(folder, "runs", WorkspaceDefinitionFiles.Name(value.Runs[0].id)), RunCodec.encodeRun(value.ResolvedRuns()[0]));
        File.WriteAllText(Path.Combine(folder, "results.txt"), "Keep results");
    }
    internal static void WriteModule(string folder, hierarchyKind family = hierarchyKind.Root, bool defineFunctions = true)
    {
        var name = Path.GetFileName(folder);
        Directory.CreateDirectory(folder);
        WriteProject(Path.Combine(folder, "child-project"), family);
        var child = WorkspaceFileStore.LoadProjectDocument(Path.Combine(folder, "child-project"), Token);
        var value = defineFunctions ? WorkspaceHierarchyTests.MakeModule(family, Guid.NewGuid(), name, [], [child])
            : WorkspaceFixtures.InheritedModule(Guid.NewGuid(), name, ModuleDefaults.configuration(family == hierarchyKind.Branch),
                ModuleOverridesDto.toDomain(family, new moduleOverridesDto()), [], [child]);
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(value.Definition.id)), Sortopia.Core.Serialization.MessagePackSetup.serialize(WorkspaceDocumentModule.moduleFile(value)));
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace, true, false, false)]
    [InlineData(WorkspaceEntityKind.Module, true, true, false)]
    [InlineData(WorkspaceEntityKind.Project, false, false, true)]
    [InlineData(WorkspaceEntityKind.Run, false, false, false)]
    public async Task Creation_and_import_commands_obey_the_selected_parent(WorkspaceEntityKind kind, bool modules, bool projects, bool runs)
    {
        var picks = 0;
        var page = Open(_ => { picks++; return null; });
        page.Selected = Parent(page, kind);
        Assert.Equal(modules, page.AddModuleCommand.CanExecute(null));
        Assert.Equal(projects, page.AddProjectCommand.CanExecute(null));
        Assert.Equal(runs, page.AddRunCommand.CanExecute(null));
        Assert.Equal(modules, page.AddExistingModuleCommand.CanExecute(null));
        Assert.Equal(projects, page.AddExistingProjectCommand.CanExecute(null));
        if (!modules) await page.AddExistingAsync(WorkspaceEntityKind.Module);
        if (!projects) await page.AddExistingAsync(WorkspaceEntityKind.Project);
        await page.AddExistingAsync(WorkspaceEntityKind.Run);
        Assert.Equal(0, picks);
        Assert.False(page.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(WorkspaceEntityKind.Workspace, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Module)]
    [InlineData(WorkspaceEntityKind.Module, WorkspaceEntityKind.Project)]
    public async Task Import_starts_in_the_correct_container_reads_the_subtree_and_writes_only_on_save(WorkspaceEntityKind parentKind, WorkspaceEntityKind childKind)
    {
        WorkspaceFolderRequest? request = null;
        string? selected = null;
        var page = Open(value => { request = value; return selected; });
        page.Selected = Parent(page, parentKind);
        var parentFolder = parentKind switch
        {
            WorkspaceEntityKind.Workspace => Root,
            WorkspaceEntityKind.Module => Path.Combine(Root, "existing"),
            _ => Path.Combine(Root, "existing", "parent-project")
        };
        var container = parentFolder;
        selected = Path.Combine(container, "imported-folder");
        if (childKind == WorkspaceEntityKind.Module) WriteModule(selected, defineFunctions: parentKind != WorkspaceEntityKind.Module);
        else WriteProject(selected);
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        var originalEntity = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(selected, childKind.ToString().ToLowerInvariant()));
        await page.AddExistingAsync(childKind);
        Assert.Empty(page.Error);
        Assert.Equal(container, request!.InitialDirectory);
        Assert.Equal(container, request.ContainerFolder);
        Assert.Equal(childKind, request.Kind);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
        Assert.Equal(originalEntity, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(selected, childKind.ToString().ToLowerInvariant())));
        Assert.True(page.HasUnsavedChanges);
        Assert.Equal("imported-folder", page.Selected!.Name);
        Assert.True(page.Selected.HasNamedFolder);
        var importedProject = childKind == WorkspaceEntityKind.Module ? page.Selected.Children.Single() : page.Selected;
        Assert.Equal("Existing trial", importedProject.Children.Single().Name);
        Assert.False(importedProject.Children.Single().HasFolder);
        await page.SaveWorkspaceAsync();
        Assert.Empty(page.Error);
        Assert.False(page.HasUnsavedChanges);
        var output = childKind == WorkspaceEntityKind.Module ? Path.Combine(selected, "child-project") : selected;
        Assert.Equal("Keep results", File.ReadAllText(Path.Combine(output, "results.txt")));
        page = new WorkspacePageViewModel(() => { }, _ => null, new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt")));
        Assert.Empty(page.Error);
        Assert.Contains(Parent(page, parentKind).Children, child => child.Name == "imported-folder");
    }

    [Fact]
    public async Task Import_uses_parent_folder_and_cancelling_picker_does_not_change_the_workspace()
    {
        WorkspaceFolderRequest? request = null;
        var page = Open(value => { request = value; return null; }, empty: true);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Equal(Root, request!.InitialDirectory);
        Assert.Equal(Root, request.ContainerFolder);
        Assert.True(Directory.Exists(request.ContainerFolder));
        Assert.False(page.HasUnsavedChanges);
        page.AddModuleCommand.Execute(null);
        Assert.True(page.AddExistingModuleCommand.CanExecute(null));
        Assert.True(page.AddExistingProjectCommand.CanExecute(null));
        await page.AddExistingAsync(WorkspaceEntityKind.Project);
        Assert.Equal(Root, request.InitialDirectory);
        Assert.Equal(Path.Combine(Root, "New module"), request.ContainerFolder);
    }

    [Theory]
    [InlineData("wrong type")]
    [InlineData("corrupt")]
    [InlineData("illegal children")]
    [InlineData("duplicate name")]
    [InlineData("duplicate folder")]
    [InlineData("corrupt run")]
    public async Task Invalid_imports_leave_the_draft_and_workspace_file_unchanged(string problem)
    {
        string? selected = null;
        var page = Open(_ => selected);
        selected = Path.Combine(Root, "candidate");
        switch (problem)
        {
            case "wrong type": WriteProject(selected); break;
            case "corrupt": Directory.CreateDirectory(selected); File.WriteAllBytes(WorkspaceTestFiles.DefinitionPath(selected, "module"), [0xc1]); break;
            case "illegal children": Directory.CreateDirectory(Path.Combine(selected, "runs")); break;
            case "duplicate name": selected = Path.Combine(temporary.FullName, "EXISTING"); WriteModule(selected); break;
            case "duplicate folder": selected = Path.Combine(Root, "existing"); break;
            case "corrupt run":
                var runsFolder = Directory.CreateDirectory(Path.Combine(selected, "P", "runs")).FullName;
                File.WriteAllBytes(Path.Combine(runsFolder, WorkspaceDefinitionFiles.Name(Guid.NewGuid())), [0xc1]); break;
        }
        var before = File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace"));
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.NotEmpty(page.Error);
        Assert.False(page.HasUnsavedChanges);
        Assert.Single(page.Roots.Single().Children);
        Assert.Equal(before, File.ReadAllBytes(WorkspaceTestFiles.DefinitionPath(Root, "workspace")));
    }

    [Fact]
    public async Task An_import_requires_metadata_to_identify_its_family()
    {
        var folder = Path.Combine(Root, "Plain module");
        var page = Open(_ => folder, empty: true);
        Directory.CreateDirectory(folder);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Contains("Missing module definition", page.Error);
        Assert.Empty(page.Roots.Single().Children);
        Assert.False(page.HasUnsavedChanges);
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(folder, "module")));
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
