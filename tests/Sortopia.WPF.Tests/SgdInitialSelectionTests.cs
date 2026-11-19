using Sortopia.Functions;
using Sortopia.Workspaces;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SgdInitialSelectionTests
{
    [Fact]
    public void Sgd_defaults_offer_random_and_best_selection_and_validate_the_seed()
    {
        var node = new WorkspaceTreeNode(WorkspaceEntityKind.Module, "Sgd", family: hierarchyKind.Branch);
        var editor = node.Editor!;
        editor.ResultsFunctionName = ResultsFunctionNames.standardMsuf4SimpleSgd;
        Assert.Equal("SorterPoolSet", Assert.Single(editor.ResultsOutputContracts).Kind);
        editor.AddRequiredResultsDefaultsCommand.Execute(null);
        var selection = Assert.Single(editor.ResultsParameterDefaults, row => row.Key == "InitialPoolSelection");
        Assert.Equal(new[] { "Random", "Best" }, selection.Value.Choices);
        Assert.Equal("Random", selection.Value.Text);
        selection.Value.Text = "Best";
        Assert.Empty(selection.Error);
        var seed = Assert.Single(editor.ResultsParameterDefaults, row => row.Key == "InitialPoolSeed");
        Assert.Equal("0", seed.Value.Text);
        seed.Value.Text = "-1";
        Assert.NotEmpty(seed.Error);
    }
}
