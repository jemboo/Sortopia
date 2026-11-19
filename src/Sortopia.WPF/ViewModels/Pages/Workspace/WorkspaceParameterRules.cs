using Sortopia.Functions;
using Sortopia.Runs;

namespace Sortopia.WPF.ViewModels.Pages.Workspace;

/// <summary>Combine typed parsing with the selected function's editor policy.</summary>
internal static class WorkspaceParameterRules
{
    private static parameterEditorRule? Find(functionParameterSupport? support, string? key) =>
        key is not null && support is not null && support.EditorRules.TryGetValue(key, out var rule) ? rule : null;
    internal static string[] Choices(functionParameterSupport? support, string? key, parameterValueType type) =>
        Find(support, key)?.Choices.ToArray() ?? ParameterValue.choices(type);
    internal static string Hint(functionParameterSupport? support, string? key, parameterValueType type) =>
        Find(support, key)?.Hint ?? ParameterValue.hint(type);
    internal static string Error(functionParameterSupport? support, string? key, parameterValueType type, string text)
        => support is null || key is null ? ParameterValue.validationError(type, text)
            : FunctionParameterSupport.valueError(support, key, type, text);
}
