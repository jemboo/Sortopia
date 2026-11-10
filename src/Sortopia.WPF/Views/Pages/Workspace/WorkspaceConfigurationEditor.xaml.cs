using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.ComponentModel;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceConfigurationEditor : UserControl
{
    private bool functionsReady;
    public static readonly DependencyProperty ActiveItemKeyProperty = DependencyProperty.Register(nameof(ActiveItemKey), typeof(string),
        typeof(WorkspaceConfigurationEditor), new PropertyMetadata("description", (owner, _) => ((WorkspaceConfigurationEditor)owner).ShowActiveItem()));
    public string ActiveItemKey { get => (string)GetValue(ActiveItemKeyProperty); set => SetValue(ActiveItemKeyProperty, value); }
    public WorkspaceConfigurationEditor()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is INotifyPropertyChanged oldValue) oldValue.PropertyChanged -= ConfigurationChanged;
            if (args.NewValue is INotifyPropertyChanged newValue) newValue.PropertyChanged += ConfigurationChanged;
            ShowActiveItem();
        };
        ShowActiveItem();
    }
    private void ConfigurationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if ((string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(WorkspaceConfigurationViewModel.FunctionsReady))
            && functionsReady != (DataContext as WorkspaceConfigurationViewModel)?.FunctionsReady) ShowActiveItem();
    }
    private void ShowActiveItem()
    {
        if (DescriptionSection is null) return;
        var configuration = DataContext as WorkspaceConfigurationViewModel;
        functionsReady = configuration?.FunctionsReady ?? false;
        var activeKey = configuration is { FunctionsReady: false }
            && ActiveItemKey is not ("description" or "resultsFunction" or "setupFunction" or "resultsDatabase" or "setupDatabase")
                ? configuration.IsBranch ? "setupFunction" : "resultsFunction" : ActiveItemKey;
        void Show(FrameworkElement element, params string[] keys)
        {
            var active = keys.Contains(activeKey);
            element.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active && element is Expander section) section.SetCurrentValue(Expander.IsExpandedProperty, true);
        }
        Show(FunctionsSection, "resultsFunction", "setupFunction");
        ResultsFunctionItem.Visibility = Visibility.Visible;
        SetupFunctionItem.Visibility = configuration?.IsBranch == true ? Visibility.Visible : Visibility.Collapsed;
        Show(DescriptionSection, "description");
        Show(ResultsSettingsSection, "resultsDatabase", "resultsTypes", "resultsQueryKeys");
        Show(ResultsDatabaseItem, "resultsDatabase");
        Show(ResultsTypesItem, "resultsTypes"); Show(ResultsQueryKeysItem, "resultsQueryKeys");
        Show(ResultsDefaultsSection, "resultsDefaults"); Show(ResultsSpansSection, "resultsSpans"); Show(ResultsPreviewSection, "resultsPreview");
        Show(SetupSettingsSection, "setupDatabase", "setupOutputs");
        Show(SetupDatabaseItem, "setupDatabase"); Show(SetupOutputsItem, "setupOutputs");
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
