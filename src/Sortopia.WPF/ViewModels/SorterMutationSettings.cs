using Sortopia.Core;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;

namespace Sortopia.WPF.ViewModels;

public sealed class MutationParameter : ViewModelBase, IParameterValue
{
    public const double Minimum = 0.001;
    private double value;

    public MutationParameter(string name, double initial)
    {
        Name = name;
        Value = initial;
    }

    public string Name { get; }
    public double Value
    {
        get => value;
        set
        {
            if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Mutation values must be finite.");
            if (SetProperty(ref this.value, Math.Clamp(value, Minimum, 1)))
                OnPropertyChanged(nameof(LogValue));
        }
    }

    public double LogValue
    {
        get => Math.Log10(Value);
        set => Value = Math.Pow(10, value);
    }

    double IParameterValue.SliderValue { get => LogValue; set => LogValue = value; }
}

public sealed record MutationEditorTable(string Name, ParameterArrayViewModel Parameters);

public sealed class SorterMutationSettings : ViewModelBase
{
    private string method = "";
    private int channelCount;
    private string variant = "V1";
    private double[,] stepRates = new double[1, 0];
    private MutationStep[] steps = [];
    private MutationEditorTable? selectedTable;

    public string Method => method;
    public string Variant { get => variant; set { if (!Variants.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value)); SetProperty(ref variant, value); } }
    public string[] Variants => method is "mssi" or "msuf4" ? ["V1", "V2"] : ["V1"];
    public bool CanExcludeSelf => method == "msce";
    public bool ExcludeSelf { get; set; } = true;
    public MutationParameter OverallRate { get; private set; } = new("Overall modification rate", 0.1);
    public IReadOnlyList<MutationParameter> Parameters { get; private set; } = [];
    public IReadOnlyList<MutationEditorTable> ParameterTables { get; private set; } = [];
    public bool HasMultipleTables => ParameterTables.Count > 1;
    public MutationEditorTable? SelectedTable { get => selectedTable; set => SetProperty(ref selectedTable, value); }

    public void SelectMethod(string kind, int width = 0)
    {
        if (kind is not ("msce" or "mssi" or "msrs" or "msuf4" or "msuf6")) throw new ArgumentOutOfRangeException(nameof(kind));
        var unfolding = kind is "msuf4" or "msuf6";
        var seedWidth = kind == "msuf6" ? 6 : 4;
        if (width == 0) width = method == kind ? channelCount : seedWidth;
        if (unfolding && (width < seedWidth || width % seedWidth != 0))
            throw new ArgumentOutOfRangeException(nameof(width));
        var stepCount = unfolding ? MathUtils.exactLog2(width / seedWidth) + 1 : 0;
        var sameMethod = method == kind;
        if (sameMethod && (!unfolding || channelCount == width)) return;
        var previousTable = sameMethod ? SelectedTable?.Name : null;
        if (!sameMethod)
        {
            method = kind;
            Variant = kind == "msuf4" ? "V2" : "V1";
            OverallRate = new("Overall modification rate", 0.1);
            steps = [];
            stepRates = new double[1, 0];
        }
        channelCount = width;

        if (unfolding)
        {
            ConfigureSteps(seedWidth, stepCount);
            Parameters = [OverallRate];
        }
        else
        {
            var parameters = new List<MutationParameter> { OverallRate };
            if (kind == "msce")
                parameters.AddRange([new("Mutation weight", 1), new("Insertion weight", MutationParameter.Minimum),
                    new("Deletion weight", MutationParameter.Minimum)]);
            else
            {
                parameters.AddRange([new("Ortho weight", 1), new("Para weight", 1)]);
                if (kind != "mssi") parameters.Add(new("Self symmetry weight", 1));
            }
            Parameters = parameters;
            var table = new ParameterArrayViewModel(["Mutation"], parameters.Skip(1).Select(p => p.Name).ToArray(),
                (_, column) => parameters[column + 1], Math.Log10(MutationParameter.Minimum), 0) { SmallChange = 0.01, LargeChange = 0.1 };
            ParameterTables = [new("Action weights", table)];
        }
        SelectedTable = ParameterTables.FirstOrDefault(table => table.Name == previousTable) ?? ParameterTables[0];
        foreach (var property in new[] { nameof(Method), nameof(OverallRate), nameof(Parameters), nameof(ParameterTables),
            nameof(HasMultipleTables), nameof(Variants), nameof(CanExcludeSelf) }) OnPropertyChanged(property);
    }

    private void ConfigureSteps(int seedWidth, int count)
    {
        var rates = new double[1, count];
        var next = new MutationStep[count];
        string[] ops = ["Ortho", "Para", "SR"];
        for (var index = 0; index < count; index++)
        {
            var from = (seedWidth << index) / 2;
            var name = index == 0 ? "Seed" : $"Unfold {from} → {2 * from}";
            var modes = index == 0 && seedWidth == 6
                ? ["Ortho 1", "Ortho 2", "Para 1", "Para 2", "Para 3", "Para 4", "SR"] : ops;
            next[index] = index < steps.Length ? steps[index] : new MutationStep(name, modes);
            rates[0, index] = index < steps.Length ? stepRates[0, index] : 1;
        }
        steps = next;
        stepRates = rates;
        var rateTable = new ParameterArrayViewModel(stepRates, rowHeaders: ["Rate"], columnHeaders: steps.Select(step => step.Name).ToArray());
        ParameterTables = new[] { new MutationEditorTable("Step rates", rateTable) }
            .Concat(steps.Select(step => new MutationEditorTable(step.Name, step.Table))).ToArray();
    }

    internal simpleSorterModelMutator Create(simpleSorterModel model)
    {
        var parentMethod = model.IsMsce ? "msce" : model.IsMssi ? "mssi" : model.IsMsrs ? "msrs" : model.IsMsuf4 ? "msuf4" : "msuf6";
        if (Method != parentMethod) throw new InvalidOperationException("Mutation settings must match the parent model family.");
        var rng = rngFactory.SmxFactory;
        var width = SimpleSorterModel.getChannelCount(model);
        var v = Variant == "V2" ? mutatorVariant.V2 : mutatorVariant.V1;
        if (method is "msuf4" or "msuf6")
        {
            SelectMethod(method, width);
            var snapshots = steps.Select((step, i) => new unfoldingMutationStep(stepRates[0, i], step.Weights)).ToArray();
            return method == "msuf4"
                ? SimpleSorterModelMutator.getMsuf4ModelMutatorByStep(width, rng, OverallRate.Value, snapshots[0], snapshots[1..], v)
                : SimpleSorterModelMutator.getMsuf6ModelMutatorByStep(width, rng, OverallRate.Value, snapshots[0], snapshots[1..], v);
        }
        double P(int index) => Parameters[index].Value;
        return method switch
        {
            "msce" => SimpleSorterModelMutator.getMsceModelMutator(rng, ExcludeSelf, P(0), P(1), P(2), P(3), v),
            "mssi" => SimpleSorterModelMutator.getMssiModelMutator(rng, true, P(0), P(1), P(2), v),
            "msrs" => SimpleSorterModelMutator.getMsrsModelMutator(rng, true, P(0), P(1), P(2), P(3), v),
            _ => throw new InvalidOperationException("Unknown sorter family.")
        };
    }

    private sealed class MutationStep
    {
        public MutationStep(string name, string[] modes)
        {
            Name = name;
            Weights = new double[modes.Length, modes.Length];
            for (var row = 0; row < modes.Length; row++)
                for (var column = 0; column < modes.Length; column++)
                    Weights[row, column] = 1;
            Table = new(Weights, rowHeaders: modes.Select(mode => $"From {mode}").ToArray(),
                columnHeaders: modes.Select(mode => $"To {mode}").ToArray());
        }

        public string Name { get; }
        public double[,] Weights { get; }
        public ParameterArrayViewModel Table { get; }
    }
}
