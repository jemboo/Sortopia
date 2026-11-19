using Microsoft.FSharp.Collections;
using Sortopia.Runs;
using Sortopia.Functions;
using Sortopia.Workspaces.Serialization;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Starting values for newly populated entries; existing bindings are never changed.</summary>
internal static class WorkspaceParameterInitialValues
{
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
        var context = new parameterDefaultContext(ListModule.OfSeq(ValidBindings(current)),
            ListModule.OfSeq(ValidBindings(related)), ListModule.OfSeq(missing));
        var suggested = support?.Defaults.Invoke(context).ToDictionary(binding => binding.KeyName);
        return missing.Select(key => new parameterBindingDto {
            keyName = key.Name, valueType = (int)key.ValueType,
            value = suggested?.TryGetValue(key.Name, out var binding) == true
                ? binding.ValueType == key.ValueType ? binding.FormattedValue
                    : throw new ArgumentException($"The default provider returned the wrong type for '{key.Name}'.")
                : PrimitiveDefault(key.ValueType)
        }).ToArray();
    }

    // Unknown providers get only primitive editor values; domain defaults belong to function providers.
    private static string PrimitiveDefault(parameterValueType type) => type switch
    {
        parameterValueType.Integer => "1",
        parameterValueType.Unsigned64 or parameterValueType.Floating => "0",
        parameterValueType.Boolean => "false",
        parameterValueType.Guid => Guid.NewGuid().ToString("D"),
        _ => ""
    };
}
