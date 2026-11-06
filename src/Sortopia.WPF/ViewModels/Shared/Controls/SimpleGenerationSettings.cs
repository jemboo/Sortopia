using Sortopia.Core;
using Sortopia.Models.Sorting.Simple;
using Sortopia.Models.Sorting.Simple.Ce;
using Sortopia.Models.Sorting.Simple.Rs;
using Sortopia.Models.Sorting.Simple.Si;
using Sortopia.Models.Sorting.Simple.Uf4;
using Sortopia.Models.Sorting.Simple.Uf6;
using Sortopia.Permutations;
using Sortopia.Sorting;

namespace Sortopia.WPF.ViewModels.Shared.Controls;

public sealed class GenerationWeight(string name) : ViewModelBase, IParameterValue
{
    private double value = 1;
    public string Name => name;
    public double Value { get => value; set => SetProperty(ref this.value, Math.Clamp(value, 0, 1)); }
}

public sealed class GenerationDistribution(string name, params string[] modes)
{
    public string Name => name;
    public GenerationWeight[] Weights { get; } = modes.Select(mode => new GenerationWeight(mode)).ToArray();
    internal bool IsValid => Weights.Any(w => w.Value > 0);
    internal double[] Probabilities()
    {
        var sum = Weights.Sum(w => w.Value);
        return Weights.Select(w => w.Value / sum).ToArray();
    }

    internal opsGenRates OpsRates()
    {
        var p = Probabilities();
        return opsGenRates.create(p[0], p[1], p[2]);
    }
}

public sealed class SimpleGenerationSettings : ViewModelBase
{
    private string method = "";
    private int width;
    public string[] RandomSources { get; } = ["SplitMix64", "LCG", ".NET"];
    public string RandomSource { get; set; } = "SplitMix64";
    public bool ExcludeSelf { get; set; } = true;
    public bool CanExcludeSelf => method == "msce";
    public IReadOnlyList<GenerationDistribution> Distributions { get; private set; } = [];
    public bool HasDistributions => Distributions.Count != 0;
    public IReadOnlyList<ParameterArrayViewModel> WeightTables { get; private set; } = [];
    public string Error => Distributions.FirstOrDefault(d => !d.IsValid) is { } invalid
        ? $"Set at least one positive weight for {invalid.Name}." : "";

    internal void Configure(string kind, int channels)
    {
        if (method == kind && width == channels) return;
        var previous = method == kind ? Distributions : [];
        method = kind;
        width = channels;
        var groups = new List<GenerationDistribution>();
        void Add(string name, params string[] modes) =>
            groups.Add(previous.FirstOrDefault(d => d.Name == name) ?? new(name, modes));
        string[] ops = ["Ortho", "Para", "SR"];
        if (kind == "msrs") Add("Stage", ops);
        if (kind is "msuf4" or "msuf6")
        {
            int seedWidth = kind == "msuf4" ? 4 : 6;
            Add("Seed", kind == "msuf4" ? ops : ["Ortho 1", "Ortho 2", "Para 1", "Para 2", "Para 3", "Para 4", "SR"]);
            for (int from = seedWidth; from < channels; from *= 2) Add($"Unfold {from} → {2 * from}", ops);
        }
        foreach (var weight in Distributions.SelectMany(d => d.Weights)) weight.PropertyChanged -= WeightChanged;
        Distributions = groups;
        foreach (var weight in groups.SelectMany(d => d.Weights)) weight.PropertyChanged += WeightChanged;
        // A UF6 seed has seven modes, while its unfolding rows share the three OPS modes.
        WeightTables = groups.GroupBy(group => kind == "msuf6" && group.Name == "Seed")
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

    internal simpleSorterModelGen CreateGenerator(int stages)
    {
        var rng = RandomSource switch
        {
            "SplitMix64" => rngFactory.SmxFactory,
            "LCG" => rngFactory.LcgFactory,
            ".NET" => rngFactory.NetFactory,
            _ => throw new InvalidOperationException("Choose a random source.")
        };
        opsGenRatesArray UnfoldingRates() => opsGenRatesArray.create(Distributions.Skip(1).Select(d => d.OpsRates()).ToArray());
        switch (method)
        {
            case "msce":
                return simpleSorterModelGen.NewSmmMsceRandGen(msceRandGen.create(rng, width, ExcludeSelf, StageLength.toCeLength(width, stages)));
            case "mssi":
                return simpleSorterModelGen.NewSmmMssiRandGen(mssiRandGen.create(rng, width, stages));
            case "msrs":
                return simpleSorterModelGen.NewSmmMsrsRandGen(msrsRandGen.create(rng, width, Distributions[0].OpsRates(), stages));
            case "msuf4":
                var uf4 = uf4GenRates.create(width, Distributions[0].OpsRates(), UnfoldingRates());
                return simpleSorterModelGen.NewSmmMsuf4RandGen(msuf4RandGen.create(rng, width, stages, uf4));
            case "msuf6":
                var p = Distributions[0].Probabilities();
                var seed = seed6GenRates.create(p[0], p[1], p[2], p[3], p[4], p[5], p[6]);
                var uf6 = uf6GenRates.create(width, seed, UnfoldingRates());
                return simpleSorterModelGen.NewSmmMsuf6RandGen(msuf6RandGen.create(rng, width, stages, uf6));
            default: throw new InvalidOperationException("Choose a simple sorter model.");
        }
    }
}
