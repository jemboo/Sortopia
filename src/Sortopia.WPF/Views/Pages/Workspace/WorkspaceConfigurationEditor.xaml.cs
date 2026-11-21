using System.Windows.Controls;
using System.Windows;
using System.Windows.Data;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceConfigurationEditor : UserControl
{
    private bool configurationVisible;
    private bool reportingVisible;
    public static readonly DependencyProperty ActiveItemKeyProperty = DependencyProperty.Register(nameof(ActiveItemKey), typeof(string),
        typeof(WorkspaceConfigurationEditor), new PropertyMetadata("description", (owner, _) => ((WorkspaceConfigurationEditor)owner).ShowActiveItem()));
    public string ActiveItemKey { get => (string)GetValue(ActiveItemKeyProperty); set => SetValue(ActiveItemKeyProperty, value); }
    private void OnOpenFunctionParameter(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: WorkspaceFunctionParameter { CanNavigate: true } parameter }) return;
        // The button's command first selects the owning node and configuration section.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
        {
            if (!ReferenceEquals(DataContext, parameter.TargetEditor) || ActiveItemKey != parameter.ItemKey) return;
            for (DependencyObject? ancestor = this; ancestor is not null; ancestor = VisualTreeHelper.GetParent(ancestor))
                if (ancestor is WorkspacePage page) { page.RevealParameterSelection(); break; }
            UpdateLayout();
            if (parameter.IsSourceInput)
            {
                var sources = parameter.TargetEditor!.CanEditDefaults ? SetupDataTypesList : InheritedSetupDataTypesList;
                var source = sources.Items.Cast<WorkspaceSetupDataTypeEntry>()
                    .FirstOrDefault(row => row.DataType.Kind == parameter.SourceKind && row.DataType.Text == parameter.SourceText);
                if (source is not null && sources.ItemContainerGenerator.ContainerFromItem(source) is ContentPresenter sourceContainer)
                {
                    sourceContainer.BringIntoView(new Rect(0, 0, sourceContainer.ActualWidth, Math.Min(80, sourceContainer.ActualHeight)));
                    (sources.ItemTemplate.FindName("SetupSourceProjectInput", sourceContainer) as ComboBox)?.Focus();
                }
                return;
            }
            var span = parameter.TargetsSpan;
            ItemsControl rows = span ? parameter.IsSetup ? SetupParametersList : ResultsParametersList
                : parameter.TargetEditor!.CanEditDefaults ? parameter.IsSetup ? SetupParameterDefaultsList : ResultsParameterDefaultsList
                : parameter.IsSetup ? InheritedSetupParameterDefaultsList : InheritedResultsParameterDefaultsList;
            var item = rows.Items.Cast<object>().FirstOrDefault(value => (value switch
            {
                WorkspaceParameterSpan row => row.Key,
                WorkspaceParameterBinding row => row.Key,
                WorkspaceParameterBindingPreview row => row.Key,
                _ => null
            }) == parameter.Name);
            if (item is null || rows.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter container) return;
            container.BringIntoView(new Rect(0, 0, container.ActualWidth, Math.Min(32, container.ActualHeight)));
            if (!span)
            {
                (rows.ItemTemplate.FindName("DefaultValueEditor", container) as WorkspaceParameterValueEditor)?.FocusValue();
                return;
            }
            if (rows.ItemTemplate.FindName("ParameterValues", container) is ItemsControl values
                && values.ItemContainerGenerator.ContainerFromIndex(0) is ContentPresenter first)
                (values.ItemTemplate.FindName("ParameterValueEditor", first) as WorkspaceParameterValueEditor)?.FocusValue();
        }));
    }
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
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName is nameof(WorkspaceConfigurationViewModel.ShowFunctionConfiguration)
            or nameof(WorkspaceConfigurationViewModel.ShowReportingConfiguration))
        {
            var configuration = DataContext as WorkspaceConfigurationViewModel;
            if (configurationVisible != configuration?.ShowFunctionConfiguration || reportingVisible != configuration?.ShowReportingConfiguration)
                ShowActiveItem();
        }
    }
    private void ShowActiveItem()
    {
        if (DescriptionSection is null) return;
        var configuration = DataContext as WorkspaceConfigurationViewModel;
        configurationVisible = configuration?.ShowFunctionConfiguration ?? false;
        reportingVisible = configuration?.ShowReportingConfiguration ?? false;
        var activeKey = configuration is { ShowFunctionConfiguration: false }
            && ActiveItemKey is not ("description" or "resultsFunction" or "setupFunction" or "resultsDatabase" or "setupDatabase" or "evolutionReporting")
            && !(ActiveItemKey == "resultsDefaults" && configuration.HasParameterDefaults(false))
            && !(ActiveItemKey == "setupDefaults" && configuration.HasParameterDefaults(true))
                ? configuration.IsBranch ? "setupFunction" : "resultsFunction" : ActiveItemKey;
        void Show(FrameworkElement element, params string[] keys)
        {
            var active = keys.Contains(activeKey);
            element.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            if (active && element is Expander section) section.SetCurrentValue(Expander.IsExpandedProperty, true);
        }
        Show(FunctionsSection, "resultsFunction", "setupFunction");
        FunctionsSection.Header = activeKey == "setupFunction" ? "Setup function" : "Results function";
        Show(ResultsFunctionItem, "resultsFunction");
        Show(SetupFunctionItem, "setupFunction");
        if (configuration?.IsBranch != true) SetupFunctionItem.Visibility = Visibility.Collapsed;
        Show(DescriptionSection, "description");
        Show(ResultsSettingsSection, "resultsDatabase", "resultsTypes", "resultsQueryKeys");
        Show(ResultsDatabaseItem, "resultsDatabase");
        Show(ResultsTypesItem, "resultsTypes");
        Show(ResultsDefaultsSection, "resultsDefaults"); Show(ResultsSpansSection, "resultsSpans");
        Show(SetupSettingsSection, "setupDatabase", "setupOutputs");
        Show(SetupDatabaseItem, "setupDatabase"); Show(SetupOutputsItem, "setupOutputs");
        Show(SetupDefaultsSection, "setupDefaults"); Show(SetupSpansSection, "setupSpans");
        Show(EvolutionReportingSection, "evolutionReporting");
        if (!reportingVisible) EvolutionReportingSection.Visibility = Visibility.Collapsed;
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
