using System.Numerics;
using System.Text.Json.Serialization;
using Sortopia.Models.Inputs;
using Sortopia.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Sorting.Inputs;
using Sortopia.Sorting.Networks;

namespace Sortopia.Runs;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "generator")]
[JsonDerivedType(typeof(MergeTests), "merge")]
[JsonDerivedType(typeof(PrefixTests), "prefix")]
[JsonDerivedType(typeof(ModelSetRecipe), "mssi")]
[JsonDerivedType(typeof(EvalRecipe), "sorterEval")]
public abstract record RunRecipe;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "generator")]
[JsonDerivedType(typeof(MergeTests), "merge")]
[JsonDerivedType(typeof(PrefixTests), "prefix")]
public abstract record SortableTestsRecipe : RunRecipe
{
    public abstract void Validate();
    internal abstract (sortableTests Tests, ce[] Prefix) Generate(Guid id);
}

public sealed record MergeTests(int Channels, int Ways) : SortableTestsRecipe
{
    public override void Validate()
    {
        if (Channels is < 2 or > 64 || Ways < 2 || Ways > Channels || Channels % Ways != 0)
            throw new ArgumentException("Choose 2–64 channels and at least two equal merge segments.");
        // The model materializes a lattice. Bound its size before it starts allocating.
        if (BigInteger.Pow(Channels / Ways + 1, Ways) * Channels > 16_000_000)
            throw new ArgumentException("This merge is too large for the in-memory generator. Reduce channels or merge ways.");
    }

    internal override (sortableTests Tests, ce[] Prefix) Generate(Guid id)
    {
        Validate();
        var model = msasM.create(Channels, Ways, mergeSuffixType.NoSuffix, sorterLibVariant.VariantA);
        return (SortableTestsModel.makeSortableTests(id, sortableTestsModel.NewMsasMi(model), sortableDataFormat.Int8Vector512), []);
    }

    public override string ToString() => $"{Channels} channels · {Ways}-way merge";
}

public sealed record PrefixTests(int Channels, int Stages, string Variant) : SortableTestsRecipe
{
    public static PrefixTests[] Available() => PrefixLib.ids().Select(id => new PrefixTests(
        id.ChannelCount, id.StageLength, PrefixLibVariant.toString(id.PrefixLibVariant))).ToArray();

    internal override (sortableTests Tests, ce[] Prefix) Generate(Guid id)
    {
        var (tests, prefix) = Model().MakeSortableBitv512TestWithPrefix(id);
        return (sortableTests.NewBitv512(tests), prefix);
    }

    private msasPfx Model() => msasPfx.create(prefixLibId.create(Channels, Stages, PrefixLibVariant.fromString(Variant)));
    public override void Validate() => Model();
    public override string ToString() => $"{Channels} channels · {Stages}-stage prefix · {Variant}";
}
