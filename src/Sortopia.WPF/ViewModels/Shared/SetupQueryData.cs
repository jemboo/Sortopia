using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Shared;

public interface ISetupQueryRow
{
    SetupQueryData SetupQueries { get; }
}

internal sealed record SetupQueryKey(sortopiaDataType DataType, string Name)
{
    internal string DataTypeLabel => SortopiaDataType.kindName(DataType)
        + (SortopiaDataType.text(DataType) is { Length: > 0 } label ? $" · {label}" : "");
}

public sealed record SetupQueryCell(string Text, parameterValue? SortValue);

/// Typed values remain separate from their display text so grid sorting is numeric.
public sealed class SetupQueryData : ViewModelBase, IEquatable<SetupQueryData>
{
    internal Dictionary<sortopiaDataType, parameterBinding[]?> Inputs { get; } = [];
    internal bool Unavailable { get; init; }
    internal IEnumerable<SetupQueryKey> Keys => Inputs.Where(input => input.Value is not null)
        .SelectMany(input => input.Value!.Select(binding => new SetupQueryKey(input.Key, binding.KeyName)));
    public SetupQueryCell[] Cells { get; private set; } = [];

    // Row records compare saved metadata, independently of the grid's current columns.
    public bool Equals(SetupQueryData? other) => other is not null && Unavailable == other.Unavailable
        && Inputs.Count == other.Inputs.Count && Inputs.All(input => other.Inputs.TryGetValue(input.Key, out var value)
            && (input.Value is null ? value is null : value is not null && input.Value.SequenceEqual(value)));
    public override bool Equals(object? other) => other is SetupQueryData queries && Equals(queries);
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Unavailable);
        foreach (var input in Inputs.OrderBy(input => input.Key))
        {
            hash.Add(input.Key); hash.Add(input.Value is null);
            foreach (var binding in input.Value ?? []) hash.Add(binding);
        }
        return hash.ToHashCode();
    }

    internal void SetColumns(IEnumerable<SetupQueryKey> keys)
    {
        Cells = keys.Select(key =>
        {
            var present = Inputs.TryGetValue(key.DataType, out var bindings);
            var binding = bindings?.SingleOrDefault(binding => binding.KeyName == key.Name);
            return binding is not null ? new SetupQueryCell(binding.FormattedValue, binding.Value)
                : new SetupQueryCell(Unavailable || (present && bindings is null) ? "Unavailable" : "—", null);
        }).ToArray();
        OnPropertyChanged(nameof(Cells));
    }
}
