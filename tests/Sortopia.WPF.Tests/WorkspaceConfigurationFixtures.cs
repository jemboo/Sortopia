using System.Diagnostics.CodeAnalysis;
using Sortopia.Workspaces.Serialization;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Tests;

internal static class WorkspaceConfigurationFixtures
{
    // Execution fixtures include project-only settings; module fixtures use only
    // their function definitions, defaults and source choices.
    internal static void LoadFixtureSettings([NotNull] this WorkspaceConfigurationViewModel? editor, projectSettingsDto value)
    {
        ArgumentNullException.ThrowIfNull(editor);
        if (!editor.IsModule) { editor.LoadProject(value); return; }
        editor.LoadModule(new moduleSettingsDto {
            description = value.description, resultsFunctionName = value.resultsFunctionName, resultsParameterDefaults = value.resultsParameterDefaults,
            setupFunctionName = value.setupFunctionName, setupParameterDefaults = value.setupParameterDefaults,
            setupSources = value.setupDataTypes?.Select(row => new setupSourceConfigDto { dataType = row.dataType, sourceProjectId = row.sourceProjectId }).ToArray()
        });
    }
}
