using Microsoft.FSharp.Collections;
using Sortopia.Functions;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class OutputPlanTests
{
    private static readonly sortopiaDataType Models = sortopiaDataType.NewSorterModelSet("");
    private static readonly sortopiaDataType Tests = sortopiaDataType.NewSortableTests("");
    private static FSharpMap<sortopiaDataType, FSharpList<string>> Projections(
        params (sortopiaDataType Type, FSharpList<string> Keys)[] entries) =>
        MapModule.OfSeq(entries.Select(entry => Tuple.Create(entry.Type, entry.Keys)));
    private static dataKey Key(FSharpMap<runDataOutput, plannedOutput> plan, sortopiaDataType type) =>
        plan.Single(entry => Equals(entry.Key.DataType, type)).Value.Key;
    private static storedDataReference Source(sortopiaDataType type) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), runExecutionPhase.Results, type);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_projections_can_select_other_required_keys_and_explicitly_select_no_keys(bool setup)
    {
        FSharpList<functionOutputDefinition> outputs = [new(Models, ["Repl"], []), new(Tests, ["ChannelCount"], [])];
        var projections = Projections((Models, ["ChannelCount"]), (Tests, []));
        var parameters = parameterSet.fromBindings([ParameterBinding.create(ParamKeys.replKey, 3), ParameterBinding.create(ParamKeys.channelCountKey, 8)]);
        var project = Guid.NewGuid();
        var plan = OutputPlan.create(project, "Database", setup, "projection.test", "1", outputs, projections, parameters, []);
        foreach (var entry in plan)
        {
            var expected = DataKey.create(project, "Database", setup, "projection.test", "1", entry.Key.DataType,
                projections[entry.Key.DataType], parameters, []);
            Assert.Equal(expected.Reference, entry.Value.Key.Reference);
            Assert.Equal(expected.QueryBytes, entry.Value.Key.QueryBytes);
            Assert.Equal(expected.IdentityBytes, entry.Value.Key.IdentityBytes);
        }
        Assert.Empty(plan.Single(entry => Equals(entry.Key.DataType, Tests)).Key.QueryParameters.ParamMap);

        var changedContribution = OutputPlan.create(project, "Database", setup, "projection.test", "1", outputs, projections,
            parameters.WithBinding(ParameterBinding.create(ParamKeys.replKey, 4)), []);
        Assert.Equal(Key(plan, Models).Reference, Key(changedContribution, Models).Reference);
        Assert.Equal(Key(plan, Tests).Reference, Key(changedContribution, Tests).Reference);
        var changedQuery = OutputPlan.create(project, "Database", setup, "projection.test", "1", outputs, projections,
            parameters.WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, 9)), []);
        Assert.NotEqual(Key(plan, Models).QueryId, Key(changedQuery, Models).QueryId);
        Assert.NotEqual(Key(plan, Models).Id, Key(changedQuery, Models).Id);
        Assert.Equal(Key(plan, Tests).Reference, Key(changedQuery, Tests).Reference);
    }

    [Fact]
    public void Copied_outputs_use_source_derived_projections_and_only_their_own_source_identity()
    {
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource);
        Assert.True(definition.ParametersFromSources);
        Assert.All(definition.Outputs, output => Assert.Empty(output.ParameterKeys));
        var projections = Projections((Models, ["Repl"]), (Tests, ["ChannelCount"]));
        FunctionConfiguration.validateOutputs(definition, projections);
        var parameters = parameterSet.fromBindings([ParameterBinding.create(ParamKeys.replKey, 3), ParameterBinding.create(ParamKeys.channelCountKey, 8)]);
        var project = Guid.NewGuid();
        var models = Source(Models);
        var tests = Source(Tests);
        var plan = OutputPlan.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version,
            definition.Outputs, projections, parameters, [models, tests]);
        foreach (var source in new[] { models, tests })
        {
            var expected = DataKey.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version,
                source.DataType, projections[source.DataType], parameters, [source]);
            Assert.Equal(expected.Reference, Key(plan, source.DataType).Reference);
            Assert.Equal(expected.QueryBytes, Key(plan, source.DataType).QueryBytes);
            Assert.Equal(expected.IdentityBytes, Key(plan, source.DataType).IdentityBytes);
        }
        var reordered = OutputPlan.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version,
            ListModule.OfSeq(definition.Outputs.Reverse()), projections, parameters, [tests, models]);
        Assert.Equal(Key(plan, Models).Reference, Key(reordered, Models).Reference);
        Assert.Equal(Key(plan, Tests).Reference, Key(reordered, Tests).Reference);
        var changed = OutputPlan.create(project, "Setup", true, SetupFunctionNames.evaluationSource, definition.Version,
            definition.Outputs, projections, parameters, [tests, new(models.ProjectId, Guid.NewGuid(), models.QueryId, models.Phase, Models)]);
        Assert.Equal(Key(plan, Models).QueryId, Key(changed, Models).QueryId);
        Assert.NotEqual(Key(plan, Models).Id, Key(changed, Models).Id);
        Assert.Equal(Key(plan, Tests).Reference, Key(changed, Tests).Reference);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-label")]
    public void Query_projections_must_cover_exactly_the_declared_outputs(string problem)
    {
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource);
        var other = sortopiaDataType.NewSorterModelSet("other");
        var projections = problem switch
        {
            "missing" => Projections((Tests, [])),
            "extra" => Projections((Tests, []), (Models, []), (other, [])),
            _ => Projections((Tests, []), (other, []))
        };
        var error = Assert.Throws<ArgumentException>(() => OutputPlan.create(Guid.NewGuid(), "Setup", true,
            SetupFunctionNames.evaluationSource, definition.Version, definition.Outputs, projections,
            parameterSet.fromBindings([]), [Source(Models), Source(Tests)]));
        Assert.Equal("queryProjections", error.ParamName);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("wrong-label")]
    public void Inputs_must_supply_exactly_one_reference_for_each_declared_dependency(string problem)
    {
        var definition = SetupFunctionCatalog.require(SetupFunctionNames.evaluationSource);
        var models = Source(Models);
        var tests = Source(Tests);
        var other = Source(sortopiaDataType.NewSortableTests("other"));
        FSharpList<storedDataReference> inputs = problem switch
        {
            "missing" => [models],
            "extra" => [models, tests, other],
            "duplicate" => [models, tests, tests],
            _ => [models, other]
        };
        var error = Assert.Throws<ArgumentException>(() => OutputPlan.create(Guid.NewGuid(), "Setup", true,
            SetupFunctionNames.evaluationSource, definition.Version, definition.Outputs, Projections((Models, []), (Tests, [])),
            parameterSet.fromBindings([]), inputs));
        Assert.Equal("inputs", error.ParamName);
    }
}
