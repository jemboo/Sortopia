using System.ComponentModel;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.Views.Pages.FileInspector;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed partial class ResultsExecutionSetupQueriesTests
{
    [Fact]
    public async Task History_notifications_preserve_selection_recover_from_errors_and_stop_on_unload_or_file_change()
    {
        var workspace = await Generate(sgd: true, generations: 6, snapshotInterval: 2, tempSnapshotInterval: 1);
        var token = TestContext.Current.CancellationToken;
        var parameterPaths = Directory.GetFiles(workspace.RunParametersFolder, "*.msgpack");
        var path = parameterPaths[0];
        var other = parameterPaths[1];
        foreach (var parameter in new[] { path, other })
            Assert.Equal(runExecutionStatus.Completed, (await Task.Run(() => RunParameterExecution.executeFile(parameter, null!, token), token)).Status);
        var item = RunParameterFiles.loadFile(path);
        var project = RunParameterFiles.projectFolder(workspace.RunParametersFolder);
        var store = new SortopiaFileDb(project, item.ProjectId);
        var batches = RunOutputFiles.samples(item).Summaries.ToDictionary(batch => batch.Item1.Last(), batch => store.GetPath(batch.Item2.Reference));
        foreach (var generation in new[] { 4, 6 }) File.Move(batches[generation], batches[generation] + ".held");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
                    var view = new FileInspectorPage { DataContext = inspector };
                    await inspector.OpenFileAsync(project, path);
                    Assert.Empty(inspector.Error);
                    var movie = inspector.BinMovie!;
                    Assert.Equal(new[] { 0, 1, 2 }, movie.Generations);
                    movie.SelectedHistogram = "Unsuccessful";
                    movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 0);
                    movie.SelectedGeneration = 1;
                    var firstUpdate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    void Updated(object? _, PropertyChangedEventArgs e)
                    {
                        if (e.PropertyName == nameof(movie.Generations)) firstUpdate.TrySetResult();
                    }
                    movie.PropertyChanged += Updated;
                    inspector.StartHistoryMonitoring(action => dispatcher.BeginInvoke(action));
                    await firstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                    movie.PropertyChanged -= Updated;

                    async Task Change(Func<Task> action, Func<bool> complete)
                    {
                        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void Observe(object? _, PropertyChangedEventArgs __) { if (complete()) ready.TrySetResult(); }
                        movie.PropertyChanged += Observe; inspector.PropertyChanged += Observe;
                        try
                        {
                            await action();
                            if (complete()) ready.TrySetResult();
                            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
                        }
                        finally { movie.PropertyChanged -= Observe; inspector.PropertyChanged -= Observe; }
                    }
                    void CheckSelection()
                    {
                        Assert.Same(movie, inspector.BinMovie);
                        Assert.Equal("Unsuccessful", movie.SelectedHistogram);
                        Assert.Equal(0, movie.SelectedPool.Index);
                        Assert.Equal(1, movie.SelectedGeneration);
                        Assert.False(movie.IsPlaying);
                    }
                    await Change(() => { File.Move(batches[4] + ".held", batches[4]); return Task.CompletedTask; }, () => movie.Generations.Last() == 4);
                    CheckSelection(); Assert.Equal(5, movie.Generations.Count);

                    view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                    File.Move(batches[6] + ".held", batches[6]);
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Assert.Equal(4, movie.Generations.Last());
                    await Change(() => { inspector.StartHistoryMonitoring(action => dispatcher.BeginInvoke(action)); return Task.CompletedTask; }, () => movie.Generations.Last() == 6);
                    CheckSelection();

                    // Extending the saved run adds future file identities to the notification filter.
                    await Change(async () =>
                    {
                        workspace.Selected!.Editor!.SnapshotCount = "4";
                        await workspace.SaveWorkspaceAsync(); Assert.Empty(workspace.Error);
                        Assert.Equal(runExecutionStatus.Completed, (await Task.Run(() => RunParameterExecution.executeFile(path, null!, token), token)).Status);
                    }, () => movie.Generations.Last() == 8);
                    CheckSelection();

                    var good = File.ReadAllBytes(batches[4]);
                    await Change(() => { File.WriteAllBytes(batches[4], [0xc1]); return Task.CompletedTask; }, () => inspector.Error.Length > 0);
                    Assert.Contains("previously loaded history", inspector.Error);
                    CheckSelection(); Assert.Equal(8, movie.Generations.Last());
                    await Change(() => { File.WriteAllBytes(batches[4], good); return Task.CompletedTask; }, () => inspector.Error.Length == 0);
                    CheckSelection();

                    // Queued notifications from a previous file must not replace the newly selected movie.
                    inspector.StopHistoryMonitoring();
                    var queued = new System.Collections.Concurrent.ConcurrentQueue<Action>();
                    inspector.StartHistoryMonitoring(queued.Enqueue);
                    await inspector.OpenFileAsync(project, other);
                    var otherMovie = inspector.BinMovie!;
                    while (queued.TryDequeue(out var callback)) callback();
                    inspector.StopHistoryMonitoring();
                    await dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    Assert.Same(otherMovie, inspector.BinMovie);
                    Assert.NotSame(movie, otherMovie);
                    Assert.Equal(other, inspector.Selected!.FullPath);
                    Assert.Equal("Successful", otherMovie.SelectedHistogram);
                    Assert.Equal(0, otherMovie.SelectedGeneration);
                    Assert.Equal(6, otherMovie.Generations.Last());
                    view.DataContext = null;
                }
                catch (Exception e) { failure = e; }
                finally { dispatcher.InvokeShutdown(); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await Task.Run(thread.Join, token);
        workspace.Cancel();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
