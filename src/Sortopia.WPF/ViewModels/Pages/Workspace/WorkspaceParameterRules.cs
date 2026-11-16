using System.Globalization;
using Sortopia.Core;
using Sortopia.Evaluation;
using Sortopia.Runs;
using Sortopia.Sorting;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Editor guidance for known keys. Parsing uses the same contracts as the catalog functions.</summary>
internal static class WorkspaceParameterRules
{
    private sealed record Rule(string[] Choices, string Hint, Func<string, bool> IsValid);
    private static Rule Choice<T>(T[] values, Func<T, string> format, Func<string, T> parse) =>
        new(values.Select(format).ToArray(), "Choose a value from the list.", text => { parse(text); return true; });
    private static Rule Minimum(int minimum) => new([], $"Enter a whole number of {minimum} or greater.",
        text => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture) >= minimum);
    private static Rule Msuf4Rate() => new([], "Finite nonnegative relative rate. OrthoRate, ParaRate, and SelfSymRate are normalized together for the seed and every unfolding level; at least one must be positive.",
        text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && value >= 0);
    private static readonly Lazy<Dictionary<string, Rule>> rules = new(() => new(StringComparer.Ordinal)
    {
        [ParamKeys.channelCountKey.Name] = Minimum(1),
        [ParamKeys.stageLengthKey.Name] = Minimum(0),
        [ParamKeys.sorterSuccessFractionKey.Name] = new(SorterSuccessFraction.all.Select(SorterSuccessFraction.toString).ToArray(),
            ParameterValue.hint(parameterValueType.SorterSuccessFraction), text => { SorterSuccessFraction.fromString(text); return true; }),
        [ParamKeys.sorterCountKey.Name] = Minimum(1),
        [ParamKeys.replKey.Name] = Minimum(0),
        [ParamKeys.aMaxKey.Name] = Minimum(0),
        [ParamKeys.orthoRateKey.Name] = Msuf4Rate(),
        [ParamKeys.paraRateKey.Name] = Msuf4Rate(),
        [ParamKeys.selfSymRateKey.Name] = Msuf4Rate(),
        [ParamKeys.aInitialKey.Name] = new([], "Starting value for every SimpleR suffix activator; must be within [-AMax, AMax]. Only positive activators enable entries.",
            text => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)),
        [ParamKeys.activationProbabilityKey.Name] = new([], "Enter a probability between 0 and 1 (SimpleS only).",
            text => double.Parse(text, CultureInfo.InvariantCulture) is >= 0 and <= 1),
        [ParamKeys.rngTypeKey.Name] = Choice([rngType.Lcg, rngType.Net, rngType.Smx], RngType.toString, RngType.fromString),
        [ParamKeys.sortableDataFormatKey.Name] = Choice([sortableDataFormat.BoolArray, sortableDataFormat.IntArray, sortableDataFormat.PackedIntArray,
            sortableDataFormat.BitVector256, sortableDataFormat.BitVector512, sortableDataFormat.Int8Vector256, sortableDataFormat.Int8Vector512], SortableDataFormat.toString, SortableDataFormat.fromString),
        [ParamKeys.sorterEvalTypeKey.Name] = Choice([sorterEvalType.V1, sorterEvalType.V2, sorterEvalType.V3], SorterEvalType.toString, SorterEvalType.fromString),
        [ParamKeys.mergeSuffixTypeKey.Name] = Choice([mergeSuffixType.NoSuffix, mergeSuffixType.VV_1], MergeSuffixType.toString, MergeSuffixType.fromString)
    });
    private static Rule? Find(string key) => rules.Value.GetValueOrDefault(key);
    internal static string[] Choices(string key) => Find(key)?.Choices ?? [];
    internal static string Hint(string key, parameterValueType type) => Find(key)?.Hint ?? ParameterValue.hint(type);
    internal static string Error(string key, parameterValueType type, string text)
    {
        var error = ParameterValue.validationError(type, text);
        if (error.Length > 0) return error;
        if (Find(key) is not { } rule) return "";
        try { if (rule.IsValid(text)) return ""; }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException
            || exception.GetType() == typeof(Exception)) { } // F# failwith uses System.Exception.
        return $"{key}: {rule.Hint}";
    }
}
