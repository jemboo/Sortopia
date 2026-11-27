using Sortopia.FileDb;
using Sortopia.Functions;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Theory]
    [InlineData("unchanged")]
    [InlineData("corrupt")]
    [InlineData("missing")]
    public void Completed_preflight_validation_is_reused_only_while_output_bytes_are_unchanged(string change)
    {
        var path = ParallelFiles()[0];
        Completed(RunParameterExecution.executeFile(path, null!, Token));
        var item = RunParameterFiles.loadFile(path);
        var folder = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        var store = new SortopiaFileDb(folder, item.ProjectId);
        var output = store.GetPath(RunOutputFiles.keys(item)[0].Reference);
        var before = File.ReadAllBytes(output);
        var timestamp = File.GetLastWriteTimeUtc(output);
        var started = false;
        var events = new ItemProgress(update => {
            if (update.State != workItemExecutionState.Running) return;
            started = true; // All preflight decoding and receipt creation have finished.
            if (change == "missing") File.Delete(output);
            else if (change == "corrupt")
            {
                var damaged = (byte[])before.Clone();
                damaged[^1] ^= 1;
                File.WriteAllBytes(output, damaged);
                File.SetLastWriteTimeUtc(output, timestamp);
            }
        });
        var report = RunParameterExecution.executeFilesWithOptions([path], new(4), null!, events, Token);
        Assert.True(started);
        if (change == "corrupt")
        {
            Assert.Equal(runExecutionStatus.Failed, report.Status);
            Assert.Contains("checksum", report.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(report.Results);
        }
        else
        {
            Completed(report);
            var saved = Assert.Single(Assert.Single(report.Results).StoredOutputs);
            Assert.Equal(change == "unchanged", saved.Reused);
            if (change == "unchanged") Assert.Equal(before, File.ReadAllBytes(output));
            // Regeneration assigns a new evaluation ID; its payload must still
            // pass the complete schema, checksum and domain validation.
            Assert.Equal(outputState.Complete, RunParameterExecution.inspectFile(path, Token).Outputs.State);
        }
    }
}
