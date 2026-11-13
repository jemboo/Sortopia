using System.Globalization;
using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Models.Sorting;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Starting values for newly populated entries; existing bindings are never changed.</summary>
internal static class WorkspaceParameterInitialValues
{
    private const int DefaultChannels = 8;
    private static readonly string[] modelFunctionPrefixes = ["sorter-model-set.uniform.", "sorter-model-set.prefix.",
        "sorter-eval.standard.", "sorter-eval.merge.", "sorter-eval.prefix.", "sorter-eval-inputs.standard."];
    internal static bool IsModelFunction(string functionName) => modelFunctionPrefixes.Any(prefix => functionName.StartsWith(prefix, StringComparison.Ordinal));

    internal static int ChannelCount(IEnumerable<parameterBindingDto> values)
    {
        var entries = values.ToArray();
        var channel = entries.FirstOrDefault(value => value.keyName == ParamKeys.channelCountKey.Name);
        if (channel?.valueType == (int)parameterValueType.Integer
            && int.TryParse(channel.value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0) return count;
        foreach (var entry in entries)
        {
            var width = ParameterValue.tryParse((parameterValueType)entry.valueType, entry.value)?.Value switch
            {
                parameterValue.SorterLibIdValue id => id.Item.ChannelCount,
                parameterValue.PrefixLibIdValue id => id.Item.ChannelCount,
                parameterValue.MergeLibIdValue id => id.Item.ChannelCount,
                _ => 0
            };
            if (width > 0) return width;
        }
        return 0;
    }

    internal static string For(paramKeyDescriptor key, int channels, string functionName = "")
    {
        if (channels <= 0) channels = IsUf6(functionName) ? 12 : DefaultChannels;
        return key.Name switch
        {
            "ChannelCount" => channels.ToString(CultureInfo.InvariantCulture),
            "StageLength" => "1",
            "SorterCount" => "100",
            "Repl" => "0",
            "ExcludeSelfCe" => "true",
            "CollectSortableTests" => "false",
            "SortableDataFormat" => "Bools",
            "SorterModelType" => "Simple",
            "AMax" => "1",
            "ActivationProbability" => "0.5",
            "StageWeights" or "Seed4Weights" => "1,1,1",
            "Seed6Weights" => "1,1,1,1,1,1,1",
            "UnfoldingWeights" => string.Join(";", Enumerable.Repeat("1,1,1", UnfoldingLevels(channels, functionName))),
            "RngType" => "SplitMix64",
            "SorterEvalType" => "V1",
            _ => ForType(key, channels)
        };
    }

    internal static int SuggestedChannelCount(int channels, string functionName)
    {
        if (!IsModelFunction(functionName)) return channels;
        var model = SimpleSorterModelType.fromString(functionName[(functionName.LastIndexOf('.') + 1)..]);
        return channels > 1 && SimpleSorterModelType.supportsChannelCount(channels, model)
            ? channels : IsUf6(functionName) ? 12 : DefaultChannels;
    }

    private static int UnfoldingLevels(int channels, string functionName)
    {
        int seedWidth = IsUf6(functionName) ? 6 : 4;
        int levels = 0;
        for (long width = seedWidth; width < channels; width *= 2) ++levels;
        return levels;
    }

    private static bool IsUf6(string functionName) => IsModelFunction(functionName) && functionName.EndsWith(".msuf6", StringComparison.Ordinal);

    private static string ForType(paramKeyDescriptor key, int channels) => key.ValueType switch
    {
        parameterValueType.SorterLibId => SorterLibId.toString(SorterLib.ids()
            .OrderBy(id => id.ChannelCount == channels ? 0 : id.ChannelCount == DefaultChannels ? 1 : 2)
            .ThenBy(id => SorterLibId.toString(id), StringComparer.Ordinal).First()),
        parameterValueType.PrefixLibId => PrefixLibId.toString(PrefixLib.ids()
            .OrderBy(id => id.ChannelCount == channels ? 0 : id.ChannelCount == DefaultChannels ? 1 : 2)
            .ThenBy(id => id.StageLength).ThenBy(id => PrefixLibId.toString(id), StringComparer.Ordinal).First()),
        parameterValueType.MergeLibId => MergeLibId.toString(MergeChoices(channels)
            .OrderBy(id => id.MergeDimension == 2 ? 0 : 1).ThenBy(id => id.MergeDimension)
            .ThenBy(id => MergeLibId.toString(id), StringComparer.Ordinal).First()),
        parameterValueType.Integer => "1",
        parameterValueType.Unsigned64 or parameterValueType.Floating => "0",
        parameterValueType.Boolean => "false",
        parameterValueType.Guid => Guid.NewGuid().ToString("D"),
        parameterValueType.OpsWeights => "1,1,1",
        parameterValueType.Seed6Weights => "1,1,1,1,1,1,1",
        parameterValueType.UnfoldingWeights => "",
        _ => WorkspaceParameterRules.Choices(key.Name).FirstOrDefault() ?? ""
    };

    private static mergeLibId[] MergeChoices(int channels)
    {
        var choices = MergeLibId.availableForChannelCount(channels);
        return choices.Length > 0 ? choices : MergeLibId.availableForChannelCount(DefaultChannels);
    }
}
