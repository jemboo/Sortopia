using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class ReportingScheduleTests
{
    [Fact]
    public void Reporting_schedule_editor_previews_and_round_trips_all_methods_and_validates_errors()
    {
        var changes = 0;
        var editor = new WorkspaceReportingSchedule("Summary", "1", () => true, () => changes++);
        Assert.Equal("Constant(1)", editor.Schedule);
        Assert.Contains("0, 1, 2, 3", editor.Preview);
        editor.Interval = "0"; Assert.NotEmpty(editor.Error); Assert.Empty(editor.Preview);
        editor.Load("Exponential(0.25, 10)");
        Assert.True(editor.IsIncreasing); Assert.Equal("10", editor.Interval); Assert.Equal("0.25", editor.Growth);
        Assert.Empty(editor.Error);
        editor.Load("Cyclic(Constant(5), 3)");
        Assert.True(editor.IsCustom); Assert.Equal("Cyclic(Constant(5), 3)", editor.Schedule);
        Assert.Contains("0, 5, 10, 11", editor.Preview);
        editor.Load("Prefixed(2, Constant(1), Constant(100))");
        Assert.True(editor.IsCustom); Assert.Contains("0, 1, 2, 102", editor.Preview);
        editor.Custom = "bad schedule"; Assert.NotEmpty(editor.Error);
        Assert.Equal(2, changes);
        var locked = new WorkspaceReportingSchedule("Snapshot", "100", () => false, () => throw new Exception("Locked editor changed"));
        locked.Interval = "5"; Assert.Equal("100", locked.Interval);
    }
}
