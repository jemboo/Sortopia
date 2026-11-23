using System.IO;
using System.Security.Cryptography;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;
using WorkspaceModule = Sortopia.Workspaces.Serialization.ModuleDocument;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceImportFunctionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-import-functions-");
    private string? sourceFolder;
    private string? openedFolder;
    private WorkspacePageViewModel Open()
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        settings.Save(Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName);
        return new(() => { }, _ => null, settings, pickExistingFolder: _ => sourceFolder, openFolder: folder => openedFolder = folder);
    }
    private static WorkspaceOverrideField Field(WorkspaceTreeNode node, bool setup) => setup ? node.Editor!.SetupFunctionOverride : node.Editor!.ResultsFunctionOverride;
    private static void Define(WorkspaceTreeNode node, bool setup)
    {
        if (setup) node.Editor!.SetupFunctionName = SetupFunctionNames.uniformMssiSimpleSorterModels;
        else node.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
    }
    internal static WorkspaceModule Module(string name, bool branch, bool setup, bool defines, params WorkspaceModule[] children)
    {
        var local = new moduleOverridesDto();
        if (defines)
        {
            if (setup) local.setupFunctionName = SetupFunctionNames.uniformMssiSimpleSorterModels;
            else local.resultsFunctionName = ResultsFunctionNames.uniformMssiSimpleSorterModels;
        }
        return WorkspaceFixtures.InheritedModule(Guid.NewGuid(), name, ModuleDefaults.configuration(branch),
            ModuleOverridesDto.toDomain(branch ? hierarchyKind.Branch : hierarchyKind.Root, local), children, []);
    }
    internal static void WriteModule(string folder, WorkspaceModule module)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, WorkspaceDefinitionFiles.Name(module.Definition.id)), Sortopia.Core.Serialization.MessagePackSetup.serialize(WorkspaceDocumentModule.moduleFile(module)));
        foreach (var child in module.Modules) WriteModule(Path.Combine(folder, child.Definition.moduleName), child);
    }
    private static string Snapshot(string folder) => string.Join("\n", Directory.GetDirectories(folder, "*", SearchOption.AllDirectories)
        .Select(path => "D:" + Path.GetRelativePath(folder, path)).Concat(Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
        .Select(path => "F:" + Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))))).Order());

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Duplicate_imports_leave_draft_and_files_unchanged_link_all_definitions_and_allow_retry(bool branch, bool setup, bool inPlace)
    {
        var page = Open();
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null);
        page.Selected!.Name = "Ancestor"; Define(page.Selected, setup);
        (branch ? page.AddBranchModuleCommand : page.AddModuleCommand).Execute(null); page.Selected!.Name = "Destination";
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        var ancestor = page.Roots[0].Children[0]; var destination = ancestor.Children[0];
        sourceFolder = Path.Combine(inPlace ? destination.GetFolderPath(page.WorkspaceFolder) : temporary.FullName, "Source");
        WriteModule(sourceFolder, Module("Source", branch, setup, false,
            Module("A", branch, setup, false, Module("Functions", branch, setup, true)),
            Module("B", branch, setup, false, Module("Functions", branch, setup, true))));
        var sourceBefore = Snapshot(sourceFolder); var workspaceBefore = Snapshot(page.WorkspaceFolder);
        page.Selected = destination;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Contains("Cannot import", page.Error); Assert.Contains("more than once", page.ImportFunctionConflictSummary);
        Assert.Same(destination, page.Selected); Assert.Empty(destination.Children); Assert.False(page.HasUnsavedChanges);
        Assert.Empty(page.Roots[0].GetPendingCopies(page.WorkspaceFolder));
        Assert.Equal(workspaceBefore, Snapshot(page.WorkspaceFolder)); Assert.Equal(sourceBefore, Snapshot(sourceFolder));
        var conflicts = page.ImportFunctionConflicts;
        Assert.Equal(3, conflicts.Count); Assert.All(conflicts, conflict => Assert.Equal(setup, conflict.IsSetup));
        var local = Assert.Single(conflicts.Where(conflict => conflict.SourceFolder is null));
        Assert.Same(ancestor, local.Node);
        page.ReviewImportFunctionConflictCommand.Execute(local);
        Assert.Same(ancestor, page.Selected); Assert.Equal(setup ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
        foreach (var external in conflicts.Where(conflict => conflict.SourceFolder is not null))
        {
            page.ReviewImportFunctionConflictCommand.Execute(external);
            Assert.Equal(external.SourceFolder, openedFolder); Assert.Same(ancestor, page.Selected);
            Assert.Contains("source folder", external.Location);
        }
        Assert.Equal(new[] { Path.Combine(sourceFolder, "A", "Functions"), Path.Combine(sourceFolder, "B", "Functions") },
            conflicts.Where(conflict => conflict.SourceFolder is not null).Select(conflict => conflict.SourceFolder));
        Field(ancestor, setup).IsOverridden = false;
        Assert.Empty(page.ImportFunctionConflicts); Assert.Empty(page.Error);
        Assert.Contains("add the module again", page.ImportFunctionConflictSummary);
        page.Selected = destination;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error); Assert.False(page.HasRejectedModuleImport);
        Assert.Same(destination, page.Selected!.Parent); Assert.Single(destination.Children);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.Equal(2, Open().Roots[0].Children[0].Children[0].Children[0].Children.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Internal_source_duplicates_are_rejected_even_at_workspace_root_and_retries_reread_source(bool setup)
    {
        var page = Open(); await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        sourceFolder = Path.Combine(temporary.FullName, "Source");
        var source = Module("Source", true, setup, true, Module("Leaf", true, setup, true));
        WriteModule(sourceFolder, source);
        var before = Snapshot(sourceFolder);
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Roots[0].Children); Assert.False(page.HasUnsavedChanges);
        Assert.Equal(2, page.ImportFunctionConflicts.Count);
        Assert.All(page.ImportFunctionConflicts, conflict => Assert.NotNull(conflict.SourceFolder));
        Assert.Equal(before, Snapshot(sourceFolder));
        var dto = WorkspaceDocumentModule.moduleFile(source);
        if (setup) dto.localOverrides.setupFunctionName = null; else dto.localOverrides.resultsFunctionName = null;
        File.WriteAllBytes(Path.Combine(sourceFolder, WorkspaceDefinitionFiles.Name(source.Definition.id)), Sortopia.Core.Serialization.MessagePackSetup.serialize(dto));
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error); Assert.False(page.HasRejectedModuleImport); Assert.Single(page.Roots[0].Children);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Different_phases_sibling_definitions_and_captured_inheritance_do_not_conflict(bool setup)
    {
        var page = Open(); page.AddBranchModuleCommand.Execute(null); var destination = page.Selected!;
        Define(destination, !setup);
        page.AddBranchModuleCommand.Execute(null); Define(page.Selected!, setup);
        sourceFolder = Path.Combine(temporary.FullName, "Source");
        // The source root captures a parent function, but does not define it locally.
        var originalParent = Module("Original parent", true, !setup, true,
            Module("Source", true, setup, false, Module("A", true, setup, true), Module("B", true, setup, true)));
        WriteModule(sourceFolder, originalParent.Modules[0]);
        page.Selected = destination;
        await page.AddExistingAsync(WorkspaceEntityKind.Module);
        Assert.Empty(page.Error); Assert.False(page.HasRejectedModuleImport); Assert.Equal(2, destination.Children.Count);
        Assert.False(Field(page.Selected!, !setup).IsOverridden);
    }

    public void Dispose() => temporary.Delete(true);
}
