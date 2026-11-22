using System.Runtime.ExceptionServices;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class MissingOutputTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-missing-outputs-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("project")]
    [InlineData("type-folder")]
    [InlineData("file")]
    public void Missing_outputs_are_pending_without_throwing_first_chance_IO_exceptions(string missing)
    {
        var projectId = Guid.NewGuid();
        var project = Path.Combine(temporary.FullName, "Project");
        var store = new SortopiaFileDb(project, projectId);
        var key = DataKey.create(projectId, "Results", false, "test", "1",
            sortopiaDataType.NewSorterPoolSummarySet(""), [], parameterSet.fromBindings([]), []);
        var path = store.GetPath(key.Reference);
        if (missing != "project") Directory.CreateDirectory(project);
        if (missing == "file") Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var thread = Environment.CurrentManagedThreadId;
        var exceptions = new List<Exception>();
        void Observe(object? sender, FirstChanceExceptionEventArgs args)
        {
            if (Environment.CurrentManagedThreadId == thread && args.Exception is IOException)
                exceptions.Add(args.Exception);
        }
        AppDomain.CurrentDomain.FirstChanceException += Observe;
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var integrity = store.CheckIntegrity(key, Token);
                Assert.True(integrity.IsOk);
                Assert.Null(integrity.ResultValue);
                var loaded = ((ISortopiaDb)store).TryLoad(key, Token);
                Assert.True(loaded.IsOk);
                Assert.Null(loaded.ResultValue);
            }
            Assert.Equal(outputState.Pending, OutputPlan.inspectIntegrity(store, [key], Token).State);
        }
        finally { AppDomain.CurrentDomain.FirstChanceException -= Observe; }
        Assert.Empty(exceptions);
        Assert.False(File.Exists(path));

        // A prior miss must not hide a later file or skip its validation.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.True(store.CheckIntegrity(key, Token).IsError);
        Assert.True(((ISortopiaDb)store).TryLoad(key, Token).IsError);
        Assert.Equal(outputState.Invalid, OutputPlan.inspectIntegrity(store, [key], Token).State);
    }

    [Fact]
    public void A_directory_at_an_output_filename_is_reported_as_invalid()
    {
        var projectId = Guid.NewGuid();
        var store = new SortopiaFileDb(temporary.FullName, projectId);
        var key = DataKey.create(projectId, "Results", false, "test", "1",
            sortopiaDataType.NewSorterPoolSummarySet(""), [], parameterSet.fromBindings([]), []);
        var path = store.GetPath(key.Reference);
        Directory.CreateDirectory(path);
        var integrity = store.CheckIntegrity(key, Token);
        Assert.True(integrity.IsError);
        Assert.Contains(path, integrity.ErrorValue);
        Assert.True(((ISortopiaDb)store).TryLoad(key, Token).IsError);
        Assert.Equal(outputState.Invalid, OutputPlan.inspectIntegrity(store, [key], Token).State);
    }

    [Fact]
    public void Missing_output_checks_still_honor_cancellation()
    {
        var projectId = Guid.NewGuid();
        var store = new SortopiaFileDb(temporary.FullName, projectId);
        var key = DataKey.create(projectId, "Results", false, "test", "1",
            sortopiaDataType.NewSorterPoolSummarySet(""), [], parameterSet.fromBindings([]), []);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => store.CheckIntegrity(key, cancelled.Token));
        Assert.Throws<OperationCanceledException>(() => ((ISortopiaDb)store).TryLoad(key, cancelled.Token));
    }

    public void Dispose() => temporary.Delete(true);
}
