using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;
using Sortopia.Functions;
using Sortopia.Runs;
using Xunit;

namespace Sortopia.WPF.Tests;

public sealed class FunctionBuilderTests
{
    private sealed class ReachedExecutor : Exception { }
    private static readonly sortopiaDataType Models = sortopiaDataType.NewSorterModelSet("");
    private static readonly sortopiaDataType Tests = sortopiaDataType.NewSortableTests("");
    private static readonly FSharpFunc<runHost, Unit> Results = FuncConvert.FromFunc<runHost, Unit>(_ => throw new ReachedExecutor());
    private static readonly FSharpFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>> Setup =
        FuncConvert.FromFunc<runHost, FSharpFunc<FSharpMap<sortopiaDataType, Guid>, Unit>>(_ =>
            FuncConvert.FromFunc<FSharpMap<sortopiaDataType, Guid>, Unit>(_ => throw new ReachedExecutor()));
    private static runHost Host(runExecutionPhase phase, parameterSet parameters, CancellationToken token) =>
        new(phase, parameters, null!, null!, token, 1, null, null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Both_builders_check_phase_and_cancellation_before_validation_or_execution(bool setup)
    {
        var empty = FunctionParameterSupport.empty;
        var validations = 0;
        var support = new functionParameterSupport(empty.Defaults, empty.Guidance, empty.Preview, empty.EditorRules,
            FuncConvert.FromFunc<FSharpList<parameterBinding>, FSharpFunc<FSharpList<parameterSpan>, string>>(_ =>
                FuncConvert.FromFunc<FSharpList<parameterSpan>, string>(_ => { validations++; return ""; })));
        FSharpList<paramKeyDescriptor> required = [ParamKey.describe(ParamKeys.channelCountKey)];
        FSharpList<Tuple<sortopiaDataType, FSharpList<string>>> outputs = [Tuple.Create(Models, ListModule.Singleton("ChannelCount"))];
        Action<runHost> execute;
        if (setup)
        {
            var definition = FunctionDefinition.setup("1", setupParameterRequirements.NewDeclared(required), outputs, [], support, Setup);
            execute = host => definition.Execute.Invoke(host).Invoke(MapModule.Empty<sortopiaDataType, Guid>());
        }
        else
        {
            var definition = FunctionDefinition.results("1", required, outputs, [], support, Results);
            execute = host => definition.Execute.Invoke(host);
        }
        var phase = setup ? runExecutionPhase.Setup : runExecutionPhase.Results;
        var other = setup ? runExecutionPhase.Results : runExecutionPhase.Setup;
        var missing = parameterSet.fromBindings([]);
        Assert.Contains(setup ? "requires a setup host" : "requires a results host",
            Assert.Throws<ArgumentException>(() => execute(Host(other, missing, TestContext.Current.CancellationToken))).Message);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => execute(Host(phase, missing, cancelled.Token)));
        Assert.Contains("ChannelCount", Assert.Throws<ArgumentException>(() => execute(Host(phase, missing, TestContext.Current.CancellationToken))).Message);
        Assert.Equal(0, validations);
        Assert.Throws<ReachedExecutor>(() => execute(Host(phase, parameterSet.fromBindings([ParameterBinding.create(ParamKeys.channelCountKey, 4)]), TestContext.Current.CancellationToken)));
        Assert.Equal(1, validations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Setup_requires_exact_source_assignments_for_declared_and_source_query_parameters(bool fromSources)
    {
        var requirements = fromSources ? setupParameterRequirements.FromSources
            : setupParameterRequirements.NewDeclared([ParamKey.describe(ParamKeys.replKey)]);
        var definition = FunctionDefinition.setup("2", requirements,
            [Tuple.Create(Models, ListModule.Empty<string>()), Tuple.Create(Tests, ListModule.Empty<string>())],
            [Tuple.Create(Models, ListModule.Singleton(Models)), Tuple.Create(Tests, ListModule.Singleton(Tests))],
            FunctionParameterSupport.empty, Setup);
        Assert.Equal(fromSources, definition.ParametersFromSources);
        Assert.Equal(fromSources ? 0 : 1, definition.RequiredParameters.Count());
        var host = Host(runExecutionPhase.Setup, parameterSet.fromBindings([ParameterBinding.create(ParamKeys.replKey, 1)]), TestContext.Current.CancellationToken);
        var valid = MapModule.OfSeq(new[] { Tuple.Create(Models, Guid.NewGuid()), Tuple.Create(Tests, Guid.NewGuid()) });
        foreach (var invalid in new[] { valid.Remove(Tests), valid.Add(Tests, Guid.Empty), valid.Add(sortopiaDataType.NewSorterSetEval(""), Guid.NewGuid()) })
            Assert.Contains("exactly the source projects", Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(host).Invoke(invalid)).Message);
        Assert.Throws<ReachedExecutor>(() => definition.Execute.Invoke(host).Invoke(valid));
        var unknown = ParameterBinding.fromText("UnknownSourceKey", parameterValueType.Integer, "3");
        var invalidHost = Host(runExecutionPhase.Setup, host.Parameters.WithBinding(unknown), TestContext.Current.CancellationToken);
        Assert.Contains("UnknownSourceKey", Assert.Throws<ArgumentException>(() => definition.Execute.Invoke(invalidHost).Invoke(valid)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Empty_dependencies_mean_independent_outputs_but_partial_tables_are_rejected(bool setup)
    {
        FSharpList<Tuple<sortopiaDataType, FSharpList<string>>> outputs =
            [Tuple.Create(Models, ListModule.Empty<string>()), Tuple.Create(Tests, ListModule.Empty<string>())];
        FSharpMap<sortopiaDataType, FSharpList<sortopiaDataType>> dependencies;
        FSharpList<Tuple<sortopiaDataType, FSharpList<sortopiaDataType>>> partial = [Tuple.Create(Models, ListModule.Empty<sortopiaDataType>())];
        if (setup)
        {
            var required = setupParameterRequirements.NewDeclared([]);
            dependencies = FunctionDefinition.setup("1", required, outputs, [], FunctionParameterSupport.empty, Setup).OutputDependencies;
            Assert.Throws<ArgumentException>(() => FunctionDefinition.setup("1", required, outputs, partial, FunctionParameterSupport.empty, Setup));
            Assert.Throws<ArgumentException>(() => FunctionDefinition.setup("1", setupParameterRequirements.FromSources, outputs, [], FunctionParameterSupport.empty, Setup));
        }
        else
        {
            dependencies = FunctionDefinition.results("1", [], outputs, [], FunctionParameterSupport.empty, Results).OutputDependencies;
            Assert.Throws<ArgumentException>(() => FunctionDefinition.results("1", [], outputs, partial, FunctionParameterSupport.empty, Results));
        }
        Assert.Equal(new[] { Models, Tests }.Order(), dependencies.Keys.Order());
        Assert.All(dependencies.Values, values => Assert.Empty(values));
    }
}
