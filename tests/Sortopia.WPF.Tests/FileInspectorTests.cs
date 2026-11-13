using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sortopia.Core.Serialization;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Sortopia.WPF.Views.Pages.FileInspector;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FileInspectorTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-inspector-ui-");
    private string Root => Path.Combine(temporary.FullName, "Workspace");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string CreateWorkspace()
    {
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Research", "Workspace", []), Token);
        return WorkspaceDefinitionFiles.Require(Root, "workspace");
    }
    private static InspectorFile Entry(string path) => new(WorkspaceFileInspection.entry(path));
    private static IEnumerable<InspectionNode> Flatten(IEnumerable<InspectionNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));

    [Fact]
    public async Task Inspect_refresh_and_errors_leave_saved_files_unchanged()
    {
        var path = CreateWorkspace();
        var bytes = File.ReadAllBytes(path);
        using var page = new FileInspectorPageViewModel(() => { }, _ => null);
        await page.OpenRootAsync(Root);
        await page.InspectAsync(Entry(path));
        Assert.Equal("Workspace definition", page.Kind);
        Assert.Contains(Flatten(page.Details), item => item.Name == "Workspace Name" && item.Value == "Research");
        var broken = Path.Combine(Root, "broken.msgpack");
        File.WriteAllBytes(broken, [0xc1]);
        await page.InspectAsync(Entry(broken));
        Assert.Contains("Could not inspect", page.Kind);
        Assert.Contains(broken, page.Error);
        Assert.Empty(page.Details);
        using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await page.InspectAsync(Entry(path));
            Assert.NotEmpty(page.Error);
        }
        await page.InspectAsync(Entry(path));
        Assert.Empty(page.Error);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(2, Directory.GetFiles(Root).Length);
    }

    [Fact]
    public async Task References_navigate_to_saved_children_and_refresh_keeps_the_selection()
    {
        var id = Guid.NewGuid();
        var module = workspaceModule.createInherited(id, "Sorting", "Sorting", ModuleDefaults.configuration(false), ModuleOverridesDto.toDomain(hierarchyKind.Root, new moduleOverridesDto()), [], []);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Research", "Workspace", [module]), Token);
        using var page = new FileInspectorPageViewModel(() => { }, _ => null);
        await page.OpenRootAsync(Root);
        await page.InspectAsync(Entry(WorkspaceDefinitionFiles.Require(Root, "workspace")));
        await page.OpenReferenceAsync(Assert.Single(page.References));
        Assert.Equal("Module definition", page.Kind);
        Assert.Equal(Path.Combine(Root, "Sorting"), page.CurrentFolder);
        Assert.True(page.CanGoUp);
        var path = page.Selected!.FullPath;
        var dto = WorkspaceDefinitionCodec.decodeModule(File.ReadAllBytes(path));
        dto.moduleName = "Changed on disk";
        File.WriteAllBytes(path, MessagePackSetup.serialize(dto));
        await page.RefreshAsync();
        Assert.Equal(path, page.Selected!.FullPath);
        Assert.Contains(Flatten(page.Details), item => item.Value == "Changed on disk");
        Assert.Contains(page.Details, item => item.Name == "Captured inherited defaults");
        Assert.Contains(page.Details, item => item.Name == "Local overrides");
    }

    [Fact]
    public async Task A_new_selection_supersedes_an_in_progress_read()
    {
        var path = CreateWorkspace();
        var other = Path.Combine(Root, "notes.txt");
        File.WriteAllText(other, "Not a configuration file");
        using var page = new FileInspectorPageViewModel(() => { }, _ => null);
        await page.OpenRootAsync(Root);
        var old = page.InspectAsync(Entry(path));
        var current = page.InspectAsync(Entry(other));
        await Task.WhenAll(old, current);
        Assert.Equal("Other file", page.Kind);
        Assert.Contains(page.Metadata, item => item.Name == "Name" && item.Value == "notes.txt");
        Assert.Empty(page.Error);
        Assert.False(page.IsInspecting);
    }

    [Fact]
    public void Shell_inspector_navigation_preserves_the_workspace_draft_and_returns_to_it()
    {
        CreateWorkspace();
        RunSta(() =>
        {
            var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
            settings.Save(Root);
            using var shell = new MainWindowViewModel(_ => null, settings);
            var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
            home.OpenWorkspaceCommand.Execute(null);
            var editor = Assert.IsType<WorkspacePageViewModel>(shell.CurrentPage);
            editor.WorkspaceName = "Unsaved title";
            editor.InspectFilesCommand.Execute(null);
            var inspector = Assert.IsType<FileInspectorPageViewModel>(shell.CurrentPage);
            inspector.ReturnCommand.Execute(null);
            Assert.Same(editor, shell.CurrentPage);
            Assert.True(editor.HasUnsavedChanges);
            Assert.Equal("Unsaved title", editor.WorkspaceName);
            Assert.Equal("Research", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
            Assert.True(SpinWait.SpinUntil(() => !inspector.IsBrowsing && !inspector.IsInspecting, TimeSpan.FromSeconds(5)));
        });
    }

    [Fact]
    public async Task Page_renders_file_metadata_and_configuration_with_a_resizable_layout()
    {
        var local = ModuleOverridesDto.toDomain(hierarchyKind.Root, new moduleOverridesDto {
            description = "Create reusable binary tests", resultsFunctionName = "sortable-tests.standard",
            resultsParameterDefaults = [new() { keyName = "ChannelCount", valueType = 1, value = "16" }, new() { keyName = "SortableDataFormat", valueType = 0, value = "Bools" }]
        });
        var module = workspaceModule.createInherited(Guid.NewGuid(), "Standard tests", "StandardTests", ModuleDefaults.configuration(false), local, [], []);
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Research", "Workspace", [module]), Token);
        var folder = Path.Combine(Root, "StandardTests");
        var path = WorkspaceDefinitionFiles.Require(folder, "module");
        using var model = new FileInspectorPageViewModel(() => { }, _ => null);
        await model.OpenRootAsync(folder);
        await model.InspectAsync(Entry(path));
        Assert.Empty(model.Error);
        RunSta(() =>
        {
            var page = new FileInspectorPage { DataContext = model };
            page.Measure(new Size(1080, 740));
            page.Arrange(new Rect(0, 0, 1080, 740));
            page.UpdateLayout();
            Assert.Equal(model.Entries.Count, ((ListView)page.FindName("FileList")).Items.Count);
            Assert.Equal(model.Details.Count, ((TreeView)page.FindName("FileDetails")).Items.Count);
            Assert.True(((ListView)page.FindName("FileList")).ActualWidth > 200);
            if (Environment.GetEnvironmentVariable("SORTOPIA_INSPECTOR_SCREENSHOT") is { Length: > 0 } output)
            {
                var image = new RenderTargetBitmap(1080, 740, 96, 96, PixelFormats.Pbgra32);
                image.Render(page);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var file = File.Create(output);
                encoder.Save(file);
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }
    public void Dispose() => temporary.Delete(true);
}
