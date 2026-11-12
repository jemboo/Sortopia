using System.Globalization;
using Sortopia.Runs;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Starting values for newly populated entries; existing bindings are never changed.</summary>
internal static class WorkspaceParameterInitialValues
{
    private const int DefaultChannels = 8;

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

    internal static string For(paramKeyDescriptor key, int channels)
    {
        if (channels <= 0) channels = DefaultChannels;
        return key.Name switch
        {
            "ChannelCount" => channels.ToString(CultureInfo.InvariantCulture),
            "StageLength" => "1",
            "SorterCount" => "100",
            "Repl" => "0",
            "ExcludeSelfCe" => "true",
            "CollectSortableTests" => "false",
            "SortableDataFormat" => "Bools",
            "SimpleSorterModelType" => "Mssi",
            "SorterModelType" => "Simple",
            "AMax" => "1",
            "ActivationProbability" => "0.5",
            "RngType" => "SplitMix64",
            "SorterEvalType" => "V1",
            _ => ForType(key, channels)
        };
    }

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
        _ => WorkspaceParameterRules.Choices(key.Name).FirstOrDefault() ?? ""
    };

    private static mergeLibId[] MergeChoices(int channels)
    {
        var choices = MergeLibId.availableForChannelCount(channels);
        return choices.Length > 0 ? choices : MergeLibId.availableForChannelCount(DefaultChannels);
    }
}
