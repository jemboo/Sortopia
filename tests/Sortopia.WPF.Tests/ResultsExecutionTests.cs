using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.ViewModels.Pages.ResultsExecution;
using Sortopia.WPF.Views.Pages.ResultsExecution;
using Sortopia.Workspaces.Persistence;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ResultsExecutionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-results-ui-");

    [Fact]
    public async Task Standalone_page_uses_folder_files_and_never_falls_back_to_other_runs()
    {
        var workspace = WorkspaceRunExecutionTests.Create(temporary.FullName, false);
        WorkspaceRunExecutionTests.SupplyQuerySpans(workspace);
        await workspace.SaveWorkspaceAsync(); WorkspaceRunExecutionTests.SelectRun(workspace);
        await workspace.GenerateRunParametersAsync();
        Assert.Empty(workspace.Error);
        var folder = workspace.ResultsExecution.Folder;
        using var page = new ResultsExecutionPageViewModel(() => { }, _ => null);
        await page.SetFolderAsync(folder, Guid.NewGuid());
        Assert.Equal(0, page.FileCount);
        Assert.False(page.ExecuteCommand.CanExecute(null));
        await page.SetFolderAsync(folder);
        Assert.Equal(1, page.FileCount);
        Assert.Null(page.SelectedRun!.Id);
        await page.CheckAsync();
        Assert.Equal(outputState.Pending, Assert.Single(page.Outputs).Value.Outputs.State);
        await page.ExecuteAsync();
        Assert.Empty(page.Error);
        Assert.Equal(runExecutionStatus.Completed, page.LastReport!.Status);
        Assert.Equal(outputState.Complete, Assert.Single(page.Outputs).Value.Outputs.State);
        var project = Path.GetDirectoryName(folder)!;
        Assert.False(Directory.Exists(Path.Combine(project, "preparations")));
        var dataFile = Directory.GetFiles(Path.Combine(project, "data", "results", "SortableTests"), "*.msgpack").Single();
        using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
        await inspector.OpenRootAsync(project);
        await inspector.InspectAsync(new InspectorFile(WorkspaceFileInspection.entry(dataFile)));
        Assert.Equal("Results data", inspector.Kind);
        Assert.Empty(inspector.Error);

        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var view = new ResultsExecutionPage { DataContext = page };
                view.Measure(new Size(1040, 780)); view.Arrange(new Rect(0, 0, 1040, 780)); view.UpdateLayout();
                if (Environment.GetEnvironmentVariable("SORTOPIA_RESULTS_SCREENSHOT") is { Length: > 0 } path)
                {
                    var bitmap = new RenderTargetBitmap(1040, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(path); encoder.Save(file);
                }
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        workspace.Cancel();
    }
    public void Dispose() => temporary.Delete(true);
}
