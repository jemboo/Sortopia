using System.IO;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceNavigationTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-navigation-");
    private string Root => Path.Combine(temporary.FullName, "Root");
    private string Other => Path.Combine(temporary.FullName, "Other");
    private WorkspaceFolderSettings Settings => new(Path.Combine(temporary.FullName, "settings.txt"));
    private void CreateSavedRoot()
    {
        WorkspaceFileStore.CreateWorkspace(Root, workspace.create(Guid.NewGuid(), "Original", "Root", []), TestContext.Current.CancellationToken);
        Directory.CreateDirectory(Other);
        Settings.Save(Root);
    }

    [Theory]
    [InlineData(WorkspaceUnsavedChoice.Save)]
    [InlineData(WorkspaceUnsavedChoice.Discard)]
    [InlineData(WorkspaceUnsavedChoice.Cancel)]
    public async Task Leaving_prompts_and_saves_discards_or_keeps_the_draft(WorkspaceUnsavedChoice choice)
    {
        CreateSavedRoot();
        var prompts = 0;
        var left = false;
        var page = new WorkspacePageViewModel(() => left = true, _ => null, Settings, () => { prompts++; return choice; });
        page.WorkspaceName = "Edited";
        Assert.Equal(choice != WorkspaceUnsavedChoice.Cancel, await page.ReturnHomeAsync());
        Assert.Equal(1, prompts);
        Assert.Equal(choice != WorkspaceUnsavedChoice.Cancel, left);
        Assert.Equal(choice == WorkspaceUnsavedChoice.Save ? "Edited" : "Original", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
        Assert.Equal(choice == WorkspaceUnsavedChoice.Discard ? "Original" : "Edited", page.WorkspaceName);
        Assert.Equal(choice == WorkspaceUnsavedChoice.Cancel, page.HasUnsavedChanges);
    }

    [Theory]
    [InlineData(WorkspaceUnsavedChoice.Save)]
    [InlineData(WorkspaceUnsavedChoice.Discard)]
    [InlineData(WorkspaceUnsavedChoice.Cancel)]
    public async Task Changing_root_resolves_old_changes_before_updating_the_setting(WorkspaceUnsavedChoice choice)
    {
        CreateSavedRoot();
        var prompts = 0;
        var page = new WorkspacePageViewModel(() => { }, _ => Other, Settings, () => { prompts++; return choice; });
        page.WorkspaceName = "Edited";
        await page.BrowseWorkspaceFolderAsync();
        Assert.Equal(1, prompts);
        var expected = choice == WorkspaceUnsavedChoice.Cancel ? Root : Other;
        Assert.Equal(expected, page.WorkspaceFolder);
        Assert.Equal(expected, Settings.Load());
        Assert.Equal(choice == WorkspaceUnsavedChoice.Save ? "Edited" : "Original", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
        Assert.False(File.Exists(WorkspaceTestFiles.DefinitionPath(Other, "workspace")));
        Assert.Equal(choice == WorkspaceUnsavedChoice.Cancel, page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Same_folder_and_cancelled_picker_preserve_edits_without_prompting()
    {
        CreateSavedRoot();
        var picks = new Queue<string?>([null, Root]);
        var page = new WorkspacePageViewModel(() => { }, _ => picks.Dequeue(), Settings, () => throw new Exception("Unexpected prompt"));
        page.WorkspaceName = "Edited";
        var draft = page.Roots.Single();
        await page.BrowseWorkspaceFolderAsync();
        await page.BrowseWorkspaceFolderAsync();
        Assert.Same(draft, page.Roots.Single());
        Assert.Equal("Edited", page.WorkspaceName);
        Assert.True(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Choosing_a_new_empty_root_without_editing_does_not_prompt_or_create_files()
    {
        Directory.CreateDirectory(Root);
        var page = new WorkspacePageViewModel(() => { }, _ => Root, Settings, () => throw new Exception("Unexpected prompt"));
        await page.BrowseWorkspaceFolderAsync();
        Assert.False(page.HasUnsavedChanges);
        Assert.True(await page.TryLeaveAsync());
        Assert.Empty(Directory.EnumerateFileSystemEntries(Root));
    }

    [Fact]
    public async Task Failed_save_prevents_leaving_and_root_changes_and_preserves_edits_for_retry()
    {
        CreateSavedRoot();
        var left = false;
        var page = new WorkspacePageViewModel(() => left = true, _ => Other, Settings, () => WorkspaceUnsavedChoice.Save);
        page.WorkspaceName = "Edited";
        using (var locked = new FileStream(WorkspaceTestFiles.DefinitionPath(Root, "workspace"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await page.ReturnHomeAsync());
            Assert.False(left);
            await page.BrowseWorkspaceFolderAsync();
            Assert.Equal(Root, page.WorkspaceFolder);
            Assert.Equal(Root, Settings.Load());
            Assert.Equal("Edited", page.WorkspaceName);
            Assert.NotEmpty(page.Error);
            Assert.True(page.HasUnsavedChanges);
        }
        Assert.True(await page.ReturnHomeAsync());
        Assert.True(left);
        Assert.Equal("Edited", WorkspaceFileStore.LoadDefinition(Root).WorkspaceName);
    }

    [Fact]
    public async Task Invalid_changes_cannot_be_saved_when_leaving_but_can_be_discarded()
    {
        CreateSavedRoot();
        var choice = WorkspaceUnsavedChoice.Save;
        var page = new WorkspacePageViewModel(() => { }, _ => null, Settings, () => choice);
        page.WorkspaceName = "";
        Assert.False(await page.TryLeaveAsync());
        Assert.True(page.HasUnsavedChanges);
        Assert.NotEmpty(page.Error);
        choice = WorkspaceUnsavedChoice.Discard;
        Assert.True(await page.TryLeaveAsync());
        Assert.Equal("Original", page.WorkspaceName);
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task A_failed_root_setting_write_does_not_discard_the_old_draft()
    {
        CreateSavedRoot();
        var page = new WorkspacePageViewModel(() => { }, _ => Other, Settings, () => WorkspaceUnsavedChoice.Discard);
        page.WorkspaceName = "Edited";
        using var locked = new FileStream(Path.Combine(temporary.FullName, "settings.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        await page.BrowseWorkspaceFolderAsync();
        Assert.Equal(Root, page.WorkspaceFolder);
        Assert.Equal("Edited", page.WorkspaceName);
        Assert.True(page.HasUnsavedChanges);
        Assert.NotEmpty(page.Error);
    }

    public void Dispose() => temporary.Delete(recursive: true);
}
