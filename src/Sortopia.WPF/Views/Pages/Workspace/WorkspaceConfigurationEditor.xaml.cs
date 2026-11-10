using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceConfigurationEditor : UserControl
{
    public static readonly DependencyProperty ActiveItemKeyProperty = DependencyProperty.Register(nameof(ActiveItemKey), typeof(string),
        typeof(WorkspaceConfigurationEditor), new PropertyMetadata("description", (owner, _) => ((WorkspaceConfigurationEditor)owner).ShowActiveItem()));
    public string ActiveItemKey { get => (string)GetValue(ActiveItemKeyProperty); set => SetValue(ActiveItemKeyProperty, value); }
    public WorkspaceConfigurationEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ShowActiveItem();
        ShowActiveItem();
    }
    private void ShowActiveItem()
    {
        if (DescriptionSection is null) return;
        void Show(FrameworkElement element, params string[] keys)
        {
            var active = keys.Contains(ActiveItemKey);
            element.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active && element is Expander section) section.SetCurrentValue(Expander.IsExpandedProperty, true);
        }
        Show(DescriptionSection, "description");
        Show(ResultsSettingsSection, "resultsDatabase", "resultsFunction", "resultsTypes", "resultsQueryKeys");
        Show(ResultsDatabaseItem, "resultsDatabase"); Show(ResultsFunctionItem, "resultsFunction");
        Show(ResultsTypesItem, "resultsTypes"); Show(ResultsQueryKeysItem, "resultsQueryKeys");
        Show(ResultsDefaultsSection, "resultsDefaults"); Show(ResultsSpansSection, "resultsSpans"); Show(ResultsPreviewSection, "resultsPreview");
        Show(SetupSettingsSection, "setupDatabase", "setupFunction", "setupOutputs");
        Show(SetupDatabaseItem, "setupDatabase"); Show(SetupFunctionItem, "setupFunction"); Show(SetupOutputsItem, "setupOutputs");
        Show(SetupDefaultsSection, "setupDefaults"); Show(SetupSpansSection, "setupSpans"); Show(SetupPreviewSection, "setupPreview");
        ResultsSettingsSection.SetBinding(TagProperty, new Binding(ActiveItemKey switch
        {
            "resultsDatabase" => "ResultsDatabaseError", "resultsFunction" => "ResultsFunctionError",
            "resultsTypes" => "OutputTypesError", "resultsQueryKeys" => "ResultsQueryKeysError", _ => "ResultsSettingsError"
        }));
        SetupSettingsSection.SetBinding(TagProperty, new Binding(ActiveItemKey switch
        {
            "setupDatabase" => "SetupDatabaseError", "setupFunction" => "SetupFunctionError",
            "setupOutputs" => "SetupDataTypesError", _ => "SetupSettingsError"
        }));
    }
}
