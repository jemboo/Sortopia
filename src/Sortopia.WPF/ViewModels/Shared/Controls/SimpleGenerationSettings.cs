using Microsoft.FSharp.Collections;
using Sortopia.Core;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Simple.Ce;
using Sortopia.Models.Sorting.Simple.Rs;
using Sortopia.Models.Sorting.Simple.Si;
using Sortopia.Models.Sorting.Simple.Uf4;
using Sortopia.Models.Sorting.Simple.Uf6;
using Sortopia.Permutations;
using Sortopia.Sorting.Networks;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

public sealed class GenerationWeight(string name) : ViewModelBase, IParameterValue
{
    private double value = 1;
    public string Name => name;
    public double Value { get => value; set => SetProperty(ref this.value, Math.Clamp(value, 0, 1)); }
}

public abstract class GenerationDistribution(string name)
{
    public string Name => name;
    public abstract GenerationWeight[] Weights { get; }
    protected abstract void Validate();
    internal string Error
    {
        get { try { Validate(); return ""; } catch (ArgumentException error) { return $"{Name}: {error.Message}"; } }
    }
}

public sealed class OpsGenerationDistribution : GenerationDistribution
{
    public OpsGenerationDistribution(string name) : base(name) => Weights = [Ortho, Para, SelfReflection];
    public GenerationWeight Ortho { get; } = new("Ortho");
    public GenerationWeight Para { get; } = new("Para");
    public GenerationWeight SelfReflection { get; } = new("SR");
    public override GenerationWeight[] Weights { get; }
    internal opsGenRates ToProbabilities() => OpsGenerationProbabilities.fromWeights(Ortho.Value, Para.Value, SelfReflection.Value);
    protected override void Validate() => ToProbabilities();
}

public sealed class Seed6GenerationDistribution : GenerationDistribution
{
    public Seed6GenerationDistribution(string name) : base(name) => Weights = [Ortho1, Ortho2, Para1, Para2, Para3, Para4, SelfReflection];
    public GenerationWeight Ortho1 { get; } = new("Ortho 1");
    public GenerationWeight Ortho2 { get; } = new("Ortho 2");
    public GenerationWeight Para1 { get; } = new("Para 1");
    public GenerationWeight Para2 { get; } = new("Para 2");
    public GenerationWeight Para3 { get; } = new("Para 3");
    public GenerationWeight Para4 { get; } = new("Para 4");
    public GenerationWeight SelfReflection { get; } = new("SR");
    public override GenerationWeight[] Weights { get; }
    internal seed6GenRates ToProbabilities() => Seed6GenerationProbabilities.fromWeights(
        Ortho1.Value, Ortho2.Value, Para1.Value, Para2.Value, Para3.Value, Para4.Value, SelfReflection.Value);
    protected override void Validate() => ToProbabilities();
}

public sealed class SimpleGenerationSettings : ViewModelBase
{
    private string method = "";
    private int width;
    private string randomSource = "SplitMix64";
    private bool excludeSelf = true;
    private OpsGenerationDistribution? stage, seed4;
    private Seed6GenerationDistribution? seed6;
    private OpsGenerationDistribution[] unfolding = [];
    public string[] RandomSources { get; } = ["SplitMix64", "LCG", ".NET"];
    private rngType RandomType => RngType.fromString(RandomSource == ".NET" ? "Net" : RandomSource);
    public string RandomSource { get => randomSource; set { if (SetProperty(ref randomSource, value)) OnPropertyChanged(nameof(Error)); } }
    public bool ExcludeSelf { get => excludeSelf; set => SetProperty(ref excludeSelf, value); }
    public bool CanExcludeSelf => method == "msce";
    public IReadOnlyList<GenerationDistribution> Distributions { get; private set; } = [];
    public bool HasDistributions => Distributions.Count != 0;
    public IReadOnlyList<ParameterArrayViewModel> WeightTables { get; private set; } = [];
    public string Error
    {
        get
        {
            try { _ = RandomType; return Distributions.Select(row => row.Error).FirstOrDefault(error => error.Length > 0) ?? ""; }
            catch (Exception exception) when (exception is ArgumentException || exception.GetType() == typeof(Exception)) { return exception.Message; }
        }
    }

    internal void Configure(string kind, int channels)
    {
        if (method == kind && width == channels) return;
        var previous = method == kind ? Distributions : [];
        method = kind; width = channels;
        OpsGenerationDistribution Ops(string name) => previous.OfType<OpsGenerationDistribution>().FirstOrDefault(row => row.Name == name) ?? new(name);
        stage = kind == "msrs" ? Ops("Stage") : null;
        seed4 = kind == "msuf4" ? Ops("Seed") : null;
        seed6 = kind == "msuf6" ? previous.OfType<Seed6GenerationDistribution>().FirstOrDefault() ?? new("Seed") : null;
        IEnumerable<int> levels = kind switch {
            "msuf4" => Msuf4RandGen.unfoldingWidths(channels),
            "msuf6" => Msuf6RandGen.unfoldingWidths(channels),
            _ => []
        };
        unfolding = levels.Select(from => Ops($"Unfold {from} → {2 * from}")).ToArray();
        var groups = new List<GenerationDistribution>();
        if (stage is not null) groups.Add(stage);
        if (seed4 is not null) groups.Add(seed4);
        if (seed6 is not null) groups.Add(seed6);
        groups.AddRange(unfolding);
        foreach (var weight in Distributions.SelectMany(d => d.Weights)) weight.PropertyChanged -= WeightChanged;
        Distributions = groups;
        foreach (var weight in groups.SelectMany(d => d.Weights)) weight.PropertyChanged += WeightChanged;
        // Only presentation groups differ: the seed6 editor has seven named modes.
        WeightTables = groups.GroupBy(group => group is Seed6GenerationDistribution)
            .Select(group => WeightTable(group.ToArray())).ToArray();
        OnPropertyChanged(nameof(Distributions));
        OnPropertyChanged(nameof(WeightTables));
        OnPropertyChanged(nameof(HasDistributions));
        OnPropertyChanged(nameof(CanExcludeSelf));
        OnPropertyChanged(nameof(Error));
    }

    private void WeightChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => OnPropertyChanged(nameof(Error));

    private static ParameterArrayViewModel WeightTable(GenerationDistribution[] rows) =>
        new(rows.Select(group => group.Name).ToArray(), rows[0].Weights.Select(weight => weight.Name).ToArray(),
            (row, column) => rows[row].Weights[column]);

    internal simpleSorterModelGen CreateGenerator(int stages, ce[]? prefix = null)
    {
        var rng = RngFactory.create(RandomType); prefix ??= [];
        FSharpList<opsGenRates> UnfoldingProbabilities() => ListModule.OfSeq(unfolding.Select(row => row.ToProbabilities()));
        return method switch {
            "msce" => simpleSorterModelGen.NewSmmMsceRandGen(MsceRandGen.create(rng, width, prefix, stages, ExcludeSelf)),
            "mssi" => simpleSorterModelGen.NewSmmMssiRandGen(MssiRandGen.create(rng, width, prefix, stages)),
            "msrs" => simpleSorterModelGen.NewSmmMsrsRandGen(MsrsRandGen.create(rng, width, prefix, stages, stage!.ToProbabilities())),
            "msuf4" => simpleSorterModelGen.NewSmmMsuf4RandGen(Msuf4RandGen.create(rng, width, prefix, stages, seed4!.ToProbabilities(), UnfoldingProbabilities())),
            "msuf6" => simpleSorterModelGen.NewSmmMsuf6RandGen(Msuf6RandGen.create(rng, width, prefix, stages, seed6!.ToProbabilities(), UnfoldingProbabilities())),
            _ => throw new InvalidOperationException("Choose a simple sorter model.")
        };
    }
}
