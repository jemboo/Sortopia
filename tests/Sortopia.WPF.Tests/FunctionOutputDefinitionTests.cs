using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionOutputDefinitionTests
{
    private static readonly sortopiaDataType Models = sortopiaDataType.NewSorterModelSet("model label");
    private static readonly sortopiaDataType Tests = sortopiaDataType.NewSortableTests("test label");
    private static readonly FSharpFunc<runHost, Unit> Results = FuncConvert.FromFunc<runHost, Unit>(_ => null!);
    private static readonly FSharpFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>> Setup =
        FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(_ =>
            FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(_ => null!));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Combined_declarations_preserve_identity_bytes_and_each_outputs_contributions(bool setup)
    {
        var required = ListModule.OfArray(new[] { ParamKey.describe(ParamKeys.replKey), ParamKey.describe(ParamKeys.channelCountKey) });
        FSharpList<functionOutputDefinition> outputs = [new(Tests, ["ChannelCount"], []), new(Models, ["Repl"], [Models])];
        var project = Guid.NewGuid();
        var source = new storedDataReference(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            setup ? runExecutionPhase.Results : runExecutionPhase.Setup, Models);
        var parameters = parameterSet.fromBindings([ParameterBinding.create(ParamKeys.replKey, 3), ParameterBinding.create(ParamKeys.channelCountKey, 8)]);
        var database = setup ? "Setup" : "Results";
        var oldKeys = MapModule.OfSeq(new[] { Tuple.Create(Models, ListModule.Singleton("Repl")), Tuple.Create(Tests, ListModule.Singleton("ChannelCount")) });
        var oldDependencies = MapModule.OfSeq(new[] { Tuple.Create(Models, ListModule.Singleton(Models)), Tuple.Create(Tests, ListModule.Empty<sortopiaDataType>()) });
        var expected = OutputPlan.create(project, database, setup, "identity.test", "7", oldKeys, oldDependencies, parameters, [source]);

        void Check<T>(functionDefinition<T> definition)
        {
            Assert.Equal("7", definition.Version);
            Assert.Equal(oldKeys, definition.DataTypes);
            Assert.Equal(oldDependencies, definition.OutputDependencies);
            Assert.Equal(new[] { Models }, definition.SourceDataTypes);
            Assert.Equal(oldKeys.Keys, definition.SortopiaDataTypes);
            var actual = OutputPlan.create(project, database, setup, "identity.test", definition.Version,
                definition.DataTypes, definition.OutputDependencies, parameters, [source]);
            foreach (var entry in expected)
            {
                Assert.Equal(entry.Value.Key.Reference, actual[entry.Key].Key.Reference);
                Assert.Equal(entry.Value.Key.QueryBytes, actual[entry.Key].Key.QueryBytes);
                Assert.Equal(entry.Value.Key.IdentityBytes, actual[entry.Key].Key.IdentityBytes);
            }
            var changed = OutputPlan.create(project, database, setup, "identity.test", definition.Version,
                definition.DataTypes, definition.OutputDependencies,
                parameters.WithBinding(ParameterBinding.create(ParamKeys.replKey, 4)),
                [new(source.ProjectId, Guid.NewGuid(), source.QueryId, source.Phase, Models)]);
            dataKey Key(FSharpMap<runDataOutput, plannedOutput> plan, sortopiaDataType type) => plan.Single(entry => Equals(entry.Key.DataType, type)).Value.Key;
            Assert.Equal(Key(actual, Tests).Reference, Key(changed, Tests).Reference);
            Assert.NotEqual(Key(actual, Models).Id, Key(changed, Models).Id);
        }

        // Declaration order is presentation, not part of a stored identity.
        foreach (var declarations in new[] { outputs, ListModule.OfSeq(outputs.Reverse()) })
        {
            if (setup) Check(FunctionDefinition.setup("7", setupParameterRequirements.NewDeclared(required), declarations, FunctionParameterSupport.empty, Setup));
            else Check(FunctionDefinition.results("7", required, declarations, FunctionParameterSupport.empty, Results));
        }
    }

    public static IEnumerable<object[]> InvalidDeclarations =>
        from setup in new[] { false, true }
        from problem in new[] { "empty", "duplicate-output", "duplicate-kind", "duplicate-key", "undeclared-key", "duplicate-dependency", "invalid-output-label", "invalid-dependency-label" }
        select new object[] { setup, problem };

    [Theory]
    [MemberData(nameof(InvalidDeclarations))]
    public void Both_builders_reject_malformed_output_declarations(bool setup, string problem)
    {
        FSharpList<functionOutputDefinition> outputs = problem switch
        {
            "empty" => [],
            "duplicate-output" => [new(Models, [], []), new(Models, [], [])],
            "duplicate-kind" => [new(Models, [], []), new(sortopiaDataType.NewSorterModelSet("another label"), [], [])],
            "duplicate-key" => [new(Models, ["Repl", "Repl"], [])],
            "undeclared-key" => [new(Models, ["ChannelCount"], [])],
            "duplicate-dependency" => [new(Models, [], [Models, Models])],
            "invalid-output-label" => [new(sortopiaDataType.NewSorterModelSet(null!), [], [])],
            "invalid-dependency-label" => [new(Models, [], [sortopiaDataType.NewSortableTests(null!)])],
            _ => throw new ArgumentOutOfRangeException(nameof(problem))
        };
        FSharpList<paramKeyDescriptor> required = [ParamKey.describe(ParamKeys.replKey)];
        if (setup) Assert.ThrowsAny<ArgumentException>(() => FunctionDefinition.setup("1", setupParameterRequirements.NewDeclared(required), outputs, FunctionParameterSupport.empty, Setup));
        else Assert.ThrowsAny<ArgumentException>(() => FunctionDefinition.results("1", required, outputs, FunctionParameterSupport.empty, Results));
    }

    [Fact]
    public void Setup_dependencies_require_exact_declared_types_while_results_can_read_other_types()
    {
        var external = sortopiaDataType.NewSorterModelSet("different source label");
        FSharpList<functionOutputDefinition> outputs = [new(Models, [], [external])];
        var results = FunctionDefinition.results("1", [], outputs, FunctionParameterSupport.empty, Results);
        Assert.Equal(new[] { external }, results.SourceDataTypes);
        foreach (var requirements in new[] { setupParameterRequirements.NewDeclared([]), setupParameterRequirements.FromSources })
            Assert.Contains("Each source must match", Assert.Throws<ArgumentException>(() =>
                FunctionDefinition.setup("1", requirements, outputs, FunctionParameterSupport.empty, Setup)).Message);
    }
}
