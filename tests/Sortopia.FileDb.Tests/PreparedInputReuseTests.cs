using Sortopia.FileDb;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Single_file_execution_uses_the_validated_input_snapshot_without_reading_it_again()
    {
        var (world, project) = Create(true);
        Completed(Prepare(world, project).Execution);
        var folder = WorkspaceData.projectFolder(world, Root, project.Id);
        var parameters = Path.Combine(folder, "runParameters");
        var item = RunParameterFiles.listWorkItems(parameters, Token).First();
        var store = new SortopiaFileDb(folder, project.Id);
        var locks = new List<FileStream>();
        var progress = new ItemProgress(value => {
            if (value.State == workItemExecutionState.Running)
                foreach (var input in item.SetupInputs)
                    locks.Add(new FileStream(store.GetPath(input), FileMode.Open, FileAccess.Read, FileShare.None));
        });
        try
        {
            // Running is reported after complete preflight. Holding exclusive read
            // handles makes any redundant input read fail, without changing a byte.
            var report = RunParameterExecution.executeFilesWithStatus(
                [Path.Combine(parameters, item.Id + ".msgpack")], null!, progress, Token);
            Completed(report);
            Assert.NotEmpty(locks);
            Assert.Single(report.Results);
        }
        finally
        {
            foreach (var stream in locks) stream.Dispose();
        }
    }
}
