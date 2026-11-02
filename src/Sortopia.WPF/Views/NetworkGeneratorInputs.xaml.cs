using System.Windows;
using System.Windows.Controls;

namespace Sortopia.WPF.Views;

public partial class NetworkGeneratorInputs : UserControl
{
    public static readonly DependencyProperty GenerateLabelProperty = DependencyProperty.Register(
        nameof(GenerateLabel), typeof(string), typeof(NetworkGeneratorInputs), new PropertyMetadata("Generate"));
    public string GenerateLabel { get => (string)GetValue(GenerateLabelProperty); set => SetValue(GenerateLabelProperty, value); }

    public static readonly DependencyProperty SeedHintProperty = DependencyProperty.Register(
        nameof(SeedHint), typeof(string), typeof(NetworkGeneratorInputs), new PropertyMetadata("New random seed"));
    public string SeedHint { get => (string)GetValue(SeedHintProperty); set => SetValue(SeedHintProperty, value); }

    public NetworkGeneratorInputs() => InitializeComponent();
}
