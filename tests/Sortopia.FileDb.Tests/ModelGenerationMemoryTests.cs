using Microsoft.FSharp.Core;
using Sortopia.FileDb;
using Sortopia.Functions;
using Sortopia.Runs;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces;
using Sortopia.Workspaces.Persistence;
using Sortopia.Workspaces.Serialization;
using Xunit;

namespace Sortopia.FileDb.Tests;

public sealed class ModelGenerationMemoryTests
{
    [Theory]
    [InlineData(6.66)]
    [InlineData(8.0)]
    public async Task Large_generated_sets_without_sources_wait_for_memory_instead_of_overlapping(double workerGiB)
    {
        var prefix = PrefixLib.ids().First(value => value.ChannelCount == 16 && value.StageLength == 1);
        var parameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix),
            ParameterBinding.create(ParamKeys.sorterCountKey, 5000)]);
        var reservation = ExecutionTuning.modelGenerationReservationBytes(parameters, 895);
        Assert.InRange(reservation, 4L * 1073741824, 5L * 1073741824);
        var gate = new ExecutionMemoryGate((long)(workerGiB * 1073741824), FuncConvert.FromFunc(() => 128L * 1024 * 1024));
        var first = gate.Acquire(reservation, TestContext.Current.CancellationToken);
        using var attempted = new ManualResetEventSlim();
        var second = Task.Run(() => {
            attempted.Set();
            return gate.Acquire(reservation, TestContext.Current.CancellationToken);
        }, TestContext.Current.CancellationToken);
        try
        {
            Assert.True(attempted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await second.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        }
        finally { first.Dispose(); }
        using var admitted = await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Generated_set_reservation_scales_with_dimensions_and_saturates_without_overflow()
    {
        var parameters = parameterSet.fromBindings([
            ParameterBinding.create(ParamKeys.channelCountKey, 16),
            ParameterBinding.create(ParamKeys.sorterCountKey, 5000)]);
        var original = ExecutionTuning.modelGenerationReservationBytes(parameters, 895);
        var twice = parameters.WithBinding(ParameterBinding.create(ParamKeys.sorterCountKey, 10000));
        Assert.Equal(2 * original, ExecutionTuning.modelGenerationReservationBytes(twice, 895));
        Assert.True(ExecutionTuning.modelGenerationReservationBytes(parameters, 1790) > original);
        Assert.Equal(0, ExecutionTuning.modelGenerationReservationBytes(parameters, 0));
        var huge = parameters.WithBinding(ParameterBinding.create(ParamKeys.sorterCountKey, int.MaxValue))
            .WithBinding(ParameterBinding.create(ParamKeys.channelCountKey, int.MaxValue));
        Assert.Equal(long.MaxValue, ExecutionTuning.modelGenerationReservationBytes(huge, int.MaxValue));
    }
}

public sealed partial class PreparedExecutionTests
{
    [Fact]
    public void Generated_set_larger_than_the_soft_target_runs_alone_and_publishes_models()
    {
        var prefix = PrefixLib.ids().First(value => value.ChannelCount == 16 && value.StageLength == 1);
        var name = ResultsFunctionNames.prefixMsuf4SimpleSorterModels;
        var project = Project("Large models", new projectConfiguration("Models", Results(name, [
            ParameterBinding.create(ParamKeys.prefixLibIdKey, prefix),
            ParameterBinding.create(ParamKeys.sorterSuccessFractionKey, sorterSuccessFraction.pct99),
            Bind("SorterCount", "8"), Bind("Repl", "0"), Bind("RngType", "LCG", parameterValueType.Text),
            Bind("OrthoGenRate", "1.5", parameterValueType.Floating),
            Bind("ParaGenRate", "1", parameterValueType.Floating),
            Bind("SelfSymGenRate", "0.05", parameterValueType.Floating)]), null));
        var module = WorkspaceFixtures.InheritedModule(Guid.NewGuid(), "Module", ModuleDefaults.configuration(false),
            ModuleOverridesDto.toDomain(hierarchyKind.Root, new moduleOverridesDto { resultsFunctionName = name }), [], [project]);
        var world = WorkspaceFixtures.Workspace(Guid.NewGuid(), "Workspace", "Workspace", [module]);
        Directory.CreateDirectory(Root);
        WorkspaceFileStore.CreateDocument(Root, world, Token);
        Completed(Prepare(world, project).Execution);
        var folder = Path.Combine(Root, new WorkspaceDocumentIndex(world).FindProject(project.Definition.id).RelativeFolder);
        var report = RunParameterExecution.executeFolderWithOptions(Path.Combine(folder, "runParameters"), null,
            new(4, 8, 1L), null!, Token);
        Completed(report);
        Assert.NotEmpty(report.Results);
        Assert.NotEmpty(DataFiles(folder, "results"));
    }
}
