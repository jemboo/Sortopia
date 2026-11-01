using Sortopia.Core;
using Sortopia.Core.Serialization;
using Sortopia.Evaluation;
using Sortopia.Evaluation.Serialization;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Serialization;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;
using Sortopia.Sorting.Serialization.Inputs;

namespace Sortopia.Runs;

public sealed record ModelSetRecipe(int Channels, int Stages, int Count, int FirstIndex) : RunRecipe
{
    public override string ToString() => $"{Count} mssi models · {Channels} channels · {Stages} stages · first index {FirstIndex}";

    internal sorterModelSet Generate(Guid id)
    {
        if (Channels is < 2 or > 64 || Channels % 2 != 0)
            throw new ArgumentException("Choose an even channel count between 2 and 64 for this model run.");
        if (Stages < 1 || Count < 1 || FirstIndex < 0 || (long)FirstIndex + Count > int.MaxValue ||
            (long)Count * Stages > 2_000_000 / (Channels / 2))
            throw new ArgumentException("Use positive model and stage counts, a nonnegative first index, and at most 2 million CEs per set.");
        var generator = SimpleSorterModelGen.makeUniform(rngFactory.SmxFactory, Channels, Stages, simpleSorterModelType.Mssi, true);
        return SorterModelGen.makeSorterModelSetFromIndexSpan(id, FirstIndex, Count, sorterModelGen.NewSimple(generator));
    }
}

public sealed record ManifestSlot(Guid RunId, Guid ManifestId, int Index);
public sealed record EvalRecipe(ManifestSlot Models, ManifestSlot Tests) : RunRecipe
{
    public override string ToString() => "Sorter evaluation";
}
public sealed record EvalSummary(int ModelCount, int Passed, int FewestFailures)
{
    public override string ToString() => $"{Passed} of {ModelCount} passed · fewest failing inputs: {FewestFailures:N0}";
}

public sealed partial class RunLibrary
{
    private sealed record Source(RunOutput Output, string Path);

    private Source Resolve(ManifestSlot slot, OutputDataType type)
    {
        var directory = RunPath(slot.RunId);
        var manifest = Read<OutputManifest>(Path.Combine(directory, "manifests", $"{slot.ManifestId:N}.json"));
        if (manifest.RunId != slot.RunId || manifest.Id != slot.ManifestId)
            throw new InvalidDataException("The selected manifest ID or run ID does not match the file.");
        var entry = manifest.Entries.SingleOrDefault(e => e.DataType == type && e.Index == slot.Index)
            ?? throw new KeyNotFoundException($"Manifest '{manifest.Name}' has no {type}[{slot.Index}].");
        var inventory = Read<CompletedRun>(Path.Combine(directory, "inventory.json"));
        var output = inventory.Outputs.SingleOrDefault(o => o.ArtifactId == entry.ArtifactId && o.DataType == type);
        if (inventory.Id != slot.RunId || output == null) throw new InvalidDataException($"The inventory does not contain the selected {type}.");
        return new(output, Path.Combine(directory, $"{entry.ArtifactId:N}.mpk"));
    }

    public (sortableTests Tests, ce[] Prefix) LoadTestsWithPrefix(ManifestSlot slot)
    {
        var source = Resolve(slot, OutputDataType.SortableTestsWithPrefix);
        if (source.Output.ParameterId is Guid parameterId)
        {
            var reference = new Storage.ResultRef(new(slot.RunId, parameterId),
                Storage.outputDataType.SortableTestsWithPrefix, source.Output.ArtifactId);
            var stored = (Storage.ResultData.SortableTestsWithPrefix)store.LoadResult(reference);
            return (stored.Item1, stored.Item2);
        }
        var dto = MessagePackSetup.deserialize<sortableTestsWithPrefixDto>(File.ReadAllBytes(source.Path));
        var (tests, prefix) = SortableTestsWithPrefixDto.toDomain(dto);
        if (SortableTests.getId(tests) != source.Output.ArtifactId) throw new InvalidDataException("Dataset ID does not match its manifest entry.");
        return (tests, prefix);
    }

    public sorterModelSet LoadModels(ManifestSlot slot)
    {
        var source = Resolve(slot, OutputDataType.SorterModelSet);
        var models = SorterModelSetDto.toDomain(MessagePackSetup.deserialize<sorterModelSetDto>(File.ReadAllBytes(source.Path)));
        if (models.Id != source.Output.ArtifactId) throw new InvalidDataException("Model set ID does not match its manifest entry.");
        return models;
    }

    public sorterSetEval LoadEvaluation(ManifestSlot slot)
    {
        var source = Resolve(slot, OutputDataType.SorterSetEval);
        var result = SorterSetEvalDto.toDomain(MessagePackSetup.deserialize<sorterSetEvalDto>(File.ReadAllBytes(source.Path)));
        if (result.SorterSetEvalId != source.Output.ArtifactId) throw new InvalidDataException("Evaluation ID does not match its manifest entry.");
        return result;
    }

    private (RunOutput Output, byte[] Bytes) Produce(RunRecipe recipe, Guid id, CancellationToken token, IProgress<string>? progress)
    {
        switch (recipe)
        {
            case SortableTestsRecipe dataset:
                var (tests, prefix) = dataset.Generate(id);
                var dto = SortableTestsWithPrefixDto.fromDomain(tests, prefix);
                return (new(id, dataset, SortableTests.getSortableCount(tests)), MessagePackSetup.serialize(dto));
            case ModelSetRecipe modelRecipe:
                var models = modelRecipe.Generate(id);
                return (new(id, modelRecipe, 0), MessagePackSetup.serialize(SorterModelSetDto.fromDomain(models)));
            case EvalRecipe eval:
                return Evaluate(eval, id, token, progress);
            default:
                throw new ArgumentException("Unknown run recipe.");
        }
    }

    private (RunOutput, byte[]) Evaluate(EvalRecipe recipe, Guid id, CancellationToken token, IProgress<string>? progress)
    {
        var models = LoadModels(recipe.Models);
        var (tests, prefixCes) = LoadTestsWithPrefix(recipe.Tests);
        var width = SortableTests.getChannelCount(tests);
        var members = models.SorterModels;
        if (members.Length == 0 || members.Any(model => SorterModel.getChannelCount(model) != width))
            throw new ArgumentException($"Select a nonempty model set whose models all have {width} channels to match the tests.");

        var prefix = ceBlock.create(SortableTests.getId(tests), width, prefixCes);

        var results = new sorterEval[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report($"Evaluating model {i + 1} of {members.Length}");
            var sorter = SorterModel.makeSorter(members[i], null);
            var block = CeBlock.fromSorter(sorter);
            var counts = CeBlockOps.evalWithSorterTest(tests, prefix, block, false);
            results[i] = SorterEval.createV2(sorter.SorterId, counts);
        }
        var evaluation = sorterSetEval.create(id, models.Id, SortableTests.getId(tests), results);
        var summary = new EvalSummary(results.Length, results.Count(SorterEval.getIsSorted), results.Min(SorterEval.getUnsortedCount));
        return (new(id, recipe, SortableTests.getSortableCount(tests), summary),
            MessagePackSetup.serialize(SorterSetEvalDto.fromDomain(evaluation)));
    }
}
