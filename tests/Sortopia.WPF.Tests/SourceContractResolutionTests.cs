using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class SourceContractResolutionTests
{
    [Fact]
    public void Required_parameters_include_source_only_keys_in_first_occurrence_order()
    {
        var models = SourceContracts.resolve(["Repl", "ChannelCount", "Repl"]);
        var tests = SourceContracts.resolve(["PrefixLibId", "ChannelCount", "SortableDataFormat"]);
        var combined = SourceContracts.combine([models, tests]);
        Assert.Equal(new[] { "Repl", "ChannelCount", "PrefixLibId", "SortableDataFormat" }, combined.QueryKeys);
        Assert.Equal(combined.QueryKeys, SourceContracts.requireParameters(combined).Select(parameter => parameter.Name));
        Assert.Empty(SourceContracts.missingQueryKeys(["ChannelCount"], models));
        Assert.Equal(new[] { "PrefixLibId" }, SourceContracts.missingQueryKeys(["PrefixLibId"], models));
        Assert.Empty(SourceContracts.missingQueryKeys([], tests));
    }

    [Fact]
    public void Unknown_keys_remain_visible_in_drafts_and_fail_when_parameters_are_required()
    {
        var contract = SourceContracts.resolve(["Repl", "UnknownSourceKey", "ChannelCount"]);
        Assert.Equal(new[] { "Repl", "UnknownSourceKey", "ChannelCount" }, contract.QueryKeys);
        Assert.Equal(new[] { "UnknownSourceKey" }, contract.UnknownKeys);
        Assert.Equal(new[] { "Repl", "ChannelCount" }, contract.RequiredParameters.Select(parameter => parameter.Name));
        var error = Assert.Throws<ArgumentException>(() => SourceContracts.requireParameters(contract));
        Assert.Equal("parameters", error.ParamName);
        Assert.StartsWith("Unknown source query parameter 'UnknownSourceKey'.", error.Message);

        var source = SourceEvaluationInputsTests.Source("SorterModelSet", contract.QueryKeys.ToArray());
        var row = new WorkspaceSetupDataTypeEntry(new() {
            dataType = new() { kind = "SorterModelSet", value = "" }, sourceProjectId = source.Id, queryParameterKeys = []
        }, () => [source]);
        row.SetContract(true, false, [], [], true);
        Assert.Equal("Unknown source query parameters: UnknownSourceKey.", row.Error);
        Assert.False(Assert.Single(row.SourceProjects).IsCompatible);
    }

    [Fact]
    public void Source_adapter_uses_only_the_matching_canonical_output_and_its_projection()
    {
        var source = SourceEvaluationInputsTests.Source("SorterModelSet", "ChannelCount");
        source.Settings.resultsDataTypes = [
            new() { dataType = new() { kind = "SorterModelSet", value = "" }, queryParameterKeys = ["ChannelCount"] },
            new() { dataType = new() { kind = "SorterModelSet", value = "other" }, queryParameterKeys = ["SorterCount"] },
            new() { dataType = new() { kind = "SortableTests", value = "" }, queryParameterKeys = [] }
        ];
        Assert.Equal(new[] { "ChannelCount" }, WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "").QueryKeys);
        Assert.Equal(new[] { "SorterCount" }, WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "other").QueryKeys);
        Assert.Empty(WorkspaceSourceContracts.Resolve(source, "SortableTests", "").QueryKeys);
        Assert.Empty(WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "missing").QueryKeys);
        Assert.Empty(WorkspaceSourceContracts.Resolve(null, "SorterModelSet", "").QueryKeys);
        Assert.False(WorkspaceSourceContracts.HasOutput(source, "SorterModelSet", "missing"));
        Assert.True(WorkspaceSourceContracts.HasOutput(source, "SorterModelSet", "other"));
        Assert.True(WorkspaceSourceContracts.HasOutput(source, "SortableTests", ""));
        source.Settings.resultsDataTypes = [];
        Assert.False(WorkspaceSourceContracts.HasOutput(source, "SorterModelSet", ""));
        Assert.Empty(WorkspaceSourceContracts.Resolve(source, "SorterModelSet", "").QueryKeys);
    }

    [Fact]
    public void Fixed_parameter_functions_continue_to_reject_extra_source_query_keys()
    {
        var source = SourceEvaluationInputsTests.Source("SortableTests", "ChannelCount", "Repl", "Repl");
        var row = new WorkspaceSetupDataTypeEntry(new() {
            dataType = new() { kind = "SortableTests", value = "" }, sourceProjectId = source.Id, queryParameterKeys = []
        }, () => [source]);
        row.SetContract(true, false, [], ["ChannelCount"]);
        Assert.Equal("Source query parameters not accepted by this setup function: Repl. Accepted parameters: ChannelCount.", row.Error);
        row.SetContract(true, false, [], ["ChannelCount"], true);
        Assert.Empty(row.Error);
    }
}
