using System.IO;
using Sortopia.Core.Serialization;
using Sortopia.Runs;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Models;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.@module;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceIdentityTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-workspace-identity-");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Empty_ids_and_duplicate_run_ids_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => workspace.create(Guid.Empty, "W", "W", []));
        Assert.Throws<ArgumentException>(() => WorkspaceModule.create(hierarchyKind.Root, Guid.Empty, "M", "M", [], []));
        Assert.Throws<ArgumentException>(() => project.create(hierarchyKind.Root, Guid.Empty, "P", "P", []));
        foreach (var branch in new[] { false, true })
        {
            var dto = RunDto.fromDomain(WorkspaceHierarchyTests.MakeRun("P", "R", branch));
            dto.id = Guid.Empty;
            Assert.Throws<ArgumentException>(() => RunDto.toDomain(dto));
        }
        var first = WorkspaceHierarchyTests.MakeRun("P", "A");
        var second = WorkspaceRun.withNames("P", "B", first);
        Assert.Throws<ArgumentException>(() => project.create(hierarchyKind.Root, Guid.NewGuid(), "P", "P", [first, second]));
    }



    public void Dispose() => temporary.Delete(recursive: true);
}
