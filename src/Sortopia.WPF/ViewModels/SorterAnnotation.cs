using System.Windows;
using System.Windows.Media;

namespace Sortopia.WPF.ViewModels;

public sealed class SorterAnnotation(string name, Brush brush) : ViewModelBase
{
    private Point? position;
    private bool isShown = true;
    public string Name { get; } = name;
    public Brush Brush { get; } = brush;
    public string Label => Position is { } point ? $"{Name}: [{point.X:0}, {point.Y:0}]" : Name;
    public bool HasSorter => Position.HasValue;
    public Point? VisiblePosition => IsShown ? Position : null;
    public Point? Position
    {
        get => position;
        internal set
        {
            if (!SetProperty(ref position, value)) return;
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(HasSorter));
            OnPropertyChanged(nameof(VisiblePosition));
        }
    }
    public bool IsShown
    {
        get => isShown;
        set
        {
            if (SetProperty(ref isShown, value)) OnPropertyChanged(nameof(VisiblePosition));
        }
    }
}
