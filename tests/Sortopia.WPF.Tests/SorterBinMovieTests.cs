using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.FSharp.Core;
using Sortopia.Core.Serialization;
using Sortopia.Evaluation.Sgd;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Workspaces.Persistence;
using Sortopia.WPF.Models;
using Sortopia.WPF.ViewModels.Pages.FileInspector;
using Sortopia.WPF.ViewModels.Shared.Controls;
using Sortopia.WPF.Views.Pages.FileInspector;
using Sortopia.WPF.Views.Shared.Controls;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SorterBinMovieTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-histogram-inspector-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid TestsId = Guid.NewGuid();
    private static sgdPoolStatistics Row(int generation, int pool, sorterLengthHistogramBin[] successful, sorterLengthHistogramBin[] unsuccessful)
    {
        var count = successful.Sum(bin => bin.Count) + unsuccessful.Sum(bin => bin.Count);
        return new sgdPoolStatistics {
            Generation = generation, PoolIndex = pool, PoolId = new Guid($"00000000-0000-0000-0000-{pool + 1:000000000000}"),
            MemberCount = count, SuccessfulCount = successful.Sum(bin => bin.Count), SuccessFraction = (double)successful.Sum(bin => bin.Count) / count,
            RawCeLength = 100, SuccessfulLengthHistogram = FSharpOption<sorterLengthHistogramBin[]>.Some(successful),
            UnsuccessfulLengthHistogram = FSharpOption<sorterLengthHistogramBin[]>.Some(unsuccessful)
        };
    }
    private static sorterPoolSummarySet Summary() => new(TestsId, [
        Row(0, 0, [new(45, 8, 2), new(52, 10, 1)], [new(58, 12, 1)]),
        Row(0, 1, [new(45, 8, 3)], [new(52, 10, 2)]),
        Row(10, 0, [new(45, 8, 1), new(48, 9, 4)], []),
        Row(10, 1, [new(48, 9, 2)], [new(52, 10, 1)]),
        Row(40, 0, [], [new(48, 9, 3)]),
        Row(40, 1, [new(48, 9, 2)], [new(52, 10, 1)]) ]);

    private string Save(sorterPoolSummarySet summary, string name = "Project")
    {
        var folder = Path.Combine(temporary.FullName, name);
        var projectId = Guid.NewGuid();
        var store = new SortopiaFileDb(folder, projectId);
        var key = DataKey.create(projectId, "Results", false, "sorter-sgd.standard.msrs.simple", "1",
            sortopiaDataType.NewSorterPoolSummarySet(""), [], parameterSet.fromBindings([]), []);
        var saved = ((ISortopiaDb)store).GetOrCreate(key,
            FuncConvert.FromFunc(() => FSharpResult<sortopiaStorageData, string>.NewOk(sortopiaStorageData.NewSorterPoolSummarySet(summary))), Token);
        Assert.True(saved.IsOk, saved.IsError ? saved.ErrorValue : "");
        return store.GetPath(key.Reference);
    }
    private static InspectorFile Entry(string path) => new(WorkspaceFileInspection.entry(path));

    [Fact]
    public void Population_pool_and_sparse_generation_selections_show_exact_weighted_bins()
    {
        using var movie = new SorterBinMovieViewModel(Summary());
        Assert.Equal(new[] { 0, 10, 40 }, movie.Generations);
        Assert.Equal("Successful", movie.SelectedHistogram);
        Assert.Null(movie.SelectedPool.Index);
        Assert.Equal(5, movie.Visits.Counts[new Point(8, 45)]);
        Assert.Equal(1, movie.Visits.Counts[new Point(10, 52)]);
        Assert.Equal(6, movie.Visits.Counts.Values.Sum());
        Assert.Equal(6, movie.FrequencyMaximum); // Generation 10 contains a bin with six members.
        var bounds = movie.AllBounds;
        movie.SelectedGeneration = 10;
        Assert.Equal(1, movie.SelectedFrameIndex);
        Assert.Equal(6, movie.Visits.Counts[new Point(9, 48)]);
        Assert.Equal(bounds, movie.AllBounds);
        movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 0);
        Assert.Equal(4, movie.Visits.Counts[new Point(9, 48)]);
        movie.SelectedHistogram = "Unsuccessful";
        Assert.Empty(movie.Visits.Counts);
        Assert.Equal("No unsuccessful sorters.", movie.EmptyMessage);
        movie.SelectedGeneration = 0;
        Assert.Equal(1, Assert.Single(movie.Visits.Counts).Value);
        Assert.Equal(new Point(12, 58), Assert.Single(movie.Visits.Counts).Key);
        movie.SelectBinCommand.Execute(new Point(12, 58));
        Assert.Contains("Stage count 12", movie.SelectionText);
        Assert.Contains("CE count 58", movie.SelectionText);
        Assert.Contains("1 sorters", movie.SelectionText);
        movie.SelectedGeneration = 999; // Only actual saved generations are selectable.
        Assert.Equal(0, movie.SelectedGeneration);
        movie.NextCommand.Execute(null);
        Assert.Equal(10, movie.SelectedGeneration);
        Assert.Null(movie.SelectedPosition);
    }

    [Fact]
    public void Missing_histograms_are_not_displayed_as_empty_or_partial_populations()
    {
        var summary = Summary();
        summary.Rows[0].SuccessfulLengthHistogram = null;
        summary.Rows[0].UnsuccessfulLengthHistogram = null;
        using var movie = new SorterBinMovieViewModel(summary);
        Assert.Empty(movie.Visits.Counts);
        Assert.Contains("not recorded for 1 of 2", movie.Status);
        Assert.Equal("Histograms unavailable.", movie.EmptyMessage);
        Assert.False(movie.SelectBinCommand.CanExecute(null));
        movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 1);
        Assert.Equal(3, Assert.Single(movie.Visits.Counts).Value);
        movie.SelectedGeneration = 40;
        movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 0);
        Assert.Empty(movie.Visits.Counts);
        Assert.Equal("No successful sorters.", movie.EmptyMessage);
        Assert.DoesNotContain("not recorded", movie.Status);
        using var unavailable = new SorterBinMovieViewModel(new(TestsId, [summary.Rows[0]]));
        Assert.False(unavailable.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public void Counted_bins_do_not_expand_members_or_overflow_int_totals()
    {
        using var movie = new SorterBinMovieViewModel(new(TestsId, [
            Row(0, 0, [new(1000000, 100000, int.MaxValue)], []),
            Row(0, 1, [new(1000000, 100000, int.MaxValue)], [])]));
        Assert.Equal(2L * int.MaxValue, Assert.Single(movie.Visits.Counts).Value);
        Assert.Null(movie.Visits.Current);
        Assert.Equal(2L * int.MaxValue, movie.FrequencyMaximum);
    }

    [Fact]
    public void Updating_history_preserves_paused_selection_by_generation_through_backfill_and_new_frames() => RunSta(() =>
    {
        var complete = Summary();
        using var movie = new SorterBinMovieViewModel(new(TestsId, complete.Rows.Where(row => row.Generation != 10).ToArray()));
        var view = new SorterBinMovie { DataContext = movie };
        void Layout()
        {
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            view.Measure(new Size(700, 600)); view.Arrange(new Rect(0, 0, 700, 600)); view.UpdateLayout();
        }
        Layout();
        movie.SelectedHistogram = "Unsuccessful";
        movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 0);
        movie.SelectedGeneration = 40;
        movie.SelectBinCommand.Execute(new Point(9, 48));
        var pool = movie.SelectedPool;
        var extended = new sorterPoolSummarySet(TestsId, [.. complete.Rows, Row(80, 0, [], [new(100, 50, 7)])]);
        movie.UpdateSummary(extended);
        Layout();
        Assert.Equal(new[] { 0, 10, 40, 80 }, movie.Generations);
        Assert.Equal(40, movie.SelectedGeneration); Assert.Equal(2, movie.SelectedFrameIndex);
        Assert.Equal("Unsuccessful", movie.SelectedHistogram); Assert.Same(pool, movie.SelectedPool);
        Assert.False(movie.IsPlaying);
        Assert.Equal(3, Assert.Single(movie.Visits.Counts).Value);
        Assert.Equal(new Point(9, 48), movie.SelectedPosition);
        Assert.Equal(7, movie.FrequencyMaximum); Assert.True(movie.AllBounds.Contains(new Point(50, 100)));
        Assert.Equal(40, ((ComboBox)view.FindName("GenerationSelector")).SelectedItem);
        Assert.Same(pool, ((ComboBox)view.FindName("PoolSelector")).SelectedItem);
        Assert.Equal("Unsuccessful", ((ComboBox)view.FindName("HistogramSelector")).SelectedItem);
        Assert.Equal(2, ((Slider)view.FindName("GenerationSlider")).Value);
        movie.UpdateSummary(new(TestsId, complete.Rows.Where(row => row.Generation < 40).ToArray()));
        Layout();
        Assert.Equal(10, movie.SelectedGeneration); // Only a removed frame needs a fallback.
        Assert.Equal("Unsuccessful", movie.SelectedHistogram); Assert.Same(pool, movie.SelectedPool);
    });

    [Fact]
    public void Updating_history_keeps_active_playback_and_continues_into_appended_frames() => RunSta(() =>
    {
        using var movie = new SorterBinMovieViewModel(Summary());
        movie.SelectedHistogram = "Unsuccessful";
        movie.SelectedPool = movie.PoolOptions.Single(pool => pool.Index == 1);
        movie.SelectedGeneration = 10;
        movie.PlayPauseCommand.Execute(null);
        movie.UpdateSummary(new(TestsId, [.. Summary().Rows, Row(80, 1, [], [new(48, 9, 3)])]));
        Assert.True(movie.IsPlaying); Assert.Equal(10, movie.SelectedGeneration);
        Assert.Equal("Unsuccessful", movie.SelectedHistogram); Assert.Equal(1, movie.SelectedPool.Index);
        movie.AdvanceFrame(); Assert.Equal(40, movie.SelectedGeneration); Assert.True(movie.IsPlaying);
        movie.AdvanceFrame(); Assert.Equal(80, movie.SelectedGeneration); Assert.False(movie.IsPlaying);
        Assert.Throws<InvalidOperationException>(() => movie.UpdateSummary(new(Guid.NewGuid(), Summary().Rows)));
        Assert.Equal(80, movie.SelectedGeneration); Assert.Equal(4, movie.Generations.Count);
    });

    [Fact]
    public void Playback_advances_saved_frames_stops_at_end_and_pauses_on_seek_and_unload() => RunSta(() =>
    {
        using var movie = new SorterBinMovieViewModel(Summary());
        var view = new SorterBinMovie { DataContext = movie };
        movie.PlayPauseCommand.Execute(null);
        Assert.True(movie.IsPlaying);
        movie.AdvanceFrame(); Assert.Equal(10, movie.SelectedGeneration);
        movie.SelectedFrameIndex = movie.SelectedFrameIndex; // A binding echo must not interrupt playback.
        Assert.True(movie.IsPlaying);
        movie.AdvanceFrame(); Assert.Equal(40, movie.SelectedGeneration); Assert.False(movie.IsPlaying);
        Assert.False(movie.NextCommand.CanExecute(null));
        movie.PlayPauseCommand.Execute(null); Assert.Equal(0, movie.SelectedGeneration);
        movie.SelectedGeneration = 10; Assert.False(movie.IsPlaying);
        movie.PlayPauseCommand.Execute(null);
        view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); Assert.False(movie.IsPlaying);
        movie.PlayPauseCommand.Execute(null);
        view.DataContext = null; Assert.False(movie.IsPlaying);
        movie.Dispose(); Assert.False(movie.PlayPauseCommand.CanExecute(null));
    });

    [Fact]
    public async Task Selecting_summary_files_loads_histograms_and_clears_them_on_other_files_errors_or_newer_selection()
    {
        var path = Save(Summary());
        var bytes = File.ReadAllBytes(path);
        using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
        await inspector.OpenRootAsync(temporary.FullName);
        await inspector.InspectAsync(Entry(path));
        Assert.Empty(inspector.Error); Assert.True(inspector.HasBinMovie);
        Assert.Equal(5, inspector.BinMovie!.Visits.Counts[new Point(8, 45)]);
        Assert.Contains("checksum and summary contents validated", inspector.Description);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        var previous = inspector.BinMovie;
        var notes = Path.Combine(temporary.FullName, "notes.txt"); File.WriteAllText(notes, "keep");
        var old = inspector.InspectAsync(Entry(path));
        var current = inspector.InspectAsync(Entry(notes));
        await Task.WhenAll(old, current);
        Assert.False(inspector.HasBinMovie); Assert.Null(inspector.BinMovie);
        Assert.False(previous.NextCommand.CanExecute(null));
        var envelope = MessagePackSetup.deserialize<dataEnvelopeDto>(bytes);
        envelope.Payload[^1] ^= 0x01;
        File.WriteAllBytes(path, MessagePackSetup.serialize(envelope));
        await inspector.InspectAsync(Entry(path));
        Assert.Contains("checksum", inspector.Error);
        Assert.False(inspector.HasBinMovie);
        File.WriteAllBytes(path, bytes);
        await inspector.InspectAsync(Entry(path)); Assert.True(inspector.HasBinMovie);
        var renamed = Path.Combine(Path.GetDirectoryName(path)!, Guid.NewGuid() + ".msgpack");
        File.Copy(path, renamed);
        await inspector.InspectAsync(Entry(renamed));
        Assert.NotEmpty(inspector.Error); Assert.False(inspector.HasBinMovie);
    }

    [Fact]
    public async Task Legacy_summary_files_remain_inspectable_with_an_unavailable_histogram_message()
    {
        var summary = Summary();
        foreach (var row in summary.Rows) { row.SuccessfulLengthHistogram = null; row.UnsuccessfulLengthHistogram = null; }
        var path = Save(summary);
        using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
        await inspector.OpenRootAsync(temporary.FullName);
        await inspector.InspectAsync(Entry(path));
        Assert.Empty(inspector.Error); Assert.True(inspector.HasBinMovie);
        Assert.Equal("Histograms unavailable.", inspector.BinMovie!.EmptyMessage);
        Assert.False(inspector.BinMovie.PlayPauseCommand.CanExecute(null));
    }

    [Fact]
    public async Task Inspector_renders_the_bound_stage_by_ce_histogram_and_its_selectors()
    {
        var path = Save(Summary());
        using var inspector = new FileInspectorPageViewModel(() => { }, _ => null);
        await inspector.OpenRootAsync(Path.GetDirectoryName(path)!);
        await inspector.InspectAsync(Entry(path));
        RunSta(() =>
        {
            var page = new FileInspectorPage { DataContext = inspector };
            try
            {
                page.Measure(new Size(1280, 960)); page.Arrange(new Rect(0, 0, 1280, 960)); page.UpdateLayout();
                var movie = Assert.IsType<SorterBinMovie>(page.FindName("sorterBinMovie"));
                Assert.Same(inspector.BinMovie, movie.DataContext);
                Assert.Equal(Visibility.Visible, ((Border)page.FindName("HistogramPanel")).Visibility);
                var population = (ComboBox)movie.FindName("HistogramSelector");
                var generation = (ComboBox)movie.FindName("GenerationSelector");
                var pool = (ComboBox)movie.FindName("PoolSelector");
                var plot = (EvolutionPlot)movie.FindName("HistogramPlot");
                Assert.Equal("Stage count", plot.XAxisTitle); Assert.Equal("CE count", plot.YAxisTitle);
                Assert.Same(inspector.BinMovie!.Visits, plot.Visits);
                Assert.Equal(6, plot.FrequencyMaximum);
                Assert.True(plot.ActualWidth > 200); Assert.True(plot.ActualHeight >= 280);
                Assert.Equal(3, generation.Items.Count); Assert.Equal(3, pool.Items.Count);
                population.SelectedItem = "Unsuccessful"; generation.SelectedItem = 10; page.UpdateLayout();
                Assert.Equal("Unsuccessful", inspector.BinMovie.SelectedHistogram);
                Assert.Equal(10, inspector.BinMovie.SelectedGeneration);
                Assert.Equal(1, Assert.Single(plot.Visits.Counts).Value);
                population.SelectedItem = "Successful"; generation.SelectedItem = 0; page.UpdateLayout();
                if (Environment.GetEnvironmentVariable("SORTOPIA_HISTOGRAM_SCREENSHOT") is { Length: > 0 } output)
                {
                    var image = new RenderTargetBitmap(1280, 960, 96, 96, PixelFormats.Pbgra32);
                    image.Render(page);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(output); encoder.Save(file);
                }
            }
            finally { inspector.Dispose(); page.DataContext = null; }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public void Dispose() => temporary.Delete(true);
}
