using System.Globalization;
using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Sorting.Catalog;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Starting values for newly populated entries; existing bindings are never changed.</summary>
internal static class WorkspaceParameterInitialValues
{
    private const int DefaultChannels = 8;

    internal static parameterBindingDto[] CreateMissing(parameterBindingDto[] current, parameterBindingDto[] related,
        paramKeyDescriptor[] missing, functionParameterSupport? support)
    {
        // Invalid existing entries remain editable; they must not prevent suggesting other defaults.
        static IEnumerable<parameterBinding> ValidBindings(IEnumerable<parameterBindingDto> values)
        {
            foreach (var value in values)
            {
                parameterBinding binding;
                try { binding = ParameterBindingDto.toDomain(value); }
                catch (ArgumentException) { continue; }
                yield return binding;
            }
        }
        var channels = ChannelCount(current);
        if (channels == 0) channels = ChannelCount(related);
        var context = new parameterDefaultContext(ListModule.OfSeq(ValidBindings(current)),
            ListModule.OfSeq(ValidBindings(related)), ListModule.OfSeq(missing));
        var suggested = support?.Defaults.Invoke(context).ToDictionary(binding => binding.KeyName);
        return missing.Select(key => new parameterBindingDto {
            keyName = key.Name, valueType = (int)key.ValueType,
            value = suggested?.TryGetValue(key.Name, out var binding) == true
                ? binding.ValueType == key.ValueType ? binding.FormattedValue
                    : throw new ArgumentException($"The default provider returned the wrong type for '{key.Name}'.")
                : For(key, channels)
        }).ToArray();
    }

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
            "SorterSuccessFraction" => "99%",
            "SorterCount" => "100",
            "Repl" => "0",
            "InitialPoolSeed" => "0",
            "InitialPoolSelection" => "Random",
            "ExcludeSelfCe" => "true",
            "CollectSortableTests" => "false",
            "SortableDataFormat" => "Bools",
            "AMax" => "1",
            "AInitial" => "0",
            "ActivationProbability" => "0.5",
            "OrthoRate" or "ParaRate" or "SelfSymRate" => "1",
            "StageWeights" or "Seed4Weights" => "1,1,1",
            "Seed6Weights" => "1,1,1,1,1,1,1",
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
