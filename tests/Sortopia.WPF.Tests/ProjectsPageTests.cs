using System.IO;
using System.Runtime.ExceptionServices;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels;
using Sortopia.WPF.ViewModels.Pages.Home;
using Sortopia.WPF.ViewModels.Pages.Projects;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ProjectsPageTests : IDisposable
{
    private readonly DirectoryInfo temp = Directory.CreateTempSubdirectory("Sortopia-projects-settings-");
    private string SettingsPath => Path.Combine(temp.FullName, "settings", "projects-folder.txt");
    private ProjectsFolderSettings Settings => new(SettingsPath);

    [Fact]
    public void Selecting_a_folder_persists_it_and_cancel_or_invalid_selection_keeps_it()
    {
        var folder = temp.CreateSubdirectory("Projects folder").FullName;
        var missing = Path.Combine(temp.FullName, "missing");
        var selections = new Queue<string?>([null, folder + Path.DirectorySeparatorChar, null, missing]);
        var initialFolders = new List<string?>();
        var page = new ProjectsPageViewModel(() => { }, current =>
        {
            initialFolders.Add(current);
            return selections.Dequeue();
        }, Settings);

        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Empty(page.ProjectsFolder);
        Assert.False(File.Exists(SettingsPath));
        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Equal(folder, page.ProjectsFolder);
        Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
        Assert.Empty(page.Error);
        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Equal(folder, page.ProjectsFolder);
        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Equal(folder, page.ProjectsFolder);
        Assert.Contains("Could not set", page.Error);
        Assert.Equal(new string?[] { null, null, folder, folder }, initialFolders);

        var reopened = new ProjectsPageViewModel(() => { }, _ => null, Settings);
        Assert.Equal(folder, reopened.ProjectsFolder);
        Assert.Empty(reopened.Error);
    }

    [Fact]
    public void Failed_save_preserves_the_previous_setting_and_can_be_retried()
    {
        var first = temp.CreateSubdirectory("First").FullName;
        var second = temp.CreateSubdirectory("Second").FullName;
        Settings.Save(first);
        var page = new ProjectsPageViewModel(() => { }, _ => second, Settings);

        using (var lockedSettings = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            page.BrowseProjectsFolderCommand.Execute(null);
            Assert.Equal(first, page.ProjectsFolder);
            Assert.Equal(first, Settings.Load());
            Assert.Contains("Could not set", page.Error);
        }
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(SettingsPath)!));
        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Equal(second, page.ProjectsFolder);
        Assert.Equal(second, Settings.Load());
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Unavailable_saved_folder_is_shown_and_can_be_replaced()
    {
        var missing = Path.Combine(temp.FullName, "unavailable");
        Settings.Save(missing);
        var replacement = temp.CreateSubdirectory("Available").FullName;
        var page = new ProjectsPageViewModel(() => { }, _ => replacement, Settings);
        Assert.Equal(missing, page.ProjectsFolder);
        Assert.Contains("unavailable", page.Error);
        page.BrowseProjectsFolderCommand.Execute(null);
        Assert.Equal(replacement, Settings.Load());
        Assert.Empty(page.Error);
    }

    [Fact]
    public void Unreadable_settings_report_an_error_without_preventing_navigation()
    {
        Directory.CreateDirectory(SettingsPath);
        bool returned = false;
        var page = new ProjectsPageViewModel(() => returned = true, _ => null, Settings);
        Assert.Empty(page.ProjectsFolder);
        Assert.Contains("Could not load", page.Error);
        page.ReturnHomeCommand.Execute(null);
        Assert.True(returned);
    }

    [Fact]
    public void Projects_selection_stays_on_the_page_and_survives_navigation()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var folder = temp.CreateSubdirectory("Projects").FullName;
                using var shell = new MainWindowViewModel(_ => folder, Settings);
                var home = Assert.IsType<HomePageViewModel>(shell.CurrentPage);
                home.OpenProjectsCommand.Execute(null);
                var projects = Assert.IsType<ProjectsPageViewModel>(shell.CurrentPage);
                projects.BrowseProjectsFolderCommand.Execute(null);
                Assert.Same(projects, shell.CurrentPage);
                Assert.Empty(Directory.EnumerateFileSystemEntries(folder));
                projects.ReturnHomeCommand.Execute(null);
                Assert.Same(home, shell.CurrentPage);
                home.OpenProjectsCommand.Execute(null);
                Assert.Same(projects, shell.CurrentPage);
                Assert.Equal(folder, projects.ProjectsFolder);
            }
            catch (Exception exception) { error = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose() => temp.Delete(recursive: true);
}
