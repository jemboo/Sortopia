using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Sortopia.WPF.Controls;

/// <summary>
/// A focusable, hit-testable group that reveals its buttons while hovered or focused within.
/// Buttons inherit the page's data context, so their commands remain bound to its view model.
/// </summary>
[ContentProperty(nameof(Buttons))]
public partial class TranslucentButtonStack : UserControl
{
    public ObservableCollection<Button> Buttons { get; } = [];

    public TranslucentButtonStack()
    {
        InitializeComponent();
    }
}
