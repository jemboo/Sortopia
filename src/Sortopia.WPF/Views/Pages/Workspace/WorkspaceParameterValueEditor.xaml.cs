using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sortopia.WPF.ViewModels.Pages.Workspace;

namespace Sortopia.WPF.Views.Pages.Workspace;

public partial class WorkspaceParameterValueEditor : UserControl
{
    private WorkspaceTypedValue? value;
    private bool refreshing;
    public WorkspaceParameterValueEditor()
    {
        InitializeComponent();
        ChoiceValueInput.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnChoiceTextChanged));
        DataContextChanged += (_, _) => Attach();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (value is not null) value.PropertyChanged -= ValueChanged; value = null; };
    }
    private void Attach()
    {
        if (value is not null) value.PropertyChanged -= ValueChanged;
        value = DataContext as WorkspaceTypedValue;
        if (value is not null) value.PropertyChanged += ValueChanged;
        RefreshChoices();
    }
    private void ValueChanged(object? sender, PropertyChangedEventArgs args) => RefreshChoices();
    private void RefreshChoices()
    {
        // Replacing a WPF ComboBox's choices clears its selection/text. Suppress
        // that writeback while changing keys so the user's draft stays intact.
        if (refreshing) return;
        refreshing = true;
        try
        {
            ChoiceValueInput.ItemsSource = value?.Choices;
            ChoiceValueInput.Text = value?.Text ?? "";
        }
        finally { refreshing = false; }
    }
    private void OnChoiceTextChanged(object sender, TextChangedEventArgs args)
    {
        if (!refreshing && value?.HasChoices == true && args.OriginalSource is TextBox input) value.Text = input.Text;
    }
    private void OnChoiceSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!refreshing && value?.HasChoices == true && ChoiceValueInput.SelectedItem is string choice) value.Text = choice;
    }
}
