using System.IO;
using System.Security.Cryptography;
using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.Settings;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class WorkspaceMoveFunctionTests : IDisposable
{
    private readonly DirectoryInfo temporary = Directory.CreateTempSubdirectory("Sortopia-move-functions-");
    private WorkspacePageViewModel Open()
    {
        var settings = new WorkspaceFolderSettings(Path.Combine(temporary.FullName, "settings.txt"));
        var folder = Directory.CreateDirectory(Path.Combine(temporary.FullName, "Workspace")).FullName;
        settings.Save(folder);
        return new(() => { }, _ => null, settings);
    }
    private static WorkspaceTreeNode Module(WorkspaceTreeNode parent, string name, bool branch = true)
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, name, family: branch ? hierarchyKind.Branch : hierarchyKind.Root);
        parent.Children.Add(node);
        return node;
    }
    private static WorkspaceOverrideField Field(WorkspaceTreeNode node, bool setup) => setup ? node.Editor!.SetupFunctionOverride : node.Editor!.ResultsFunctionOverride;
    private static void Define(WorkspaceTreeNode node, bool setup)
    {
        if (setup) node.Editor!.SetupFunctionName = SetupFunctionNames.uniformMssiSorterModels;
        else node.Editor!.ResultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
    }
    private static void CutTo(WorkspacePageViewModel page, WorkspaceTreeNode source, WorkspaceTreeNode destination)
    {
        page.Selected = source; Assert.True(page.CutCommand.CanExecute(null)); page.CutCommand.Execute(null); page.Selected = destination;
    }
    private static string Snapshot(string folder) => string.Join("\n", Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Order()
        .Select(path => Path.GetRelativePath(folder, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Conflicting_moves_are_disabled_without_changing_the_draft_or_files_and_link_every_definition(bool branch, bool setup)
    {
        var page = Open(); var root = page.Roots[0];
        var moving = Module(root, "Moving", branch);
        var first = Module(Module(moving, "First", branch), "Functions", branch); Define(first, setup);
        var second = Module(Module(moving, "Second", branch), "Functions", branch); Define(second, setup);
        var ancestor = Module(root, "Destination ancestor", branch); Define(ancestor, setup);
        var destination = Module(ancestor, "Destination", branch);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        WorkspaceTreeNode Saved(WorkspaceTreeNode node) => page.Roots[0].DescendantsAndSelf().Single(item => item.Id == node.Id);
        root = page.Roots[0]; moving = Saved(moving); first = Saved(first); second = Saved(second);
        ancestor = Saved(ancestor); destination = Saved(destination);
        var before = Snapshot(page.WorkspaceFolder);
        CutTo(page, moving, destination);
        Assert.False(page.PasteCommand.CanExecute(null));
        Assert.Contains("more than once", page.PasteHint);
        var conflicts = page.MoveFunctionConflicts;
        Assert.Equal(new[] { ancestor, first, second }, conflicts.Select(conflict => conflict.Node));
        Assert.All(conflicts, conflict => Assert.Equal(setup, conflict.IsSetup));
        Assert.Contains("Moving / First / Functions", conflicts[1].Location);
        Assert.Contains("Moving / Second / Functions", conflicts[2].Location);
        page.PasteCommand.Execute(null); await page.PasteAsync();
        Assert.Same(root, moving.Parent); Assert.Empty(destination.Children);
        Assert.True(page.HasCutItem); Assert.True(moving.IsCut); Assert.False(page.HasUnsavedChanges);
        Assert.Equal(before, Snapshot(page.WorkspaceFolder));
        foreach (var conflict in conflicts)
        {
            page.ReviewMoveFunctionConflictCommand.Execute(conflict);
            Assert.Same(conflict.Node, page.Selected);
            Assert.Equal(setup ? "setupFunction" : "resultsFunction", page.ActiveItemKey);
            Assert.True(page.HasCutItem);
        }
        ancestor.Name = "Renamed destination ancestor";
        page.Selected = destination;
        Assert.Contains("Renamed destination ancestor", page.MoveFunctionConflicts[0].Location);
        Field(ancestor, setup).IsOverridden = false;
        Assert.Empty(page.MoveFunctionConflicts); Assert.True(page.PasteCommand.CanExecute(null));
        await page.PasteAsync(); Assert.Empty(page.Error);
        Assert.Same(destination, moving.Parent); Assert.False(page.HasCutItem);
        await page.SaveWorkspaceAsync(); Assert.Empty(page.Error);
        Assert.Equal(destination.Id, Open().Roots[0].DescendantsAndSelf().Single(node => node.Id == moving.Id).Parent!.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Moves_keep_sibling_branches_and_function_phases_independent_and_drop_the_old_ancestor_path(bool setup)
    {
        var page = Open(); var root = page.Roots[0];
        var oldAncestor = Module(root, "Old ancestor"); Define(oldAncestor, !setup);
        var moving = Module(oldAncestor, "Moving");
        var leaf = Module(moving, "Functions"); Define(leaf, setup);
        var destination = Module(root, "Destination"); Define(destination, !setup);
        var sibling = Module(destination, "Sibling"); Define(sibling, setup);
        CutTo(page, moving, destination);
        Assert.Empty(page.MoveFunctionConflicts); Assert.True(page.PasteCommand.CanExecute(null));
        await page.PasteAsync(); Assert.Empty(page.Error); Assert.Same(destination, moving.Parent);
        Assert.Same(destination.Editor, Field(leaf, !setup).InheritedSource);
    }

    [Fact]
    public async Task Blank_definitions_do_not_block_but_a_definition_added_after_cut_is_checked_again()
    {
        var page = Open(); var root = page.Roots[0];
        var moving = Module(root, "Moving"); Field(moving, false).IsOverridden = true;
        var destination = Module(root, "Destination"); Define(destination, false);
        CutTo(page, moving, destination);
        Assert.True(page.PasteCommand.CanExecute(null)); Assert.Empty(page.MoveFunctionConflicts);
        Define(moving, false);
        Assert.False(page.PasteCommand.CanExecute(null)); Assert.Equal(2, page.MoveFunctionConflicts.Count);
        await page.PasteAsync(); Assert.Same(root, moving.Parent);
        page.CancelCutCommand.Execute(null);
        Assert.False(page.HasMoveFunctionConflicts); Assert.Empty(page.MoveFunctionConflictSummary);
    }

    [Fact]
    public async Task A_module_with_a_valid_draft_run_cannot_be_moved_under_another_results_definition()
    {
        var page = WorkspaceRunExecutionTests.Create(temporary.FullName, false);
        WorkspaceRunExecutionTests.SupplyQuerySpans(page);
        var run = page.Selected!; var moving = run.Parent!.Parent!;
        var destination = Module(page.Roots[0], "Destination", false); Define(destination, false);
        Assert.Empty(page.SaveIssues); Assert.False(run.Parent.IsSealed);
        var originalFunction = run.Editor!.ResultsFunctionName;
        Assert.NotEqual(originalFunction, destination.Editor!.ResultsFunctionName);
        CutTo(page, moving, destination);
        Assert.False(page.PasteCommand.CanExecute(null)); Assert.Equal(2, page.MoveFunctionConflicts.Count);
        await page.PasteAsync();
        Assert.Same(page.Roots[0], moving.Parent); Assert.Equal(originalFunction, run.Editor.ResultsFunctionName);
        Assert.Empty(page.SaveIssues); Assert.True(page.HasCutItem);
    }

    [Fact]
    public async Task Moving_out_of_a_legacy_duplicate_path_is_allowed()
    {
        var page = Open(); var root = page.Roots[0];
        var ancestor = Module(root, "Ancestor"); Define(ancestor, false);
        var moving = Module(ancestor, "Moving");
        var legacy = moving.Editor!.ToModuleOverrides(); legacy.resultsFunctionName = ResultsFunctionNames.uniformMssiSorterModels;
        moving.Editor.LoadModuleOverrides(legacy);
        CutTo(page, moving, root);
        Assert.Empty(page.MoveFunctionConflicts); Assert.True(page.PasteCommand.CanExecute(null));
        await page.PasteAsync(); Assert.Empty(page.Error); Assert.Same(root, moving.Parent);
        Assert.True(Field(moving, false).CanEditValue);
    }

    public void Dispose() => temporary.Delete(true);
}
