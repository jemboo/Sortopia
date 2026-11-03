using Sortopia.Core;
using Sortopia.Models.Sorting;
using Sortopia.Models.Sorting.Simple;

namespace Sortopia.WPF.ViewModels;

public sealed class MutationParameter : ViewModelBase
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
}

public sealed class SorterMutationSettings : ViewModelBase
{
    private string method = "";
    private string variant = "V1";
    public string Method => method;
    public string Variant { get => variant; set { if (!Variants.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value)); SetProperty(ref variant, value); } }
    public string[] Variants => method is "mssi" or "msuf4" ? ["V1", "V2"] : ["V1"];
    public bool CanExcludeSelf => method == "msce";
    public bool ExcludeSelf { get; set; } = true;
    public IReadOnlyList<MutationParameter> Parameters { get; private set; } = [];
    public string Hint => $"{method} native mutation. Rates are probabilities; action weights are normalized by the model mutator.";

    public void SelectMethod(string kind)
    {
        if (kind is not ("msce" or "mssi" or "msrs" or "msuf4" or "msuf6")) throw new ArgumentOutOfRangeException(nameof(kind));
        if (method == kind) return;
        method = kind;
        Variant = kind == "msuf4" ? "V2" : "V1";
        var parameters = new List<MutationParameter> { new("Modification rate", 0.1) };
        if (kind is "msuf4" or "msuf6") parameters.Add(new("Seed modification rate", 0.1));
        if (kind == "msce") parameters.AddRange([new("Mutation weight", 1), new("Insertion weight", MutationParameter.Minimum),
            new("Deletion weight", MutationParameter.Minimum)]);
        else
        {
            parameters.AddRange([new("Ortho weight", 1), new("Para weight", 1)]);
            if (kind != "mssi") parameters.Add(new("Self symmetry weight", 1));
        }
        Parameters = parameters;
        foreach (var property in new[] { nameof(Method), nameof(Parameters), nameof(Variants), nameof(CanExcludeSelf), nameof(Hint) }) OnPropertyChanged(property);
    }

    internal simpleSorterModelMutator Create(simpleSorterModel model)
    {
        var parentMethod = model.IsMsce ? "msce" : model.IsMssi ? "mssi" : model.IsMsrs ? "msrs" : model.IsMsuf4 ? "msuf4" : "msuf6";
        if (Method != parentMethod) throw new InvalidOperationException("Mutation settings must match the parent model family.");
        var rng = rngFactory.SmxFactory;
        var width = SimpleSorterModel.getChannelCount(model);
        var v = Variant == "V2" ? mutatorVariant.V2 : mutatorVariant.V1;
        double P(int index) => Parameters[index].Value;
        return method switch
        {
            "msce" => SimpleSorterModelMutator.getMsceModelMutator(rng, ExcludeSelf, P(0), P(1), P(2), P(3), v),
            "mssi" => SimpleSorterModelMutator.getMssiModelMutator(rng, true, P(0), P(1), P(2), v),
            "msrs" => SimpleSorterModelMutator.getMsrsModelMutator(rng, true, P(0), P(1), P(2), P(3), v),
            "msuf4" => SimpleSorterModelMutator.getMsuf4ModelMutator(width, rng, true, P(1), P(0), P(2), P(3), P(4), v),
            "msuf6" => SimpleSorterModelMutator.getMsuf6ModelMutator(width, rng, true, P(1), P(0), P(2), P(3), P(4), v),
            _ => throw new InvalidOperationException("Unknown sorter family.")
        };
    }
}
