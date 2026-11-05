using System.Windows;
using System.Windows.Controls;

namespace Sortopia.WPF.Views;

public partial class StageLayoutSelector : UserControl
{
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(int), typeof(StageLayoutSelector),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnModeChanged), value => value is int mode && mode is >= 0 and <= 3);
    public int Mode { get => (int)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public static readonly DependencyProperty HasTestResultsProperty = DependencyProperty.Register(nameof(HasTestResults), typeof(bool), typeof(StageLayoutSelector), new PropertyMetadata(false));
    public bool HasTestResults { get => (bool)GetValue(HasTestResultsProperty); set => SetValue(HasTestResultsProperty, value); }

    public StageLayoutSelector() => InitializeComponent();
    private void ModeChecked(object sender, RoutedEventArgs e) => SetCurrentValue(ModeProperty, int.Parse((string)((RadioButton)sender).Tag));
    private static void OnModeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var selector = (StageLayoutSelector)sender;
        foreach (var button in new[] { selector.AllCes, selector.UsedStages, selector.UsedCes, selector.Compact })
            if (button is not null) button.SetCurrentValue(RadioButton.IsCheckedProperty, int.Parse((string)button.Tag) == (int)e.NewValue);
    }
}
