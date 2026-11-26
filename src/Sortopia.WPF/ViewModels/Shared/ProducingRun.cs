using System.IO;
using Sortopia.Workspaces.Persistence;

namespace Sortopia.WPF.ViewModels.Shared;

public sealed record ProducingRun(string WorkspaceFolder, Guid ProjectId, Guid RunId)
{
    public static ProducingRun? Find(string projectFolder, Guid projectId, Guid runId)
    {
        if (!File.Exists(Path.Combine(projectFolder, "runs", $"{runId:D}.msgpack"))) return null;
        for (var folder = new DirectoryInfo(projectFolder); folder is not null; folder = folder.Parent)
        {
            if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint)) return null;
            if (WorkspaceDefinitionFiles.Find(folder.FullName, "workspace") is not null)
                return new(folder.FullName, projectId, runId);
        }
        return null;
    }
}
